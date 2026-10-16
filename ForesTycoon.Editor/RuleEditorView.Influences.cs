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
        WorldRuleCard(world, rule);
        bool changed = false;
        var scope = RuleEngine.Scope(rule.Scope);
        if (ImGui.CollapsingHeader("Név, futási idő és egyéb beállítások")) {
            string name = rule.Name;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##name", ref name, 120) && !string.IsNullOrWhiteSpace(name)) rule.Name = name;
            changed |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.SetNextItemWidth(230);
            if (ImGui.BeginCombo("Mely objektumokon fusson?", ObjectDisplay(rule.Scope, rule.Scope))) {
                foreach (var s in RuleEngine.Scopes.Where(s => s.Entity != null && (s.Fields.Any(f => f.Write != null) || s.Actions.Length > 0))) {
                    if (ImGui.Selectable(ObjectDisplay(s.Id, s.Id) + "##" + s.Id, s.Id == rule.Scope) && s.Id != rule.Scope) {
                        SetRuleScope(rule, s.Id); scope = s;
                        // Keep existing effects intact: incompatible bindings remain visible and require a deliberate replacement.
                        changed = true;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip((Objects.Find(s.Id)?.Help ?? "") + "\nKód: " + s.Id);
                }
                ImGui.EndCombo();
            }
            DrawSchedule(rule, ref changed);
            if (ImGui.SmallButton("Teljesülési feltétel / számítási csomópontok")) { editRuleCalculation = true; selectedCalculation = -1; }
            if (rule.ConditionGraph != null || rule.Condition.Length > 0) ImGui.TextWrapped("Ez a szabály csak a teljesülési feltétel igaz állapotában hat. A feltétel a fenti gombbal, csomópontokból szerkeszthető.");
            DrawCustomFields(world, rule.Scope);
            if (rule.Template.Length > 0) {
                if (ImGui.Button("Visszaállítás a mintára")) ResetVisualRule(world, rule.Id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("A szabály hatásai a minta szerinti változatra állnak vissza. Visszavonható.");
            }
        }
        var readable = ReadableFields(world, scope);
        var writable = WritableFields(world, scope);
        bool Pick(string label, string current, string[] choices, Action<string> set, bool empty = false)
            => ObjectFieldPicker(label, rule.Scope, current, choices, set, empty);
        void Number(string label, double value, Action<double> set)
        {
            bool finished = false;
            if (CurveNumber(label, ref value, 0, Math.Max(1, Math.Abs(value) * 2), ref finished)) set(value);
            changed |= finished;
        }

        // The rule read aloud: always derived from the saved data, so it follows every edit at once.
        var sentences = Sentences(world, rule.Scope);
        InfluenceSentences = sentences.Rule(rule);
        string current = null;
        if (rule.Effects.Count > 0 && rule.Effects[Math.Clamp(selectedInfluence, 0, rule.Effects.Count - 1)] is { } shown && shown.Influence.Terms.Count > 0)
            current = sentences.Term(shown, shown.Influence.Terms[Math.Clamp(selectedTerm, 0, shown.Influence.Terms.Count - 1)]);
        if (ImGui.CollapsingHeader("A szabály szövegesen")) {
            ImGui.BeginChild("rule-sentences", new Vec2(0, Math.Min(126, 10 + InfluenceSentences.Count * ImGui.GetTextLineHeightWithSpacing() * 1.6f)), ImGuiChildFlags.Borders);
            ImGui.PushTextWrapPos(0);
            foreach (string line in InfluenceSentences)
                EditorStyle.Text(line == current ? HudTheme.Parchment : HudTheme.Muted, (line == current ? "» " : "• ") + line);
            ImGui.PopTextWrapPos();
            ImGui.EndChild();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A szabály szövegesen, a gráfból számolva. A kijelölt kapcsolat világos.");
        }
        OtherWriters(world, rule);

        if (ruleBookError != null) EditorStyle.Text(HudTheme.Bad, ruleBookError);
        if (ImGui.BeginTable("graph-workspace", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) {
        ImGui.TableSetupColumn("Gráf", ImGuiTableColumnFlags.WidthStretch, 1.9f);
        ImGui.TableSetupColumn("Hatás", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableNextColumn();
        DrawInfluenceGraph(world, rule, scope, ref changed);
        InfluencePreviewArea(world, rule, scope);
        StumpComparisonArea(world, rule);
        ImGui.TableNextColumn();
        ImGui.BeginChild("graph-inspector", new Vec2(0, Math.Max(440, ImGui.GetContentRegionAvail().Y - 40)), ImGuiChildFlags.Borders);
        ImGui.PushTextWrapPos(0);
        if (rule.Effects.Count > 0) {
            selectedInfluence = Math.Clamp(selectedInfluence, 0, rule.Effects.Count - 1);
            var effect = rule.Effects[selectedInfluence]; var influence = effect.Influence;
            if (current != null) {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorStyle.Fade(HudTheme.AmberAccent, .08f));
                ImGui.BeginChild("term-sentence", new Vec2(0, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
                ImGui.TextWrapped(current);
                ImGui.EndChild(); ImGui.PopStyleColor();
            }
            if (effect.Mode != "flow")
                changed |= Pick(effect.Mode == "action" ? "Mely művelet fusson?" : "Mit változtasson?", effect.Target, effect.Mode == "action" ? scope.Actions.Select(a => a.Name).ToArray() : writable, v => { effect.Target = v; });
            int combination = effect.Influence.Combine == "maximum" ? 1 : effect.Influence.Combine == "minimum" ? 2 : 0;
            if (ImGui.Combo("Kapcsolatok összesítése", ref combination, new[] { "Összeg", "Legnagyobb hatás", "Legkisebb hatás" }, 3)) {
                effect.Influence.Combine = combination == 1 ? "maximum" : combination == 2 ? "minimum" : "sum"; changed = true;
            }
            if (effect.Mode == "action") ImGui.TextWrapped("Ha a kapcsolat teljesül, a művelet végrehajtódik. Eltávolítás után az objektum további hatásai leállnak.");
            else {
                ImGui.TextUnformatted("Hogyan hasson a célra?");
                int mode = effect.Mode switch { "set" => 1, "flow" => 2, "approach" => 3, _ => 0 };
                ImGui.RadioButton("Hozzáad vagy levon", ref mode, 0); ImGui.SameLine();
                ImGui.RadioButton("Felülírja", ref mode, 1);
                ImGui.RadioButton("Célértékhez közelít", ref mode, 3);
                ImGui.RadioButton("Áramoltat egyik készletből a másikba", ref mode, 2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Készletből készletbe visz át: amennyi az egyikből elfogy, annyi a másikba kerül.");
                if (mode != effect.Mode switch { "set" => 1, "flow" => 2, "approach" => 3, _ => 0 }) {
                    if (mode == 2) { effect.To = ""; effect.FlowName = FieldDisplay(rule.Scope, effect.Target); foreach (var t in influence.Terms) t.Decrease = false; }
                    effect.Mode = mode switch { 1 => "set", 2 => "flow", 3 => "approach", _ => "add" }; influence.PerYear = rule.Schedule is not ("passage" or "growth") && mode is 0 or 2; influence.PerHour = false; changed = true;
                }
                if (effect.Mode == "approach") {
                    Number("Legnagyobb közelítés / erdőév", effect.ApproachPerYear, v => effect.ApproachPerYear = v);
                    EditorStyle.Text(HudTheme.Muted, "A kapcsolatok a célértéket adják. A tényleges változás az eltelt idő és a közelítés sebessége szerint korlátozott.");
                }
                if (effect.Mode == "flow") FlowSettings(world, rule, scope, effect, writable, ref changed);
                else {
                    EditorStyle.Text(HudTheme.Muted, effect.Mode == "approach"
                        ? "A kapcsolatok összege a célérték. Az érték ehhez közelít a megadott sebességgel, túllépés nélkül."
                        : effect.Mode == "set"
                        ? $"A(z) {FieldDisplay(rule.Scope, effect.Target)} új értékét a kapcsolatok választott összesítése adja."
                        : $"A kapcsolatok eredménye a jelenlegi {FieldDisplay(rule.Scope, effect.Target).ToLowerInvariant()} értékhez adódik (csökkentésnél levonódik).");
                    if (FieldRange(rule.Scope, effect.Target) is { Length: > 0 } range) EditorStyle.Text(HudTheme.Muted, $"Az eredmény {range} között marad.");
                }
            }
            if (rule.Schedule == "passage") EditorStyle.Text(HudTheme.Muted, "A megadott erősség egy áthaladásra vonatkozik. Az időlépés hossza nem szorozza meg.");
            else if (rule.Schedule != "growth" && effect.Mode is ("add" or "flow")) {
                ImGui.TextUnformatted("Mennyi idő alatt?");
                int pace = influence.PerYear ? 0 : 1;
                ImGui.RadioButton("Egy erdőév alatt", ref pace, 0); ImGui.SameLine(); ImGui.RadioButton("Minden lépésben", ref pace, 1);
                if (pace != (influence.PerYear ? 0 : 1)) { influence.PerYear = pace == 0; changed = true; }
                EditorStyle.Text(HudTheme.Muted, influence.PerYear
                    ? "Az erősség egy teljes erdőévre vonatkozik; a játék minden lépésben az eltelt idővel arányos részét alkalmazza. Ajánlott: így a játék sebessége nem változtat a hatáson."
                    : "Az erősség minden szabálylépésben (0,5 játékmásodperc) teljes egészében hat. Csak számlálókhoz és kapcsolókhoz ajánlott.");
            }
            if (ImGui.SmallButton("Cél törlése")) { rule.Effects.RemoveAt(selectedInfluence); changed = true; }
            else {
                if (effect.Mode != "action") EditorStyle.Text(HudTheme.Muted, "Az összesítés módja választható. Egy kapcsolaton belül a források szorzói összeszorzódnak.");
                if (influence.Terms.Count < 16 && ImGui.Button(effect.Mode == "action" ? "+ Kiváltó kapcsolat" : "+ Állandó hatás")) {
                    influence.Terms.Add(new() { Decrease = effect.Mode is not ("action" or "approach"), Enabled = effect.Mode != "action" });
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
                        if (effect.Mode != "action") {
                            ImGui.RadioButton("Növelje", ref direction, 0); ImGui.SameLine(); ImGui.RadioButton("Csökkentse", ref direction, 1);
                            if (direction != (term.Decrease ? 1 : 0)) { term.Decrease = direction == 1; changed = true; }
                        }
                        ImGui.TextUnformatted("Erősség");
                        int strength = term.AmountField.Length > 0 ? 1 : 0;
                        ImGui.RadioButton("Saját szám", ref strength, 0); ImGui.SameLine(); ImGui.RadioButton("Tulajdonságból", ref strength, 1);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A játékparaméterek a Típusok › Játékparaméterek lapon hangolhatók. Több szabály is használhatja ugyanazt.");
                        if (strength != (term.AmountField.Length > 0 ? 1 : 0)) {
                            if (strength == 0 && term.AmountField.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(term.AmountField[7..]) is { } spec) term.Amount = world.Tuning[spec.Key];
                            term.AmountField = strength == 1 ? "tuning." + GameTuning.Specs[0].Id : ""; changed = true;
                        }
                        if (strength == 1) {
                            Number("Forrásérték szorzója", term.AmountScale, v => term.AmountScale = Math.Max(0, v));
                            changed |= Pick("Melyik tulajdonság?", term.AmountField, readable, v => term.AmountField = v);
                            if (term.AmountField.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(term.AmountField[7..]) is { } now)
                                EditorStyle.Text(HudTheme.Muted, $"Most: {RuleSentence.Number(world.Tuning[now.Key])} {now.Unit}. A Típusok › Játékparaméterek lapon módosítható.");
                        } else {
                            string unit = FieldUnit(rule.Scope, effect.Target);
                            string per = effect.Mode == "add" ? rule.Schedule == "passage" ? " / áthaladás" : influence.PerYear ? " / erdőév" : " / lépés" : "";
                            Number((effect.Mode == "action" ? "Súly" : "Mennyiség" + (unit.Length > 0 && unit != "0–1" ? $" ({unit}{per})" : per)) + "##amount", term.Amount, v => term.Amount = Math.Max(0, v));
                            if (effect.Mode == "add" && rule.Schedule != "passage" && FieldRange(rule.Scope, effect.Target) == "0–1") {
                                // Presets for the common 0–1 states: how many forest years from full to empty.
                                EditorStyle.Text(HudTheme.Muted, "Gyors választás (teljes skála ennyi idő alatt):");
                                foreach (var (label, amount) in new[] { ("20 év", .05), ("10 év", .1), ("5 év", .2), ("2 év", .5), ("1 év", 1.0) }) {
                                    ImGui.SameLine();
                                    if (ImGui.SmallButton(label + "##preset")) { term.Amount = amount; influence.PerYear = true; changed = true; }
                                }
                            }
                        }
                        }
                        if (ImGui.CollapsingHeader($"Feltételek: {(term.Conditions.Count == 0 ? "mindig hat" : $"mind a(z) {term.Conditions.Count} teljesüljön")}##predicate", term.Conditions.Count > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) {
                            EditorStyle.Text(HudTheme.Muted, "A kapcsolat csak akkor hat, ha minden feltétel teljesül. Több lehetséges okhoz külön kapcsolatot adj meg.");
                            for (int c = 0; c < term.Conditions.Count; c++) {
                                ImGui.PushID("condition-" + c);
                                var condition = term.Conditions[c];
                                if (ImGui.CollapsingHeader($"{c + 1}. ha {sentences.Condition(condition)}##condition-details")) {
                                changed |= Pick("Vizsgált tulajdonság", condition.Field, readable, v => { condition.Field = v; FitCondition(rule.Scope, condition); });
                                var options = SchemaField(rule.Scope, condition.Field)?.Options ?? Array.Empty<RuleFieldOption>();
                                if (options.Length > 0) {
                                    // A yes/no or named-option field: pick the option, no thresholds.
                                    FitCondition(rule.Scope, condition);
                                    int negate = condition.Comparison == "notEqual" ? 1 : 0;
                                    ImGui.SetNextItemWidth(120);
                                    if (ImGui.Combo("##equality", ref negate, new[] { "pontosan", "nem" }, 2)) { condition.Comparison = negate == 1 ? "notEqual" : "equal"; changed = true; }
                                    ImGui.SameLine(); ImGui.SetNextItemWidth(-1);
                                    int option = Math.Max(0, Array.FindIndex(options, o => o.Value == condition.Value));
                                    if (ImGui.Combo("##option", ref option, options.Select(o => o.Display).ToArray(), options.Length)) { condition.Value = options[option].Value; changed = true; }
                                    if (ImGui.SmallButton("Feltétel törlése")) { term.Conditions.RemoveAt(c--); changed = true; }
                                    ImGui.PopID(); ImGui.Separator();
                                    continue;
                                }
                                var comparisons = new[] { "less", "atMost", "greater", "atLeast" };
                                int comparison = Array.IndexOf(comparisons, condition.Comparison);
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.Combo("##comparison", ref comparison, new[] { "kisebb, mint", "legfeljebb", "nagyobb, mint", "legalább" }, 4)) { condition.Comparison = comparisons[comparison]; changed = true; }
                                int thresholdKind = condition.ValueCurve != null ? 2 : condition.ValueField.Length > 0 ? 1 : 0;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.Combo("Küszöb forrása", ref thresholdKind, new[] { "Rögzített szám", "Másik tulajdonság", "Tulajdonság görbéje" }, 3)) {
                                    condition.ValueField = thresholdKind == 1 ? readable.FirstOrDefault(f => f != condition.Field) ?? readable[0] : "";
                                    condition.ValueCurve = thresholdKind == 2 ? new() { Source = readable.Contains("shadeTolerance") ? "shadeTolerance" : readable[0] } : null;
                                    changed = true;
                                }
                                if (thresholdKind == 2) {
                                    var factor = condition.ValueCurve;
                                    changed |= Pick("Mi adja a küszöböt?", factor.Source, readable, v => factor.Source = v);
                                    var curve = factor.ToCurve(); bool finished = false;
                                    if (CurveEditor("threshold", curve, rule.Scope, readable, ref finished, bindings: false)) factor.SetCurve(curve);
                                    changed |= finished;
                                    EditorStyle.Text(HudTheme.Muted, "A görbe kimenete a feltétel határértéke, nem szorzó.");
                                } else if (thresholdKind == 1) changed |= Pick("Határérték forrása", condition.ValueField, readable, v => condition.ValueField = v);
                                else Number("Határérték" + (FieldUnit(rule.Scope, condition.Field) is { Length: > 0 } u && u != "0–1" ? $" ({u})" : ""), condition.Value, v => condition.Value = v);
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
                            EditorStyle.Text(HudTheme.Muted, sentences.Factor(factor));
                            var curve = factor.ToCurve();
                            bool finished = false;
                            if (CurveEditor("factor-" + f, curve, rule.Scope, readable, ref finished, bindings: false)) factor.SetCurve(curve);
                            changed |= finished;
                            EditorStyle.Text(HudTheme.Muted, "A görbe kimenete szorzó: 0 = nincs hatás; 1 = teljes erősség; 2 = kétszeres.");
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
        ImGui.PopTextWrapPos();
        ImGui.EndChild(); ImGui.EndTable();
        }
        if (ImGui.Button("Másolat")) {
            var copy = RuleEngine.Clone(new() { rule })[0]; copy.Id = "rule-" + Guid.NewGuid().ToString("N")[..8];
            copy.Name += " (másolat)"; copy.Enabled = false;
            ruleBook.Add(copy); selectedBookRule = ruleBook.Count - 1; changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Szabály törlése")) { DeleteRule(world, rule); ImGui.PopStyleVar(); ImGui.PopTextWrapPos(); ImGui.EndChild(); return; }
        if (changed) { ApplyRuleBook(world, "Hatások módosítva."); }
        if (ruleBookError != null) EditorStyle.Text(HudTheme.Bad, ruleBookError);
        if (world.RuleBook.LastError != null) EditorStyle.Text(HudTheme.Bad, world.RuleBook.LastError);
        ImGui.Spacing();
        ImGui.PopStyleVar(); ImGui.PopTextWrapPos(); ImGui.EndChild();
    }

    /// <summary>The tile an element stands on, through its own index (roads, trails) or its tile relation; −1 when it has none.</summary>
    private static int ElementTile(GameWorld world, RuleScope scope, int id)
    {
        var tile = scope.Fields.FirstOrDefault(f => f.Name == "tile.index");
        try { return tile != null ? (int)tile.Read(world, id) : -1; } catch (Exception) { return -1; }
    }

    /// <summary>“Út 3 · (12; 40) · Állapot 0,78 · Burkolat makadám”: an element as a designer recognises it.</summary>
    private static string DescribeElement(GameWorld world, RuleScope scope, int id, int order)
    {
        var (_, rows) = world.TileGrid; int tile = ElementTile(world, scope, id);
        string where = tile >= 0 && rows > 0 ? $" · ({tile / rows}; {tile % rows})" : "";
        var facts = scope.Fields.Where(f => !f.Name.Contains('.') && f.Name != "index").Take(2).Select(f => {
            try { return FieldDisplay(scope.Id, f.Name) + " " + FieldValue(scope.Id, f.Name, f.Read(world, id)); } catch (Exception) { return null; }
        }).Where(s => s != null);
        return $"{ObjectDisplay(scope.Id, scope.Id)} {order + 1}{where} · {string.Join(" · ", facts)}";
    }

    /// <summary>Keeps a condition meaningful for its field: an option of a bool or enum field, a threshold of a number.</summary>
    private static void FitCondition(string scope, InfluenceCondition condition)
    {
        var options = SchemaField(scope, condition.Field)?.Options ?? Array.Empty<RuleFieldOption>();
        bool equality = condition.Comparison is "equal" or "notEqual";
        if (options.Length > 0) {
            if (!equality) condition.Comparison = "equal";
            condition.ValueField = ""; condition.ValueCurve = null;
            if (!options.Any(o => o.Value == condition.Value)) condition.Value = options[^1].Value;
        } else if (equality) condition.Comparison = "atLeast";
    }

    private void InfluencePreviewArea(GameWorld world, WorldRule rule, RuleScope scope)
    {
        if (rule.Schedule == "passage") { PassagePreviewArea(world, rule); return; }
        // Folded when the rule has its own comparison trial below, so the two do not crowd each other.
        if (ImGui.CollapsingHeader("Próba a kiválasztott objektumon", HasComparisonTrial(rule) ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen)) {
            EditorStyle.Text(HudTheme.Muted, "A tesztvilág egy objektumán mutatja, mit tenne a szabály, változatlan környezettel. Nem ír a világba.");
            var elements = scope.Elements(world);
            if (!elements.Contains(influencePreviewId)) influencePreviewId = elements.FirstOrDefault(-1);
            if (influencePreviewId < 0) { ImGui.TextUnformatted("Nincs ilyen objektum a tesztvilágban."); return; }
            int order = Array.IndexOf(elements, influencePreviewId);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##preview-object", DescribeElement(world, scope, influencePreviewId, order), ImGuiComboFlags.HeightLarge)) {
                for (int i = 0; i < Math.Min(elements.Length, 400); i++)
                    if (ImGui.Selectable(DescribeElement(world, scope, elements[i], i) + "##e" + elements[i], elements[i] == influencePreviewId)) influencePreviewId = elements[i];
                if (elements.Length > 400) EditorStyle.Text(HudTheme.Muted, $"…és még {elements.Length - 400}; a többit a térképen választhatod.");
                ImGui.EndCombo();
            }
            PreviewMap(world, scope, elements);
            ImGui.SetNextItemWidth(200); ImGui.SliderFloat("Eltelt idő (erdőév)", ref influencePreviewYears, .01f, 1, "%.3f");
            if (rule.Schedule == "month" && ImGui.SmallButton("Egy erdőhónap időtartama")) influencePreviewYears = 1f / 12;
            var active = world.RuleBook.Rules.FirstOrDefault(r => r.Id == rule.Id && r.Scope == rule.Scope);
            if (active != null && active.Effects.All(e => e.Influence != null)) {
                try { foreach (var row in world.RuleBook.PreviewInfluences(world, rule.Id, influencePreviewId, influencePreviewYears)) {
                    if (active.Effects.Any(e => e.Target == row.Target && e.Mode == "action")) {
                        ImGui.TextUnformatted(FieldDisplay(rule.Scope, row.Target) + (row.After > 0 ? ": végrehajtódna" : ": nem hajtódna végre")); continue;
                    }
                    double change = row.After - row.Before;
                    ImGui.TextUnformatted($"{FieldDisplay(rule.Scope, row.Target)}: {FieldValue(rule.Scope, row.Target, row.Before)} › {FieldValue(rule.Scope, row.Target, row.After)}"
                        + $"  ({(change >= 0 ? "+" : "–")}{FieldValue(rule.Scope, row.Target, Math.Abs(change))})");
                    if (active.Effects.Any(e => e.Target == row.Target && e.Mode == "approach"))
                        ImGui.TextWrapped("Az alábbi részek a célértéket adják; a tényleges változást a közelítés sebessége korlátozza.");
                    double total = row.Contributions.Sum(p => Math.Abs(p.Change));
                    foreach (var part in row.Contributions)
                        EditorStyle.Text(part.Change == 0 ? HudTheme.Muted : part.Change < 0 ? HudTheme.AmberAccent : HudTheme.Good,
                            $"  {part.Name}: {(part.Change >= 0 ? "+" : "–")}{RuleSentence.Number(Math.Abs(part.Change))}" + (total > 0 && part.Change != 0 ? $"  ({Math.Abs(part.Change) / total:P0})" : "  (most nem hat)"));
                } }
                catch (System.IO.InvalidDataException e) { EditorStyle.Text(HudTheme.Bad, e.Message); }
            } else ImGui.TextUnformatted("A szerkesztett szabály még nincs alkalmazva.");
        }
    }

    /// <summary>A small map of the scope's elements; clicking one picks it for the trial.</summary>
    private void PreviewMap(GameWorld world, RuleScope scope, int[] elements)
    {
        var (columns, rows) = world.TileGrid;
        if (columns == 0 || rows == 0 || !scope.Fields.Any(f => f.Name == "tile.index")) return;
        float cell = Math.Max(1, MathF.Floor(150f / Math.Max(columns, rows)));
        var size = new Vec2(columns * cell, rows * cell);
        var at = ImGui.GetCursorScreenPos(); var draw = ImGui.GetWindowDrawList();
        ImGui.InvisibleButton("preview-map", size);
        bool hover = ImGui.IsItemHovered(); var mouse = ImGui.GetIO().MousePos;
        draw.AddRectFilled(at, at + size, EditorStyle.Canvas);
        draw.AddRect(at, at + size, EditorStyle.GridMajor);
        int nearest = -1; float best = 8 * 8;
        foreach (int id in elements.Take(5000)) {
            int tile = ElementTile(world, scope, id); if (tile < 0) continue;
            var p = at + new Vec2(tile / rows + .5f, tile % rows + .5f) * cell;
            draw.AddCircleFilled(p, Math.Max(1.5f, cell * .45f), EditorStyle.U(EditorStyle.InputKind));
            float d = Vec2.DistanceSquared(p, mouse);
            if (hover && d < best) { best = d; nearest = id; }
        }
        int selected = ElementTile(world, scope, influencePreviewId);
        if (selected >= 0) {
            var p = at + new Vec2(selected / rows + .5f, selected % rows + .5f) * cell;
            draw.AddCircle(p, Math.Max(5, cell * 1.2f), EditorStyle.Selection, 0, 2);
        }
        if (nearest >= 0) {
            ImGui.SetTooltip(DescribeElement(world, scope, nearest, Array.IndexOf(elements, nearest)) + "\nKattintás: ezt próbáld ki.");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) influencePreviewId = nearest;
        }
        ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, "Kattints egy pontra a térképen,\nvagy válassz a fenti listából.");
    }
}
