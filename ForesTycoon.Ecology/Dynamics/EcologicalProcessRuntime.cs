using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal enum EcologicalPhase { Preparation, Environment, Vegetation, Finalization }

    [Flags]
    internal enum EcologicalBoundary { None = 0, EnvironmentStep = 1, ForestMonth = 2 }

    internal readonly record struct EcologicalStep(ulong Index, double StartSeconds, double DeltaSeconds,
        EcologicalBoundary Boundaries)
    {
        internal double EndSeconds => StartSeconds + DeltaSeconds;
        internal bool EndsAt(EcologicalBoundary boundary) => (Boundaries & boundary) != 0;
    }

    internal sealed class EcologicalProcessDescriptor
    {
        internal string Id { get; }
        internal EcologicalPhase Phase { get; }
        internal ReadOnlySpan<string> Reads => reads;
        internal ReadOnlySpan<string> Writes => writes;
        private readonly string[] reads, writes;

        internal EcologicalProcessDescriptor(string id, EcologicalPhase phase, string[] reads = null, string[] writes = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
            Id = id; Phase = phase;
            this.reads = CopyFields(reads); this.writes = CopyFields(writes);
        }

        private static string[] CopyFields(string[] fields)
        {
            if (fields == null) return Array.Empty<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string field in fields)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(field);
                if (!unique.Add(field)) throw new ArgumentException($"Duplicate field '{field}'.", nameof(fields));
            }
            return (string[])fields.Clone();
        }
    }

    internal interface IEcologicalProcess
    {
        EcologicalProcessDescriptor Descriptor { get; }
        // A process may request an exact calendar boundary. Infinity means no additional subdivision.
        EcologicalBoundary Boundary => EcologicalBoundary.None;
        double SecondsUntilBoundary => double.PositiveInfinity;
        void Advance(in EcologicalStep step);
    }

    /// <summary>Stable, multi-rate process execution without per-step allocations or render dependencies.</summary>
    internal sealed class EcologicalProcessRuntime
    {
        private readonly IEcologicalProcess[] processes;
        private readonly double[] boundaries;
        private double remainder;
        private bool running, faulted;
        internal double StepSeconds { get; }
        internal ulong CompletedSteps { get; private set; }
        internal double TimeSeconds { get; private set; }
        internal double PendingSeconds => remainder;
        internal bool IsFaulted => faulted;
        internal RuntimeCheckpoint Capture()
        {
            if (running || faulted) throw new InvalidOperationException("Cannot snapshot an executing or faulted runtime.");
            return new(TimeSeconds, remainder, CompletedSteps);
        }
        internal void Restore(RuntimeCheckpoint state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (running || faulted) throw new InvalidOperationException("Cannot restore an executing or faulted runtime.");
            CheckpointGuard.NonNegative(state.Time, "runtime time");
            CheckpointGuard.Require(double.IsFinite(state.PendingSeconds) && state.PendingSeconds >= -1e-9 &&
                state.PendingSeconds < StepSeconds, "runtime remainder");
            CheckpointGuard.Require(Math.Abs(state.Time - state.CompletedSteps * StepSeconds) <= Math.Max(1, state.Time) * 1e-9, "runtime step count");
            TimeSeconds = state.Time; remainder = state.PendingSeconds; CompletedSteps = state.CompletedSteps;
        }

        internal EcologicalProcessRuntime(double stepSeconds, IEcologicalProcess[] processes, params string[] initialFields)
        {
            if (!double.IsFinite(stepSeconds) || stepSeconds <= 1e-10) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            ArgumentNullException.ThrowIfNull(processes);
            if (processes.Length == 0) throw new ArgumentException("At least one ecological process is required.", nameof(processes));
            StepSeconds = stepSeconds;
            var ordered = new List<IEcologicalProcess>(processes.Length);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var process in processes)
            {
                ArgumentNullException.ThrowIfNull(process);
                ArgumentNullException.ThrowIfNull(process.Descriptor);
                if (!ids.Add(process.Descriptor.Id)) throw new ArgumentException($"Duplicate process '{process.Descriptor.Id}'.");
                if ((process.Boundary & ~EcologicalBoundary.ForestMonth) != 0)
                    throw new ArgumentException($"Invalid process boundary: {process.Descriptor.Id}.");
                // Stable insertion keeps registration order within the same phase.
                int index = ordered.Count;
                while (index > 0 && ordered[index - 1].Descriptor.Phase > process.Descriptor.Phase) index--;
                ordered.Insert(index, process);
            }
            this.processes = ordered.ToArray();
            boundaries = new double[processes.Length];
            ValidateFields(initialFields ?? Array.Empty<string>());
        }

        private void ValidateFields(string[] initialFields)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (string field in initialFields) { ArgumentException.ThrowIfNullOrWhiteSpace(field); available.Add(field); }
            var writers = new Dictionary<(EcologicalPhase, string), string>();
            foreach (var process in processes)
            {
                var descriptor = process.Descriptor;
                foreach (string field in descriptor.Reads)
                    if (!available.Contains(field)) throw new ArgumentException($"Process '{descriptor.Id}' reads unavailable field '{field}'.");
                foreach (string field in descriptor.Writes)
                {
                    if (!writers.TryAdd((descriptor.Phase, field), descriptor.Id))
                        throw new ArgumentException($"Multiple writers for '{field}' in phase {descriptor.Phase}.");
                    available.Add(field);
                }
            }
        }

        internal void Update(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(remainder + seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (running) throw new InvalidOperationException("Ecological process execution cannot be nested.");
            if (faulted) throw new InvalidOperationException("The ecological runtime failed; restore a validated world before continuing.");
            if (seconds == 0) return;
            remainder += seconds;
            running = true;
            try
            {
                // Preserve the established game's fixed-step tolerance and fractional remainder.
                while (remainder + 1e-10 >= StepSeconds)
                {
                    remainder -= StepSeconds;
                    double remaining = StepSeconds;
                    while (remaining > 1e-10)
                    {
                        double dt = remaining;
                        for (int i = 0; i < processes.Length; i++)
                        {
                            double until = processes[i].SecondsUntilBoundary;
                            if (double.IsNaN(until) || until <= 0)
                                throw new InvalidOperationException($"Process '{processes[i].Descriptor.Id}' has a non-positive calendar boundary.");
                            if (double.IsFinite(until) && processes[i].Boundary == EcologicalBoundary.None)
                                throw new InvalidOperationException("A finite calendar boundary needs an explicit boundary type.");
                            boundaries[i] = until;
                            dt = Math.Min(dt, until);
                        }
                        if (dt <= 1e-10) throw new InvalidOperationException("Calendar boundary cannot make numerical progress.");
                        var flags = dt == remaining ? EcologicalBoundary.EnvironmentStep : EcologicalBoundary.None;
                        for (int i = 0; i < processes.Length; i++)
                            if (boundaries[i] == dt) flags |= processes[i].Boundary;
                        var step = new EcologicalStep(CompletedSteps, TimeSeconds, dt, flags);
                        foreach (var process in processes) process.Advance(step);
                        TimeSeconds += dt;
                        remaining -= dt;
                    }
                    CompletedSteps++;
                }
            }
            catch { faulted = true; throw; }
            finally { running = false; }
        }
    }
}
