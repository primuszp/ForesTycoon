#nullable enable

using System;
using System.Collections.Generic;

namespace ForesTycoon;

/// <summary>
/// Accumulates flow transfers in compiled slots and publishes only touched entries.
/// Publication keeps slot order; totals from removed flows survive checkpoint round trips.
/// </summary>
internal sealed class RuleFlowLedger
{
    private readonly List<string> names = new();
    private readonly Dictionary<string, double> totals = new(StringComparer.Ordinal);
    private readonly List<int> dirtySlots = new(64);
    private double[] pending = Array.Empty<double>();
    private bool[] dirty = Array.Empty<bool>();

    internal IReadOnlyDictionary<string, double> Totals => totals;

    /// <summary>Registers a flow during compilation; <see cref="Prepare"/> allocates the final work arrays.</summary>
    internal int Register(string name)
    {
        int slot = names.IndexOf(name);
        if (slot < 0)
        {
            slot = names.Count;
            names.Add(name);
        }
        return slot;
    }

    internal void Prepare()
    {
        pending = new double[names.Count];
        dirty = new bool[names.Count];
    }

    internal void BeginStep()
    {
        for (int slot = 0; slot < pending.Length; slot++)
            totals.TryGetValue(names[slot], out pending[slot]);
    }

    internal void Record(int slot, double moved)
    {
        if (!(moved > 0)) return;
        pending[slot] += moved;
        if (dirty[slot]) return;
        dirty[slot] = true;
        dirtySlots.Add(slot);
    }

    internal void Publish()
    {
        if (dirtySlots.Count == 0) return;
        dirtySlots.Sort();
        foreach (int slot in dirtySlots)
        {
            totals[names[slot]] = pending[slot];
            dirty[slot] = false;
        }
        dirtySlots.Clear();
    }

    internal Dictionary<string, double>? Capture() => totals.Count == 0 ? null : new(totals, StringComparer.Ordinal);

    internal void Restore(Dictionary<string, double>? saved)
    {
        if (saved == null) return;
        foreach (var (name, total) in saved)
        {
            CheckpointGuard.Require(double.IsFinite(total), "flow ledger");
            totals[name] = total;
        }
    }
}
