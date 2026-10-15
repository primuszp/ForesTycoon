using System;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private StumpRuleTrial.Result stumpTrial;
    private string stumpTrialRevision, stumpTrialError;

    internal void ResetVisualRule(GameWorld world, string ruleId)
    {
        var rules = RuleBookRules(world);
        int index = rules.FindIndex(r => r.Id == ruleId);
        if (index < 0) return;
        var example = RuleEngine.BuiltIn().Find(r => r.Id == ruleId);
        if (example == null) return;
        rules[index].Effects = example.Effects; rules[index].Description = example.Description;
        rules[index].Condition = ""; rules[index].Enabled = true; rules[index].Scope = example.Scope;
        ShowRuleBook(index); selectedInfluence = selectedTerm = 0;
        stumpTrial = null; stumpTrialError = null;
        ApplyRuleBook(world, "Alapértelmezett szabály visszaállítva.");
    }

    internal void RunStumpComparison(GameWorld world, WorldRule rule)
    {
        try {
            stumpTrial = StumpRuleTrial.Run(CollectProject(world), rule.Id);
            stumpTrialRevision = JsonSerializer.Serialize(rule); stumpTrialError = null;
        } catch (System.IO.InvalidDataException e) { stumpTrialError = e.Message; }
    }

    private void StumpComparisonArea(GameWorld world, WorldRule rule)
    {
        if (rule.Scope != "stump") return;
        if (!ImGui.CollapsingHeader("Nedvesség és átmérő · négy próbatönk", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.TextWrapped("Külön tesztvilág, négy friss tönk. A nedvesség rögzített; csak ez a tönkszabály fut, a játék szabályfuttatójával.");
        if (ImGui.Button("Összehasonlítás futtatása")) RunStumpComparison(world, rule);
        if (stumpTrialError != null) EditorStyle.Text(HudTheme.Bad, stumpTrialError);
        if (stumpTrial == null) return;
        if (stumpTrialRevision != JsonSerializer.Serialize(rule))
            EditorStyle.Text(HudTheme.AmberAccent, "A szabály megváltozott. Az eredmény frissítéséhez futtasd újra a próbát.");
        if (ImGui.BeginTable("stump-comparison", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) {
            foreach (string heading in new[] { "Próbatönk", "Nedv. / átm.", "1 év után", "Eltűnés" }) ImGui.TableSetupColumn(heading);
            ImGui.TableHeadersRow();
            foreach (var row in stumpTrial.Cases) {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Moisture:P0} / {row.Diameter * 100:F0} cm");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.OneYearProgress:P1}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.RemovedAtYears is double at ? $"{at:F2} év" : $"> {stumpTrial.DurationYears:F0} év");
            }
            ImGui.EndTable();
        }
        ImGui.TextWrapped($"Az idők erdőévben értendők. Lépésköz: {stumpTrial.StepYears:F2} év; legfeljebb {stumpTrial.DurationYears:F0} évig fut. Szemléltető beállítás, nem mért biológiai modell.");
    }
}
