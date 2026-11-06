using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor;
internal sealed partial class RuleWorkspaceView
{
    private readonly RuleFieldPickerView fields;
    private readonly RuleCurveEditorView curves = new();
    private readonly RuleObjectBrowserView objects;
    private readonly RuleOverviewView overview;
    private bool ObjectFieldPicker(string label, string scope, string current, string[] choices, Action<string> set, bool empty = false)
        => fields.Draw(label, scope, current, choices, set, empty);
    internal static IReadOnlyList<RuleFieldUse> FieldUses(IReadOnlyList<WorldRule> rules) => RuleFieldAnalysis.Build(Objects, rules, KeyDisplay);
    internal void ShowObject(string id) { ShowPage(EditorPage.Objects); ui.Rules.SelectedObject = id; }
    internal void ShowRuleOverview() => ShowPage(EditorPage.Overview);
    internal void ShowRuleBook(int index = 0) => book.ShowRuleBook(index);
    internal void ShowRuleCalculation(int index, int effect = -1) => book.ShowRuleCalculation(index, effect);
    internal void ResetVisualRule(GameWorld world, string ruleId) => book.ResetVisualRule(world, ruleId);
    internal void RunStumpComparison(GameWorld world, WorldRule rule) => book.RunStumpComparison(world, rule);
    internal Dictionary<string, System.Numerics.Vector2> InfluenceGraphAnchors => book.InfluenceGraphAnchors;
    internal Dictionary<string, string> InfluenceGraphSourceLabels => book.InfluenceGraphSourceLabels;
    internal IReadOnlyList<string> InfluenceSentences => book.InfluenceSentences;
    internal Dictionary<string, System.Numerics.Vector2> RuleCardAnchors => cards.Anchors;
    private bool RuleCardEntry(string id, string name, string kind, bool selected, bool enabled, Action select, Action<bool> enable, string tooltip)
        => cards.Entry(id, name, kind, selected, enabled, select, enable, tooltip);
    internal static readonly string[] EditableModes = { "add", "set", "approach", "flow", "action" };
}
