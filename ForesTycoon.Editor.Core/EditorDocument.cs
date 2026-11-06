#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>The authored state and its revision cache. UI selection and runtime state do not belong here.</summary>
internal sealed class EditorDocument
{
    internal string Name { get; set; } = "ForesTycoon szabályrendszer";
    internal WorldScenario? World { get; set; }
    internal EditorTuning Tuning { get; } = new();
    private readonly EditorBuffer<RuleAuthoring> authoring = new(new(),
        value => JsonSerializer.Serialize(value), json => JsonSerializer.Deserialize<RuleAuthoring>(json)!);
    internal RuleAuthoring Authoring { get => authoring.Value; set => authoring.Load(value); }
    internal BehaviorEditorState Behaviors { get; } = new();
    internal DynamicsEditorState Dynamics { get; } = new();
    internal RuleBookEditor RuleBook { get; }
    internal EditorDocumentHistory History { get; }
    internal EditorSceneCommands Scene { get; }
    internal EditorAuthoringCommands Library { get; }
    internal AlgorithmGraphReference? Algorithms { get; set; }
    internal List<WorldRule> Rules { get; set; } = new();
    internal List<RuleFieldDefinition> Fields { get; set; } = new();
    internal List<string> RemovedStarters { get; set; } = new();
    internal long Version { get; private set; }
    private string? revision;

    internal EditorDocument()
    {
        RuleBook = new(this);
        History = new(this);
        Scene = new(this);
        Library = new(this);
        Tuning.Changed += Invalidate;
        authoring.Changing += Invalidate; authoring.Replaced += Invalidate;
        Behaviors.Changing += Invalidate; Behaviors.Replaced += Invalidate;
        Dynamics.Changing += Invalidate; Dynamics.Replaced += Invalidate;
    }

    internal void RememberAuthoring() => authoring.Remember();
    internal void EditAuthoring(Action<RuleAuthoring> change) => authoring.Execute("sablontár módosítása", change);
    internal bool UndoAuthoring() => authoring.Undo();
    internal bool RedoAuthoring() => authoring.Redo();

    internal void Invalidate() { Version++; revision = null; }

    internal void Load(RuleProject project)
    {
        var copy = project.Clone();
        // Validate tuning before replacing any authored state.
        _ = GameTuning.FromOverrides(copy.Tuning);
        Name = copy.Name; World = copy.World; Authoring = copy.Authoring ?? new();
        Tuning.Replace(copy.Tuning);
        Algorithms = copy.Algorithms; Rules = copy.Rules; Fields = copy.Fields; RemovedStarters = copy.RemovedStarters;
        Behaviors.LoadDraft(copy.Behaviors); Dynamics.Load(copy.Dynamics);
        Invalidate();
        History.Reset();
    }

    internal string Revision() => revision ??= RevisionOf(Capture());

    internal RuleProject Capture()
        => new RuleProject {
            Name = Name, Tuning = Tuning.Capture(), Behaviors = Behaviors.Draft, Dynamics = Dynamics.Draft, Algorithms = Algorithms,
            Rules = Rules, Fields = Fields, RemovedStarters = RemovedStarters,
            Authoring = Authoring, World = World
        }.Clone();

    internal static string RevisionOf(RuleProject project) => project.ContentRevision() + ":"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project.Authoring ?? new()))));

    /// <summary>Only the preview uses the editor's compiled algorithms. The authored document keeps its reference.</summary>
    internal static RuleProject ForSandbox(RuleProject project)
    {
        var sandbox = project.Clone(); sandbox.Algorithms = null; return sandbox;
    }
}
