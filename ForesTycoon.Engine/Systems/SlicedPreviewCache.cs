using System;
using System.Collections.Generic;

namespace ForesTycoon.Engine;

/// <summary>Owner-thread presentation queries, with a shared work budget and a bounded LRU cache.</summary>
internal sealed class SlicedPreviewCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private sealed class Entry(TKey key, Func<WorkResult<TValue>, IEnumerable<int>> factory)
    {
        internal readonly TKey Key = key;
        internal readonly WorkResult<TValue> Result = new();
        internal readonly Func<WorkResult<TValue>, IEnumerable<int>> Factory = factory;
        internal IEnumerator<int> Work;
        internal LinkedListNode<Entry> Recent, Pending;
        internal bool Ready;
        internal long CompletedFrame;
        internal long RequestedFrame;
    }
    private readonly Dictionary<TKey, Entry> entries = new();
    private readonly LinkedList<Entry> recent = new(), pending = new();
    private readonly int capacity, refreshFrames;
    private object version;
    private long frame;
    internal int Count => entries.Count;
    internal int LastWorkUnits { get; private set; }

    internal SlicedPreviewCache(int capacity = 256, int refreshFrames = 30)
    {
        if (capacity < 1 || refreshFrames < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity; this.refreshFrames = refreshFrames;
    }
    internal void BeginFrame(object revision)
    {
        if (!Equals(version, revision)) { Clear(); version = revision; }
        frame++;
    }
    internal bool Request(TKey key, Func<WorkResult<TValue>, IEnumerable<int>> factory, out TValue value)
    {
        if (entries.TryGetValue(key, out var entry) && entry.Ready && frame - entry.CompletedFrame >= refreshFrames) {
            Remove(entry); entry = null;
        }
        if (entry == null) {
            if (entries.Count == capacity) Remove(recent.First.Value);
            entry = new(key, factory); entries.Add(key, entry);
            entry.Recent = recent.AddLast(entry); entry.Pending = pending.AddLast(entry);
        } else { recent.Remove(entry.Recent); entry.Recent = recent.AddLast(entry); }
        entry.RequestedFrame = frame;
        value = entry.Result.Value; return entry.Ready;
    }
    internal void Advance(int budget)
    {
        if (budget < 1) throw new ArgumentOutOfRangeException(nameof(budget));
        LastWorkUnits = 0;
        while (pending.First != null && LastWorkUnits < budget) {
            var entry = pending.First.Value;
            if (entry.RequestedFrame != frame) { Remove(entry); continue; }
            entry.Work ??= entry.Factory(entry.Result).GetEnumerator();
            bool more;
            try { more = SimulationWork.MoveNext(entry.Work, budget - LastWorkUnits); }
            catch { Remove(entry); throw; }
            LastWorkUnits += more ? Math.Max(1, entry.Work.Current) : 1;
            pending.Remove(entry.Pending);
            if (more) entry.Pending = pending.AddLast(entry);
            else {
                entry.Pending = null; entry.Work.Dispose(); entry.Work = null;
                entry.Ready = true; entry.CompletedFrame = frame;
            }
        }
    }
    private void Remove(Entry entry)
    {
        entry.Work?.Dispose();
        if (entry.Pending != null) pending.Remove(entry.Pending);
        recent.Remove(entry.Recent); entries.Remove(entry.Key);
    }
    internal void Clear()
    {
        foreach (var entry in entries.Values) entry.Work?.Dispose();
        entries.Clear(); recent.Clear(); pending.Clear(); LastWorkUnits = 0;
    }
    public void Dispose() => Clear();
}
