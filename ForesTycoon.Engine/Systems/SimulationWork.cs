using System.Collections.Generic;

namespace ForesTycoon.Engine;

/// <summary>Deterministic suspension points. A yield completes one bounded unit of simulation work.</summary>
internal static class SimulationWork
{
    // Explicit synchronous adapter for headless tools and tests. Interactive hosts consume a bounded slice instead.
    internal static void Complete(IEnumerable<int> work)
    {
        foreach (int _ in work) { }
    }
}

/// <summary>A result slot passed through nested, resumable computations.</summary>
internal sealed class WorkResult<T>
{
    internal T Value;
}
