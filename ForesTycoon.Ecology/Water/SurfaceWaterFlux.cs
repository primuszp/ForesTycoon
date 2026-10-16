using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal readonly struct SurfaceRunoffStep
    {
        private readonly double retainedWater, releasedFraction;
        internal SurfaceRunoffStep(double retainedWater, double releasedFraction)
        {
            if (!double.IsFinite(retainedWater) || retainedWater < 0) throw new ArgumentOutOfRangeException(nameof(retainedWater));
            if (!double.IsFinite(releasedFraction) || releasedFraction < 0 || releasedFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(releasedFraction));
            this.retainedWater = retainedWater; this.releasedFraction = releasedFraction;
        }
        internal double Amount(double available)
        {
            if (!double.IsFinite(available) || available < 0) throw new ArgumentOutOfRangeException(nameof(available));
            return Math.Min(available, Math.Max(0, available - retainedWater) * releasedFraction);
        }
    }

    /// <summary>
    /// Stages one downhill flux per source cell. Incoming water is committed after every source
    /// has been evaluated, so it cannot traverse several cells in a single step. Equal-area cell
    /// depths conserve volume; only explicit outlets export water. No per-step allocations.
    /// </summary>
    internal sealed class SurfaceWaterFlux
    {
        private readonly double[] delta;
        private readonly bool[] scheduled;
        private bool active;

        internal SurfaceWaterFlux(int cells)
        {
            if (cells <= 0) throw new ArgumentOutOfRangeException(nameof(cells));
            delta = new double[cells]; scheduled = new bool[cells];
        }

        internal void BeginStep()
        {
            if (active) throw new InvalidOperationException("Commit or cancel the previous surface flux step.");
            Array.Clear(delta); Array.Clear(scheduled); active = true;
        }
        internal IEnumerable<int> BeginStepWork()
        {
            if (active) throw new InvalidOperationException("Commit or cancel the previous surface flux step.");
            active = true;
            for (int id = 0; id < delta.Length; id++) { delta[id] = 0; scheduled[id] = false; yield return 1; }
        }

        // Returns exported depth, allowing the caller to accumulate its existing ordered water ledger.
        internal double Schedule(int source, int destination, bool outlet, double available, SurfaceRunoffStep step)
        {
            if (!active) throw new InvalidOperationException("Begin a surface flux step first.");
            if ((uint)source >= (uint)delta.Length) throw new ArgumentOutOfRangeException(nameof(source));
            if (destination < -1 || destination >= delta.Length || destination == source)
                throw new ArgumentOutOfRangeException(nameof(destination));
            if (scheduled[source]) throw new InvalidOperationException("A surface cell can send water only once per step.");
            double amount = step.Amount(available);
            scheduled[source] = true;
            if (destination >= 0) { delta[source] -= amount; delta[destination] += amount; return 0; }
            if (!outlet) return 0;
            delta[source] -= amount; return amount;
        }

        internal void Commit(Span<double> surface)
        {
            if (!active) throw new InvalidOperationException("There is no active surface flux step.");
            if (surface.Length != delta.Length) throw new ArgumentException("Surface raster length mismatch.", nameof(surface));
            // Validate the whole commit before mutating any cell; never clamp away a balance error.
            for (int id = 0; id < surface.Length; id++)
                if (!double.IsFinite(surface[id]) || surface[id] < 0 ||
                    !double.IsFinite(surface[id] + delta[id]) || surface[id] + delta[id] < 0)
                    throw new InvalidOperationException("Surface water changed below its scheduled outgoing flux.");
            for (int id = 0; id < surface.Length; id++) surface[id] += delta[id];
            active = false;
        }

        internal IEnumerable<int> CommitWork(double[] surface)
        {
            if (!active || surface.Length != delta.Length) throw new InvalidOperationException("Invalid surface flux commit.");
            for (int id = 0; id < surface.Length; id++) {
                if (!double.IsFinite(surface[id]) || surface[id] < 0 ||
                    !double.IsFinite(surface[id] + delta[id]) || surface[id] + delta[id] < 0)
                    throw new InvalidOperationException("Surface water changed below its scheduled outgoing flux.");
                yield return 1;
            }
            for (int id = 0; id < surface.Length; id++) { surface[id] += delta[id]; yield return 1; }
            active = false;
        }

        internal void Cancel() { Array.Clear(delta); Array.Clear(scheduled); active = false; }
    }
}
