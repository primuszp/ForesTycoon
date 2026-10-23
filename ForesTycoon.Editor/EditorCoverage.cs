using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

/// <summary>
/// Whether the editor reaches everything the game runs. Every behaviour hook, rule schedule, effect mode, tunable number and
/// editable catalog rule of the engine must have a page in the editor; anything the engine still does natively is listed,
/// not hidden. The smoke test fails on a gap, and <c>--check-editor-coverage</c> prints the report.
/// </summary>
internal static class EditorCoverage
{
    internal sealed record Report(List<string> Gaps, List<string> Native, int Hooks, int Tuning, int Editable);

    internal static Report Check(GameWorld world, BehaviorModel behaviors)
    {
        var gaps = new List<string>();
        var catalog = world.DescribeCurrentRules();

        // Hooks: a pure response is listed under a named topic on Összefüggések; other hooks on Folyamatkapcsolatok,
        // except the native-only tuning hooks, whose number is on Járművek / Játékparaméterek.
        foreach (var hook in WorldBehaviorPolicy.Hooks)
            if (EcologyResponseProgram.IsPureKind(hook.Kind) && RuleEditorView.ResponseFamily(hook.Kind) == hook.Kind)
                gaps.Add($"Összefüggés témacsoport nélkül: {hook.Id} ({hook.Kind})");
        foreach (var graph in behaviors.Graphs)
            if (!WorldBehaviorPolicy.Hooks.Any(h => h.Id == graph.Hook && h.Kind == graph.Kind))
                gaps.Add($"A projekt gráfja a motor által nem ismert hookra köt: {graph.Name} · {graph.Hook} ({graph.Kind})");

        // Schedules and effect modes of state rules.
        foreach (string schedule in WorldRule.Schedules)
            if (schedule != "step" && RuleEditorView.ScheduleChip(schedule) == RuleEditorView.ScheduleChip("step"))
                gaps.Add("Időzítés felirat nélkül: " + schedule);
        foreach (string mode in RuleEffect.Modes)
            if (!RuleEditorView.EditableModes.Contains(mode)) gaps.Add("Hatásmód szerkesztő nélkül: " + mode);

        // Every tunable number appears on Járművek or Játékparaméterek.
        var tunable = catalog.Rules.SelectMany(r => r.Parameters).Where(p => p.Tunable).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var spec in GameTuning.Specs)
            if (!tunable.Contains(spec.Id)) gaps.Add($"Hangolható szám, amely egyik oldalon sem jelenik meg: {spec.Id} ({spec.Name})");

        // Every catalog rule the engine marks editable has something to edit in the project.
        var rules = world.RuleProjectDocument.Rules;
        int editable = 0;
        var native = new List<string>();
        foreach (var rule in catalog.Rules) {
            switch (rule.Execution) {
                case GameRuleExecution.NativeCode: native.Add($"{rule.Name} ({rule.Id})"); continue;
                case GameRuleExecution.Presentation: continue;
            }
            editable++;
            bool owned = rules.Any(r => r.Id == rule.Id || r.Template == rule.Id)
                || WorldBehaviorPolicy.Hooks.Any(h => RuleWorkshopModel.HookBelongsTo(h, rule) && behaviors.Graphs.Any(g => g.Hook == h.Id && g.Kind == h.Kind))
                || rule.Execution == GameRuleExecution.EditableController
                || rule.Parameters.Any(p => p.Tunable);
            if (!owned) gaps.Add($"Szerkeszthetőnek jelölt motorszabály, amelyhez nincs szerkeszthető elem: {rule.Name} ({rule.Id})");
        }
        return new Report(gaps, native, WorldBehaviorPolicy.Hooks.Count(), GameTuning.Specs.Length, editable);
    }

    internal static string Format(Report r)
    {
        var lines = new List<string> {
            $"Editor–motor lefedettség: {r.Hooks} hook, {r.Tuning} hangolható szám, {r.Editable} szerkeszthető motorszabály.",
            r.Gaps.Count == 0 ? "Hiány: nincs." : $"Hiány: {r.Gaps.Count}"
        };
        lines.AddRange(r.Gaps.Select(g => "  - " + g));
        lines.Add($"Motoroldali (nem szerkeszthető) szabályok: {r.Native.Count}");
        lines.AddRange(r.Native.Select(n => "  · " + n));
        return string.Join(Environment.NewLine, lines);
    }
}
