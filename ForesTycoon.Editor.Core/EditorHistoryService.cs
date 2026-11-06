#nullable enable
using System;
using System.Collections.Generic;

namespace ForesTycoon.Editor.Core;

internal abstract class EditorEdit : IDisposable
{
    internal abstract bool IsActive { get; }
    internal abstract void Apply(Action change);
    internal abstract bool Commit();
    internal abstract bool Cancel();
    public void Dispose() => Cancel();
}

internal sealed record EditorHistoryMove<T>(T Before, T After, string Summary);

/// <summary>Shared snapshot navigation and explicit edit boundaries, independent of models, UI and runtime.</summary>
internal sealed class EditorHistoryService<T> where T : notnull
{
    private readonly Func<T> capture;
    private readonly Action<T> restore;
    private readonly Action? changing;
    private readonly Func<T, T, string> describe;
    private readonly EditorHistory<T> history;
    private EditToken? active;
    internal event Action? Recorded;
    internal event Action? EditStarted;
    internal event Action<bool>? EditFinished;
    internal bool IsEditing => active != null;
    internal bool CanUndo => history.CanUndo;
    internal bool CanRedo => history.CanRedo;
    internal int Count => history.Count;
    internal string UndoSummary => history.Current?.Summary ?? "";
    internal string RedoSummary => history.CanRedo ? history[history.Index + 1].Summary : "";
    internal EditorHistoryMove<T>? LastMove { get; private set; }

    internal EditorHistoryService(Func<T> capture, Action<T> restore, Func<T, T, string>? describe = null,
        Action? changing = null, int limit = 100)
    {
        this.capture = capture; this.restore = restore; this.changing = changing;
        this.describe = describe ?? ((_, _) => ""); history = new(limit);
    }

    internal void Reset()
    {
        // Loading a document abandons old handles without restoring their previous document.
        bool editing = active != null;
        active = null; LastMove = null; history.Clear(); Record(capture(), "Megnyitás");
        if (editing) EditFinished?.Invoke(false);
    }

    internal bool Checkpoint()
    {
        if (IsEditing) return false;
        var next = capture();
        return Record(next, history.Current is { } current ? describe(current.State, next) : "Megnyitás");
    }

    internal EditorEdit BeginEdit(string summary)
    {
        if (IsEditing) throw new InvalidOperationException("Előbb zárd le a folyamatban lévő szerkesztést.");
        Checkpoint();
        var edit = new EditToken(this, capture(), summary); active = edit;
        try { EditStarted?.Invoke(); }
        catch { active = null; throw; }
        return edit;
    }

    internal bool Edit(string summary, Action change)
    {
        using var edit = BeginEdit(summary);
        edit.Apply(change); return edit.Commit();
    }

    internal bool CommitEdit() => active?.Commit() == true;

    /// <summary>Joins the current gesture, or records a standalone command when no gesture is open.</summary>
    internal void Execute(string summary, Action change)
    {
        if (active != null) active.ApplyCommand(change);
        else {
            using var edit = BeginEdit(summary);
            ((EditToken)edit).ApplyCommand(change);
            edit.Commit();
        }
    }

    internal TResult Execute<TResult>(string summary, Func<TResult> change)
    {
        TResult result = default!;
        Execute(summary, () => { result = change(); });
        return result;
    }

    internal bool CancelEdit() => active?.Cancel() == true;

    internal bool Undo()
    {
        CommitEdit(); Checkpoint();
        return history.CanUndo && Move(history.Index - 1, history.Current!.Summary);
    }

    internal bool Redo()
    {
        bool edited = CommitEdit();
        if (Checkpoint() || edited) return false;
        return history.CanRedo && Move(history.Index + 1, history[history.Index + 1].Summary);
    }

    private bool Record(T state, string summary)
    {
        if (!history.Record(state, summary)) return false;
        Recorded?.Invoke(); return true;
    }

    private bool Move(int index, string summary)
    {
        var before = history.Current!.State; var after = history[index].State;
        restore(after); history.MoveTo(index);
        LastMove = new(before, after, summary); return true;
    }

    private bool Finish(EditToken edit, bool cancel)
    {
        if (!ReferenceEquals(active, edit)) return false;
        var after = capture(); bool changed = !EqualityComparer<T>.Default.Equals(edit.Before, after);
        active = null;
        try {
            if (cancel && changed) restore(edit.Before);
            else if (!cancel && changed) Record(after, edit.Summary);
        } catch { active = edit; throw; }
        EditFinished?.Invoke(cancel); return changed;
    }

    private sealed class EditToken(EditorHistoryService<T> owner, T before, string summary) : EditorEdit
    {
        internal T Before { get; } = before;
        internal string Summary { get; } = summary;
        internal override bool IsActive => ReferenceEquals(owner.active, this);
        internal override void Apply(Action change)
        {
            if (!IsActive) throw new InvalidOperationException("Ez a szerkesztés már lezárult.");
            try { owner.changing?.Invoke(); change(); }
            catch { Cancel(); throw; }
        }
        internal override bool Commit() => owner.Finish(this, false);
        internal override bool Cancel() => owner.Finish(this, true);
        // Commands validate first and invalidate their owning model only after a successful mutation.
        internal void ApplyCommand(Action change)
        {
            try { change(); }
            catch { Cancel(); throw; }
        }
    }
}
