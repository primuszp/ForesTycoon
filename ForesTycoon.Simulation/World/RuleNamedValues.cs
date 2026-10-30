#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ForesTycoon;

/// <summary>
/// Lazy named inputs of one logical step. Previews read live values without replacing the step's snapshot;
/// parallel flow queries share the first successful read. Only the owner begins or restores a step.
/// </summary>
internal sealed class RuleNamedValues
{
    private readonly Func<string, double> read;
    private readonly Dictionary<string, int> slots = new(StringComparer.Ordinal);
    private readonly object readLock = new();
    private double[] values = new double[8];
    private int[] stamps = new int[8];
    private int step = 1;

    internal RuleNamedValues(Func<string, double> read) => this.read = read;

    internal int Slot(string key)
    {
        if (slots.TryGetValue(key, out int slot)) return slot;
        slots[key] = slot = slots.Count;
        if (slot == values.Length)
        {
            Array.Resize(ref values, slot * 2);
            Array.Resize(ref stamps, slot * 2);
        }
        return slot;
    }

    internal void BeginStep() => step++;

    internal double Read(int slot, string key, bool preview, bool parallel)
    {
        if (parallel)
        {
            if (Volatile.Read(ref stamps[slot]) == step) return values[slot];
            lock (readLock)
            {
                if (stamps[slot] != step) ReadAndPublish(slot, key);
                return values[slot];
            }
        }
        if (preview) return read(key);
        if (stamps[slot] != step) ReadAndPublish(slot, key);
        return values[slot];
    }

    private void ReadAndPublish(int slot, string key)
    {
        values[slot] = read(key);
        Volatile.Write(ref stamps[slot], step);
    }

    internal Dictionary<string, double> Capture()
    {
        lock (readLock)
            return slots.OrderBy(p => p.Value).Where(p => stamps[p.Value] == step)
                .ToDictionary(p => p.Key, p => values[p.Value], StringComparer.Ordinal);
    }

    internal void Restore(Dictionary<string, double> saved)
    {
        foreach (var (key, value) in saved)
        {
            CheckpointGuard.Require(slots.ContainsKey(key), "rule step value");
            int slot = slots[key];
            values[slot] = value;
            stamps[slot] = step;
        }
    }
}
