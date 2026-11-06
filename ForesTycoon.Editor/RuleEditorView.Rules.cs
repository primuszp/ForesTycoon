using System.Collections.Generic;
using ForesTycoon.Rules;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
internal sealed partial class RuleEditorView
{
    private readonly RuleWorkspaceView ruleWorkspace;
    internal void ShowRuleBook(int index = 0) => ruleWorkspace.ShowRuleBook(index);
    internal void ShowRuleCalculation(int index, int effect = -1) => ruleWorkspace.ShowRuleCalculation(index, effect);
    internal void ShowCommonRule(string key) => ruleWorkspace.ShowCommonRule(key);
    internal void AddResponseCurve(string hook) => ruleWorkspace.AddResponseCurve(hook);
    internal void ShowRuleLibrary() => ruleWorkspace.ShowRuleLibrary();
    internal void ShowThematicStock(string id, int topic) => ruleWorkspace.ShowThematicStock(id, topic);
    internal void ShowObject(string id) => ruleWorkspace.ShowObject(id);
    internal void ShowRuleOverview() => ruleWorkspace.ShowRuleOverview();
    internal void ResetVisualRule(GameWorld world, string ruleId) => ruleWorkspace.ResetVisualRule(world, ruleId);
    internal void RunStumpComparison(GameWorld world, WorldRule rule) => ruleWorkspace.RunStumpComparison(world, rule);
    internal Dictionary<string, Vec2> InfluenceGraphAnchors => ruleWorkspace.InfluenceGraphAnchors;
    internal Dictionary<string, string> InfluenceGraphSourceLabels => ruleWorkspace.InfluenceGraphSourceLabels;
    internal IReadOnlyList<string> InfluenceSentences => ruleWorkspace.InfluenceSentences;
    internal Dictionary<string, Vec2> RuleCardAnchors => ruleWorkspace.RuleCardAnchors;
    internal static string ResponseFamily(string kind) => RuleWorkspaceView.ResponseFamily(kind);
    internal static string ScheduleChip(string schedule) => RuleFieldCatalog.ScheduleChip(schedule);
    internal static readonly string[] EditableModes = RuleWorkspaceView.EditableModes;
    internal static IReadOnlyList<RuleFieldUse> FieldUses(IReadOnlyList<WorldRule> rules) => RuleWorkspaceView.FieldUses(rules);
}
