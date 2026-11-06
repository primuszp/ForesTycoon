#nullable enable
using System;
using System.Collections.Generic;

namespace ForesTycoon.Editor.Core;

/// <summary>Bounded document history, independent of ImGui and the simulation.</summary>
internal sealed class EditorHistory<T> where T : notnull
{
    internal sealed record Entry(T State, string Summary);
    private readonly List<Entry> entries = new();
    private readonly int limit;
    internal EditorHistory(int limit = 100) => this.limit = limit > 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    internal int Index { get; private set; } = -1;
    internal int Count => entries.Count;
    internal bool CanUndo => Index > 0;
    internal bool CanRedo => Index >= 0 && Index < entries.Count - 1;
    internal Entry this[int index] => entries[index];
    internal Entry? Current => Index >= 0 ? entries[Index] : null;

    internal bool Record(T state, string summary)
    {
        if (Current != null && EqualityComparer<T>.Default.Equals(Current.State, state)) return false;
        entries.RemoveRange(Index + 1, entries.Count - Index - 1);
        entries.Add(new(state, summary));
        if (entries.Count > limit) entries.RemoveAt(0);
        Index = entries.Count - 1;
        return true;
    }

    internal void MoveTo(int index)
    {
        if ((uint)index >= (uint)entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
    }

    internal void Clear() { entries.Clear(); Index = -1; }
}
