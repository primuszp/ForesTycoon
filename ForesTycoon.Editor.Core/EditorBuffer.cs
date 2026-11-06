#nullable enable
using System;

namespace ForesTycoon.Editor.Core;

/// <summary>A mutable working model with isolated snapshots and bounded, before-edit history.</summary>
internal sealed class EditorBuffer<T> where T : notnull
{
    private readonly Func<T, string> serialize;
    private readonly Func<string, T> deserialize;
    private readonly EditorHistoryService<string> history;
    internal T Value { get; private set; }
    internal event Action? Changing;
    internal event Action? Replaced;
    internal event Action? EditStarted;
    internal event Action<bool>? EditFinished;

    internal EditorBuffer(T initial, Func<T, string> serialize, Func<string, T> deserialize, int historyLimit = 100)
    {
        this.serialize = serialize; this.deserialize = deserialize;
        Value = deserialize(serialize(initial));
        history = new(() => serialize(Value), Restore, changing: () => Changing?.Invoke(), limit: historyLimit);
        history.EditStarted += () => EditStarted?.Invoke();
        history.EditFinished += cancelled => EditFinished?.Invoke(cancelled);
        history.Reset();
    }

    internal T Capture() => deserialize(serialize(Value));

    internal void Load(T value)
    {
        // Parsing must succeed before replacing either the model or its history.
        var next = deserialize(serialize(value));
        Value = next;
        history.Reset();
        Replaced?.Invoke();
    }

    internal void Remember()
    {
        history.Checkpoint();
        Changing?.Invoke();
    }

    internal bool Undo() => history.Undo();
    internal bool Redo() => history.Redo();
    internal EditorEdit BeginEdit(string summary) => history.BeginEdit(summary);
    internal bool CommitEdit() => history.CommitEdit();
    internal void Execute(string summary, Action<T> change)
        => history.Execute(summary, () => { change(Value); Changing?.Invoke(); });

    private void Restore(string snapshot)
    {
        var next = deserialize(snapshot);
        Changing?.Invoke();
        Value = next;
        Replaced?.Invoke();
    }
}
