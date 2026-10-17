using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ForesTycoon.Engine;

/// <summary>Joins a fixed number of bounded pure-query jobs without a synchronous completion path.</summary>
internal sealed class SlicedQueryBatch
{
    internal static int WorkerCount => Math.Min(8, Math.Max(1, System.Environment.ProcessorCount - 1));
    private readonly Task[] pending = new Task[8];
    internal bool Pending => Array.Exists(pending, task => task != null);

    internal void Start(int slot, Action execute)
    {
        if ((uint)slot >= pending.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        if (pending[slot] != null) throw new InvalidOperationException("Join a query slot before reusing it.");
        pending[slot] = Task.Run(execute);
    }
    internal IEnumerable<int> JoinSlotWork(int slot)
    {
        var task = pending[slot];
        if (task == null) yield break;
        while (!task.IsCompleted) yield return Math.Min(64, SimulationWork.MaximumUnits);
        pending[slot] = null;
        task.GetAwaiter().GetResult();
    }

    // Callers bound each worker's input; this helper never drains an entire map in one worker job.
    internal IEnumerable<int> RunWork(int count, Action<int> execute)
    {
        if (Pending) throw new InvalidOperationException("A query batch is already pending.");
        if (count < 1 || count > 8) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < count; i++) { int worker = i; Start(i, () => execute(worker)); yield return 1; }
        foreach (int unit in JoinWork()) yield return unit;
    }

    // Interrupted work retains its slots. Wait for every reader before propagating any failure.
    internal IEnumerable<int> JoinWork()
    {
        foreach (var task in pending) {
            if (task == null) continue;
            while (!task.IsCompleted) yield return Math.Min(64, SimulationWork.MaximumUnits);
        }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
        for (int i = 0; i < pending.Length; i++) {
            var task = pending[i]; pending[i] = null;
            try { task?.GetAwaiter().GetResult(); }
            catch (Exception error) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        }
        failure?.Throw();
    }
}
