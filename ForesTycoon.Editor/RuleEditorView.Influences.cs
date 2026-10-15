using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private int selectedInfluence, selectedTerm, influencePreviewId = -1;
    private float influencePreviewYears = .1f;

    private void InfluenceArea(GameWorld world, WorldRule rule)
    {
        ImGui.BeginChild("influence-editor", Vec2.Zero, ImGuiChildFlags.Borders);
        ImGui.PushTextWrapPos(0);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vec2(8, 4));
        bool changed = false;
        string name = rule.Name;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##name", ref name, 120) && !string.IsNullOrWhiteSpace(name)) rule.Name = name;
        changed |= ImGui.IsItemDeactivatedAfterEdit();
        EditorStyle.Text(HudTheme.Muted, "Építs kapcsolatokat a gráfon; a kijelölt hatás görbéjét és feltételeit jobbra szerkesztheted.");
        var scope = RuleEngine.Scope(rule.Scope);
        ImGui.SetNextItemWidth(230);
        if (ImGui.BeginCombo("Mely objektumokon?", Words(rule.Scope))) {
            foreach (var s in RuleEngine.Scopes.Where(s => s.Entity != null && (s.Fields.Any(f => f.Write != null) || s.Actions.Length > 0)))
                if (ImGui.Selectable(Words(s.Id), s.Id == rule.Scope) && s.Id != rule.Scope) {
                    rule.Scope = s.Id; scope = s; rule.UseLegacyRoadOverride = false;
                    // Keep existing effects intact: incompatible bindings remain visible and require a deliberate replacement.
                    changed = true;
                }
            ImGui.EndCombo();
        }
        EditorStyle.Text(HudTheme.Muted, "Minden szabálylépésben. Az éves hatásokat a játék az eltelt idővel arányosítja.");
        if (rule.Id is "forest.stumps" or "trail.age" && ImGui.Button("Alapértelmezett szabály visszaállítása"))
            ResetVisualRule(world, rule.Id);
        var readable = scope.Fields.Select(f => f.Name).ToArray();
        var writable = scope.Fields.Where(f => f.Write != null).Select(f => f.Name).ToArray();
        bool Pick(string label, string current, string[] choices, Action<string> set, bool empty = false)
            => ObjectFieldPicker(label, rule.Scope, current, choices, set, empty);
        void Number(string label, double value, Action<double> set)
        {
            bool finished = false;
            if (CurveNumber(label, ref value, 0, Math.Max(1, Math.Abs(value) * 2), ref finished)) set(value);
            changed |= finished;
        }

        var legacy = behaviors.Document.Graphs.Any(g => g.Hook == "road.weather" && g.Kind == "road"
            && g.Nodes.Find(n => n.Id == g.Output)?.Operation != BehaviorOperation.Native);
        if (rule.UseLegacyRoadOverride && legacy) {
            EditorStyle.Text(HudTheme.AmberAccent, "Ez a projekt régi útromlási felülírást is tartalmaz. A saját kapcsolatok használatához kapcsold ki itt.");
            if (ImGui.Button("Csak az itt összeállított hatások fussanak")) { rule.UseLegacyRoadOverride = false; changed = true; }
        }

        ImGui.Separator();
        if (ruleBookError != null) EditorStyle.Text(HudTheme.Bad, ruleBookError);
        if (ImGui.BeginTable("graph-workspace", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) {
        ImGui.TableSetupColumn("Gráf", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Hatás", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableNextColumn();
        DrawInfluenceGraph(rule, scope, ref changed);
        InfluencePreviewArea(world, rule, scope, legacy);
        StumpComparisonArea(world, rule);
        ImGui.TableNextColumn();
        ImGui.BeginChild("graph-inspector", new Vec2(0, Math.Max(440, ImGui.GetContentRegionAvail().Y - 40)), ImGuiChildFlags.Borders);
        if (rule.Effects.Count > 0) {
            selectedInfluence = Math.Clamp(selectedInfluence, 0, rule.Effects.Count - 1);
            var effect = rule.Effects[selectedInfluence]; var influence = effect.Influence;
            changed |= Pick(effect.Mode == "action" ? "Mely művelet fusson?" : "Mire hasson?", effect.Target, effect.Mode == "action" ? scope.Actions.Select(a => a.Name).ToArray() : writable, v => { effect.Target = v; rule.UseLegacyRoadOverride = false; });
            if (effect.Mode == "action") ImGui.TextWrapped("Pozitív eredménynél végrehajtódik a művelet. Eltávolítás után az objektum további hatásai leállnak.");
            else {
                int mode = effect.Mode == "set" ? 1 : 0;
                ImGui.TextUnformatted("Hatás módja"); ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##effect-mode", ref mode, new[] { "Változtassa az értéket", "Állítsa erre az értékre" }, 2)) {
                    effect.Mode = mode == 1 ? "set" : "add"; influence.PerYear = mode == 0; changed = true;
                }
            }
            bool yearly = influence.PerYear;
            if (effect.Mode == "add" && ImGui.Checkbox("Éves változási ütem", ref yearly)) { influence.PerYear = yearly; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Bekapcsolva: erősség / erdőév, a játék időarányosít. Kikapcsolva: erősség minden szabálylépésben.");
            if (ImGui.SmallButton("Cél törlése")) { rule.Effects.RemoveAt(selectedInfluence); rule.UseLegacyRoadOverride = false; changed = true; }
            else {
                if (effect.Mode != "action") ImGui.TextWrapped("A kapcsolatok hatása összeadódik. Egy kapcsolaton belül a források szorzói összeszorzódnak.");
                if (influence.Terms.Count < 16 && ImGui.Button(effect.Mode == "action" ? "+ Kiváltó kapcsolat" : "+ Állandó hatás")) {
                    influence.Terms.Add(new() { Decrease = effect.Mode != "action", Enabled = effect.Mode != "action" });
                    selectedTerm = influence.Terms.Count - 1; changed = true;
                }
                if (influence.Terms.Count > 0) {
                    selectedTerm = Math.Clamp(selectedTerm, 0, influence.Terms.Count - 1);
                    var term = influence.Terms[selectedTerm];
                    ImGui.Separator();
                    string termName = term.Name;
                    ImGui.SetNextItemWidth(-1); if (ImGui.InputText("##connection-name", ref termName, 100)) term.Name = termName;
                    changed |= ImGui.IsItemDeactivatedAfterEdit();
                    bool enabled = term.Enabled;
                    if (ImGui.Checkbox("Kapcsolat aktív", ref enabled)) { term.Enabled = enabled; changed = true; }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Kapcsolat törlése")) { influence.Terms.RemoveAt(selectedTerm); changed = true; }
                    else {
                        if (effect.Mode != "action" || ImGui.CollapsingHeader("Kapcsolat súlya és iránya")) {
                        int direction = term.Decrease ? 1 : 0;
                        ImGui.SetNextItemWidth(160);
                        if (ImGui.Combo("Hogyan?", ref direction, new[] { "Növelje", "Csökkentse" }, 2)) { term.Decrease = direction == 1; changed = true; }
                        bool fromField = term.AmountField.Length > 0;
                        if (ImGui.Checkbox("Erősség egy projektparaméterből", ref fromField)) {
                            if (!fromField && term.AmountField.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(term.AmountField[7..]) is { } spec) term.Amount = world.Tuning[spec.Key];
                            term.AmountField = fromField ? "tuning." + GameTuning.Specs[0].Id : ""; changed = true;
                        }
                        if (fromField) changed |= Pick("Erősség forrása", term.AmountField, GameTuning.Specs.Select(s => "tuning." + s.Id).ToArray(), v => term.AmountField = v);
                        else Number(effect.Mode == "action" ? "Feltétel súlya" : effect.Mode == "set" ? "Érték szorzója" : influence.PerYear ? "Erősség / erdőév" : "Erősség / lépés", term.Amount, v => term.Amount = Math.Max(0, v));
                        }
                        if (ImGui.CollapsingHeader($"Feltételek: {(term.Conditions.Count == 0 ? "mindig" : $"mind a(z) {term.Conditions.Count} teljesüljön")}##predicate", term.Conditions.Count > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) {
                            ImGui.TextWrapped("Egy kapcsolaton belül minden feltételnek teljesülnie kell. Külön kapcsolatokkal több lehetséges kiváltó ok is megadható.");
                            for (int c = 0; c < term.Conditions.Count; c++) {
                                ImGui.PushID("condition-" + c);
                                var condition = term.Conditions[c];
                                string comparisonName = condition.Comparison switch { "less" => "kisebb, mint", "atMost" => "legfeljebb", "greater" => "nagyobb, mint", _ => "legalább" };
                                string thresholdName = condition.ValueField.Length > 0 ? FieldDisplay(rule.Scope, condition.ValueField) : condition.Value.ToString("G5");
                                if (ImGui.CollapsingHeader($"{c + 1}. {FieldDisplay(rule.Scope, condition.Field)} {comparisonName} {thresholdName}##condition-details")) {
                                changed |= Pick("Vizsgált tulajdonság", condition.Field, readable, v => condition.Field = v);
                                var comparisons = new[] { "less", "atMost", "greater", "atLeast" };
                                int comparison = Array.IndexOf(comparisons, condition.Comparison);
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.Combo("##comparison", ref comparison, new[] { "kisebb, mint", "legfeljebb", "nagyobb, mint", "legalább" }, 4)) { condition.Comparison = comparisons[comparison]; changed = true; }
                                bool linked = condition.ValueField.Length > 0;
                                if (ImGui.Checkbox("Határérték egy tulajdonságból vagy paraméterből", ref linked)) {
                                    if (!linked && condition.ValueField.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(condition.ValueField[7..]) is { } spec) condition.Value = world.Tuning[spec.Key];
                                    condition.ValueField = linked ? "tuning.SkidTrailOvergrowYears" : ""; changed = true;
                                }
                                if (linked) changed |= Pick("Határérték forrása", condition.ValueField,
                                    readable.Concat(GameTuning.Specs.Select(s => "tuning." + s.Id)).ToArray(), v => condition.ValueField = v);
                                else Number("Határérték", condition.Value, v => condition.Value = v);
                                if (ImGui.SmallButton("Feltétel törlése")) { term.Conditions.RemoveAt(c--); changed = true; }
                                }
                                ImGui.PopID(); ImGui.Separator();
                            }
                            if (term.Conditions.Count < 8 && readable.Length > 0 && ImGui.Button("+ Feltétel")) {
                                term.Conditions.Add(new() { Field = readable.FirstOrDefault(f => f != "index") ?? readable[0] }); changed = true;
                            }
                        }
                        for (int f = 0; f < term.Factors.Count; f++) {
                            ImGui.PushID(f);
                            var factor = term.Factors[f];
                            ImGui.Separator();
                            changed |= Pick("Mi hasson rá?", factor.Source, readable, v => factor.Source = v);
                            var curve = factor.ToCurve();
                            bool finished = false;
                            if (CurveEditor("factor-" + f, curve, rule.Scope, readable, ref finished, bindings: false)) factor.SetCurve(curve);
                            changed |= finished;
                            ImGui.TextWrapped("A görbe kimenete szorzó: 0 = nincs hatás; 1 = teljes erősség; 2 = kétszeres.");
                            if (ImGui.SmallButton("Forrás eltávolítása")) { term.Factors.RemoveAt(f--); changed = true; }
                            ImGui.PopID();
                        }
                        if (term.Factors.Count < 8 && readable.Length > 0 && ImGui.Button("+ Másik forrás szorzóként")) {
                            term.Factors.Add(new() { Source = readable.FirstOrDefault(s => s != "index") ?? readable[0] }); changed = true;
                        }
                    }
                }
            }
        }
        ImGui.Separator();
        ImGui.EndChild(); ImGui.EndTable();
        }
        if (ImGui.Button("Másolat saját szabályként")) {
            var copy = RuleEngine.Clone(new() { rule })[0]; copy.Id = "rule-" + Guid.NewGuid().ToString("N")[..8];
            copy.Name += " (másolat)"; copy.BuiltIn = false; copy.Enabled = false; copy.UseLegacyRoadOverride = false;
            ruleBook.Add(copy); selectedBookRule = ruleBook.Count - 1; changed = true;
        }
        if (!rule.BuiltIn) {
            ImGui.SameLine(); if (ImGui.Button("Szabály törlése")) { ruleBook.Remove(rule); changed = true; }
        }
        if (changed) { if (!legacy) rule.UseLegacyRoadOverride = false; ApplyRuleBook(world, "Hatások módosítva."); }
        if (ruleBookError != null) EditorStyle.Text(HudTheme.Bad, ruleBookError);
        if (world.RuleBook.LastError != null) EditorStyle.Text(HudTheme.Bad, world.RuleBook.LastError);
        ImGui.Spacing();
        ImGui.PopStyleVar(); ImGui.PopTextWrapPos(); ImGui.EndChild();
    }
    private void InfluencePreviewArea(GameWorld world, WorldRule rule, RuleScope scope, bool legacy)
    {
        if (ImGui.CollapsingHeader("Próba a kiválasztott objektumon", rule.Scope == "stump" ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen)) {
            EditorStyle.Text(HudTheme.Muted, "Az aktív tesztvilág szabálya, változatlan környezettel. A próba nem ír a világba.");
            var elements = scope.Elements(world);
            if (!elements.Contains(influencePreviewId)) influencePreviewId = elements.FirstOrDefault(-1);
            if (influencePreviewId < 0) ImGui.TextUnformatted("Nincs ilyen objektum a tesztvilágban.");
            else {
                ImGui.SetNextItemWidth(150);
                if (ImGui.BeginCombo("Objektum", influencePreviewId.ToString())) {
                    foreach (int id in elements) if (ImGui.Selectable(id.ToString(), id == influencePreviewId)) influencePreviewId = id;
                    ImGui.EndCombo();
                }
                ImGui.SetNextItemWidth(200); ImGui.SliderFloat("Eltelt erdőév", ref influencePreviewYears, .01f, 1, "%.2f");
                var active = world.RuleBook.Rules.FirstOrDefault(r => r.Id == rule.Id && r.Scope == rule.Scope);
                if (active != null && active.Effects.All(e => e.Influence != null)) {
                    try { foreach (var row in world.RuleBook.PreviewInfluences(world, rule.Id, influencePreviewId, influencePreviewYears)) {
                        if (active.Effects.Any(e => e.Target == row.Target && e.Mode == "action")) {
                            ImGui.TextUnformatted(FieldDisplay(rule.Scope, row.Target) + (row.After > 0 ? ": végrehajtódna" : ": nem hajtódna végre")); continue;
                        }
                        ImGui.TextUnformatted($"{FieldDisplay(rule.Scope, row.Target)}: {row.Before:F5} -> {row.After:F5} (változás: {row.After - row.Before:F5})");
                        foreach (var part in row.Contributions) EditorStyle.Text(HudTheme.Muted, $"  {part.Name}: {part.Change:F5}");
                        if (active.UseLegacyRoadOverride && legacy) EditorStyle.Text(HudTheme.AmberAccent, "A végértéket a régi felülírás módosíthatja; a részértékek az itt összeállított kapcsolatokat mutatják.");
                    } }
                    catch (System.IO.InvalidDataException e) { EditorStyle.Text(HudTheme.Bad, e.Message); }
                } else ImGui.TextUnformatted("A szerkesztett szabály még nincs alkalmazva.");
            }
        }
    }
}
