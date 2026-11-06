#nullable enable
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>Template metadata is readable; reusable graphs leave the library only as independent copies.</summary>
internal sealed class RuleTemplateView(RuleTemplate model)
{
    public string Id => model.Id;
    public string Name => model.Name;
    public bool HasGraph => model.Graph != null;
    public bool SupportsObjects => model.Controller != null || WorldBehaviorPolicy.Hooks.FirstOrDefault(hook => hook.Id == model.Graph?.Hook
        && hook.Kind == model.Graph?.Kind)?.SupportsObjects == true;
    internal RuleTemplate Capture() => new RuleProject { Authoring = new() { Templates = new() { model } } }.Clone().Authoring!.Templates[0];
}

internal sealed class RuleLibrarySession
{
    private readonly EditorAuthoringCommands library;
    private readonly EditorReadCache<RuleTemplate, RuleTemplateView> templates = new(template => new(template));
    internal EditorReadList<RuleTemplateView> Templates { get; }
    internal RuleLibrarySession(EditorAuthoringCommands library)
    {
        this.library = library;
        Templates = new(() => library.Templates.Count, index => templates.View(library.Templates[index])!);
    }
    internal bool Undo() => library.Undo();
    internal bool Redo() => library.Redo();
    internal void Store(BehaviorGraph graph) => library.Store(graph);
    internal void Store(BehaviorController controller) => library.Store(controller);
    internal void SetTopic(string id, int topic) => library.SetTopic(id, topic);
    internal void Rename(RuleTemplateView template, string name) => library.Rename(templates.Model(template), name);
    internal void Delete(RuleTemplateView template) => library.Delete(templates.Model(template));
    internal string Export() => library.Export();
    internal void Import(string json) => library.Import(json);
}
