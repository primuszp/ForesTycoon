#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>Owns behavior drafts, local history and validation. It has no dependency on a UI or a sandbox clock.</summary>
internal sealed class BehaviorEditorState
{
    private readonly EditorBuffer<BehaviorModel> buffer = new(new(), model => model.ToJson(), BehaviorModel.FromJson);
    private Func<string, double> read = _ => 0;
    private string? validatedJson;
    internal BehaviorModel Draft => buffer.Value;
    internal BehaviorHook[] Hooks { get; private set; } = WorldBehaviorPolicy.Hooks;
    internal HashSet<string> NamedKeys { get; private set; } = new(SimulationWorld.RuleWorldFields);
    internal CompiledBehaviorModel? Compiled { get; private set; }
    internal string? Error { get; private set; }
    internal BehaviorGraphEditor Graphs { get; }
    internal BehaviorControllerEditor Controllers { get; }
    internal event Action? Changing;
    internal event Action? Replaced;
    internal event Action? EditStarted;
    internal event Action<bool>? EditFinished;

    internal BehaviorEditorState()
    {
        Graphs = new(this); Controllers = new(this);
        buffer.Changing += () => Changing?.Invoke();
        buffer.Replaced += () => { ClearValidation(); Replaced?.Invoke(); };
        buffer.EditStarted += () => EditStarted?.Invoke();
        buffer.EditFinished += cancelled => EditFinished?.Invoke(cancelled);
    }

    internal void Configure(BehaviorHook[] hooks, HashSet<string> keys, Func<string, double> reader)
    {
        if (!NamedKeys.SetEquals(keys) || !Hooks.SequenceEqual(hooks)) {
            Hooks = (BehaviorHook[])hooks.Clone(); NamedKeys = new(keys); ClearValidation();
        }
        // Compiled expressions read through this indirection, so switching the value source needs no recompilation.
        read = reader;
    }

    internal BehaviorModel Capture() => buffer.Capture();
    internal void LoadDraft(BehaviorModel value) => buffer.Load(value);
    internal void Load(BehaviorModel value)
    {
        _ = new CompiledBehaviorModel(value, Hooks, NamedKeys, key => read(key));
        var copy = value.Clone(); copy.Version = 3; buffer.Load(copy);
    }
    internal void Remember() => buffer.Remember();
    internal bool Undo() => buffer.Undo();
    internal bool Redo() => buffer.Redo();
    internal EditorEdit BeginEdit(string summary) => buffer.BeginEdit(summary);
    internal bool CommitEdit() => buffer.CommitEdit();

    internal RuleTemplate Instantiate(RuleTemplate template, ulong? objectId)
    {
        // Clone and check the template before changing the draft or discarding its redo history.
        var instance = AuthoringOperations.PrepareInstance(template, objectId);
        Remember(); AuthoringOperations.InstallInstance(Draft, instance); return instance;
    }

    internal void Validate()
    {
        // Layout drags update mutable nodes between Remember calls; compare content before reusing validation.
        string json = Draft.ToJson();
        if (validatedJson == json) return;
        validatedJson = json;
        try { Compiled = new(Draft, Hooks, NamedKeys, key => read(key)); Error = null; }
        catch (Exception error) { Compiled = null; Error = error.Message; }
    }

    private void ClearValidation() { validatedJson = null; Compiled = null; Error = null; }
}
