using System;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>One rule list and workspace for object effects, hook graphs, controllers, stocks and flows.</summary>
internal sealed partial class RuleWorkspaceView
{
    private ref string activeCommonKey => ref ui.Rules.ActiveCommonKey;
    private bool pendingCommonChange;
    private ref string commonSelection => ref ui.Rules.CommonSelection;
    internal void ShowCommonRule(string key) { if (commonSelection != key) scrollRuleCard = true; commonSelection = key; ShowPage(CommonPage(key)); lastCommon[page] = key; }
    internal void AddResponseCurve(string hook)
    {
        var graph = behaviors.Document.Graphs.Single(g => g.Hook == hook);
        behaviors.SelectGraph(graph.Id); behaviors.AddOutputCurve();
        ShowCommonRule("behavior:" + graph.Id); pendingCommonChange = true;
    }

    private void DrawCommonSelection(GameWorld world)
    {
        EditorStyle.BeginPanel("common-rule-workspace", Vec2.Zero);
        string before = new RuleProject { Behaviors = behaviors.Document, Dynamics = dynamicsEditor.Document }.ToJson();
        string key = commonSelection;
        if (key == "library") library.Draw();
        else if (key.StartsWith("controllerId:", StringComparison.Ordinal)) {
            if (activeCommonKey != key) behaviors.SelectControllerById(key[13..]); behaviors.DrawEmbedded(world);
        } else if (key.StartsWith("behavior:", StringComparison.Ordinal)) {
            behaviors.Configure(WorldBehaviorPolicy.Hooks, dynamicsEditor.NamedValues(), world.ReadRuleValue);
            if (activeCommonKey != key) behaviors.SelectGraph(key[9..]);
            var selected = behaviors.Document.Graphs.FirstOrDefault(g => g.Id == key[9..]);
            behaviors.SourceDisplay = field => FieldTitle(selected?.Kind ?? "world", field);
            behaviors.PickField = (label, current, choices, set) => ObjectFieldPicker(label, selected?.Kind ?? "world", current, choices, set);
            if (selected != null) {
                if (EcologyResponseProgram.IsPureKind(selected.Kind)) ResponseRuleCard(selected);
                else ProcessRuleCard(selected);
            }
            behaviors.DrawEmbedded(world);
        } else if (key.StartsWith("controller:", StringComparison.Ordinal)) {
            if (activeCommonKey != key) behaviors.SelectController(key[11..]); behaviors.DrawEmbedded(world);
        } else {
            dynamicsEditor.EquationEditor.SourceDisplay = field => FieldTitle("world", field);
            dynamicsEditor.DrawSelected(world, key, world.ReadRuleValue, (label, current, choices, set) => ObjectFieldPicker(label, "world", current, choices, set));
        }
        string after = new RuleProject { Behaviors = behaviors.Document, Dynamics = dynamicsEditor.Document }.ToJson();
        activeCommonKey = key;
        pendingCommonChange |= before != after;
        if (pendingCommonChange && !ImGui.IsAnyItemActive()) { Try(() => ApplyProject(world, false)); pendingCommonChange = false; }
        ImGui.EndChild();
    }

}
