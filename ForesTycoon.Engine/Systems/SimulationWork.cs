using System;
using System.Collections.Generic;

namespace ForesTycoon.Engine;

/// <summary>Deterministic suspension points. A yield completes one bounded unit of simulation work.</summary>
internal static class SimulationWork
{
    [ThreadStatic] private static int sliceDepth, maximumUnits;
    internal static int MaximumUnits => Math.Max(1, maximumUnits);

    /// <summary>Guards nested work against accidentally draining a synchronous adapter.</summary>
    internal static bool MoveNext(IEnumerator<int> work, int budget = 1)
    {
        if (sliceDepth != 0) return work.MoveNext();
        sliceDepth = 1; maximumUnits = Math.Max(1, budget);
        try { return work.MoveNext(); }
        finally { sliceDepth = 0; maximumUnits = 0; }
    }

    internal static void RequireSynchronousContext()
    {
        if (sliceDepth != 0)
            throw new InvalidOperationException("Synchronous completion inside sliced work is forbidden. Consume the nested work with yield instead.");
    }

    // Explicit synchronous adapter for headless tools and tests. Interactive hosts consume a bounded slice instead.
    internal static void Complete(IEnumerable<int> work)
    {
        RequireSynchronousContext();
        foreach (int _ in work) { }
    }
}

/// <summary>A result slot passed through nested, resumable computations.</summary>
internal sealed class WorkResult<T>
{
    internal T Value;
}
