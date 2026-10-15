using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The rule book: a rule reads as a sentence – WHEN · WHERE · IF · THEN – and is run by the game. Built-in rules moved out
/// of C# appear here too; they can be edited, switched off and reset, not deleted.
/// </summary>
internal sealed partial class RuleEditorView
{
    private List<WorldRule> ruleBook;
    private List<RuleFieldDefinition> ruleFields;
    private int selectedBookRule;
    private string ruleBookError, nameFilter = "";
    private int lastFormula = -1; // -1 condition, 0.. effect index: where a clicked name is inserted
    private RuleFieldDefinition newField = new() { Name = "saját.", Scope = "tile" };

    private static readonly (string Mode, string Label, string Help)[] EffectModes = {
        ("set", "legyen", "A mező új értéke a képlet eredménye."),
        ("add", "növekedjen", "A képlet eredménye hozzáadódik a mezőhöz (negatív: csökken)."),
        ("action", "művelet", "A hatókör művelete lefut, ha a képlet nem 0 (üres képlet: mindig). Utána az elem további hatásai nem futnak.") };

    private void LoadRuleBook(RuleProject project)
    {
        ruleBook = RuleEngine.Clone(project.Rules ?? new()); RuleEngine.AddMissingBuiltIns(ruleBook);
        ruleFields = RuleEngine.CloneFields(project.Fields ?? new());
        selectedBookRule = 0; ruleBookError = null;
    }
    private List<WorldRule> RuleBookRules(GameWorld world) => ruleBook ??= RuleEngine.Clone(world.RuleProjectDocument.Rules);
    private List<RuleFieldDefinition> RuleBookFields(GameWorld world) => ruleFields ??= RuleEngine.CloneFields(world.RuleProjectDocument.Fields);
    internal void ShowRuleBook(int index = 0) { worldTab = WorldTab.Rules; selectedBookRule = index; }
    private void RulesArea(GameWorld world) => RuleBookArea(world);

    /// <summary>Checks the whole book as the game will; on success the test world runs it at once.</summary>
    private void ApplyRuleBook(GameWorld world, string what)
    {
        try {
            _ = new RuleEngine(RuleEngine.Clone(ruleBook), RuleEngine.CloneFields(ruleFields), dynamicsEditor.Draft.Stocks.Select(s => s.Id), _ => 0);
            ruleBookError = null;
            Try(() => ApplyProject(world, false));
            message = what + " A tesztvilágban már ez fut.";
        } catch (InvalidDataException e) { ruleBookError = e.Message; }
    }

    private void RuleBookArea(GameWorld world)
    {
        var rules = RuleBookRules(world); var fields = RuleBookFields(world);
        selectedBookRule = Math.Clamp(selectedBookRule, 0, Math.Max(0, rules.Count - 1));

        // ── Left: the book ───────────────────────────────────────────────
        ImGui.BeginChild("book-list", new Vec2(290, 0), ImGuiChildFlags.Borders);
        foreach (var (title, builtIn) in new[] { ("Beépített szabályok", true), ("Saját szabályok", false) }) {
            EditorStyle.Section(title);
            bool any = false;
            for (int i = 0; i < rules.Count; i++) {
                if (rules[i].BuiltIn != builtIn) continue;
                any = true;
                bool enabled = rules[i].Enabled;
                if (ImGui.Checkbox("##on" + i, ref enabled)) { rules[i].Enabled = enabled; ApplyRuleBook(world, rules[i].Name + (enabled ? ": bekapcsolva." : ": kikapcsolva.")); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(enabled ? "Fut a játékban. Kattintás: kikapcsolás." : "Nem fut. Kattintás: bekapcsolás.");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, enabled ? HudTheme.Parchment : HudTheme.Muted);
                if (ImGui.Selectable(rules[i].Name + "##rule" + i, selectedBookRule == i)) { selectedBookRule = i; lastFormula = -1; }
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{ScopeName(rules[i].Scope)} · {(rules[i].RoadWeather != null ? 1 : rules[i].Effects.Count)} hatás");
            }
            if (!any) EditorStyle.Text(HudTheme.Muted, builtIn ? "Nincs." : "Még nincs saját szabály.");
        }
        ImGui.Spacing();
        if (ImGui.Button("Új szabály", new Vec2(-1, 0))) {
            int n = rules.Count(r => !r.BuiltIn) + 1;
            while (rules.Exists(r => r.Id == $"sajat-{n}")) n++;
            rules.Add(new WorldRule { Id = $"rule-{Guid.NewGuid():N}", Name = $"Saját szabály {n}", Scope = "road", Enabled = false,
                Effects = { new() { Target = "condition", Mode = "add", Influence = new() } } });
            selectedBookRule = rules.Count - 1; lastFormula = -1;
            message = "Új szabály: válaszd ki, hol fusson, és adj hozzá hatást.";
        }
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A szabályok fentről lefelé, minden lépésben (0,5 játékmásodperc) futnak, a hatókör minden elemén.");
        ImGui.PopTextWrapPos();
        ImGui.EndChild(); ImGui.SameLine();

        if (rules.Count > 0 && rules[selectedBookRule].Condition.Length == 0 && rules[selectedBookRule].Effects.All(e => e.Influence != null)) {
            InfluenceArea(world, rules[selectedBookRule]);
            return;
        }

        // ── Middle: the rule as a sentence ──────────────────────────────
        ImGui.BeginChild("book-rule", new Vec2(-360, 0), ImGuiChildFlags.Borders);
        if (rules.Count == 0) { ImGui.EndChild(); ImGui.SameLine(); ImGui.BeginChild("book-names", Vec2.Zero, ImGuiChildFlags.Borders); ImGui.EndChild(); return; }
        var rule = rules[selectedBookRule];
        var scope = RuleEngine.Scope(rule.Scope) ?? RuleEngine.Scopes[1];
        bool finished = false;

        string name = rule.Name;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##rule-name", ref name, 120) && name.Trim().Length > 0) rule.Name = name;
        finished |= ImGui.IsItemDeactivatedAfterEdit();
        string description = rule.Description ?? "";
        if (ImGui.InputTextMultiline("##rule-desc", ref description, 2000, new Vec2(-1, 48))) rule.Description = description;
        finished |= ImGui.IsItemDeactivatedAfterEdit();
        if (rule.BuiltIn) {
            ImGui.PushTextWrapPos(0);
            EditorStyle.Text(HudTheme.Muted, "Beépített szabály: korábban C# kód volt, most adatként fut. Szerkeszthető és kikapcsolható.");
            ImGui.PopTextWrapPos();
            if (EditorStyle.QuietButton("Eredeti visszaállítása", "A szabály a játék eredeti változatára áll vissza.")) {
                var original = RuleEngine.BuiltIn().Find(r => r.Id == rule.Id);
                if (original != null) { rules[selectedBookRule] = original; rule = original; ApplyRuleBook(world, rule.Name + ": eredeti visszaállítva."); }
            }
        }

        Clause("MIKOR", "Minden lépésben, a lépés hossza: lépés (erdőév).");
        Clause("HOL", null);
        ImGui.SetNextItemWidth(260);
        if (ImGui.BeginCombo("##scope", ScopeName(rule.Scope), ImGuiComboFlags.HeightLarge)) {
            EditorStyle.Text(HudTheme.Muted, "Térkép");
            foreach (var s in RuleEngine.Scopes.Where(s => s.Entity == null))
                if (ImGui.Selectable("  " + s.Name, s.Id == rule.Scope) && s.Id != rule.Scope) { rule.Scope = s.Id; finished = true; }
            ImGui.Separator();
            EditorStyle.Text(HudTheme.Muted, "Objektumok");
            string picked = null;
            foreach (var root in Objects.Roots) ObjectNode(root, rule.Scope, id => picked = id, abstractSelectable: false);
            if (picked != null && picked != rule.Scope) { rule.Scope = picked; finished = true; ImGui.CloseCurrentPopup(); }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, $"most {ElementCount(world, scope)} {scope.Element}");
        if (scope.Entity != null) {
            ImGui.SetCursorPosX(80);
            EditorStyle.Text(HudTheme.Muted, string.Join(" › ", Objects.Lineage(scope.Entity).Select(x => x.Name)));
        }

        Clause("HA", null);
        string condition = rule.Condition ?? "";
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##condition", "üres: mindig · például: gyökérzónavíz > talaj.vízkapacitás", ref condition, 1000)) rule.Condition = condition;
        if (ImGui.IsItemActive()) lastFormula = -1;
        finished |= ImGui.IsItemDeactivatedAfterEdit();

        Clause("AKKOR", "");
        var writable = scope.Fields.Where(f => f.Write != null).Select(f => f.Name)
            .Concat(fields.Where(f => f.Scope == scope.Id).Select(f => f.Name)).ToArray();
        int remove = -1, up = -1;
        for (int i = 0; i < rule.Effects.Count; i++) {
            var e = rule.Effects[i];
            ImGui.PushID("effect" + i);
            ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{i + 1}.");
            ImGui.SameLine();
            var targets = e.Mode == "action" ? scope.Actions.Select(a => a.Name).ToArray() : writable;
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##target", targets.Contains(e.Target) ? e.Target : "válassz…")) {
                foreach (string t in targets) if (ImGui.Selectable(t, t == e.Target)) { e.Target = t; finished = true; }
                if (targets.Length == 0) ImGui.TextDisabled(e.Mode == "action" ? "Ennek a hatókörnek nincs művelete." : "Nincs írható mező: vegyél fel saját mezőt.");
                ImGui.EndCombo();
            }
            ImGui.SameLine(); ImGui.SetNextItemWidth(110);
            int mode = Math.Max(0, Array.FindIndex(EffectModes, m => m.Mode == e.Mode));
            if (ImGui.Combo("##mode", ref mode, EffectModes.Select(m => m.Label).ToArray(), EffectModes.Length)) {
                e.Mode = EffectModes[mode].Mode; if (e.Mode == "action" != scope.Actions.Any(a => a.Name == e.Target)) e.Target = ""; finished = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(EffectModes[mode].Help);
            ImGui.SameLine(0, 16);
            ImGui.BeginDisabled(i == 0); if (ImGui.SmallButton("fel")) up = i; ImGui.EndDisabled();
            ImGui.SameLine(); if (ImGui.SmallButton("töröl")) remove = i;
            // A curve is edited by its shape; anything else (or on request) as formula text on its own full-width line.
            ImGui.Indent(24);
            string viewKey = rule.Id + "#" + i;
            bool isCurve = RuleCurve.TryParse(e.Formula, out var curve);
            if (e.Mode != "action") {
                if (isCurve) {
                    bool asText = formulaView.Contains(viewKey);
                    if (EditorStyle.QuietButton(asText ? "görbeként##v" : "képletként##v", asText ? "Szerkesztés a görbe alakjával." : "A görbe képletszövege; így is szerkeszthető."))
                        if (!formulaView.Remove(viewKey)) formulaView.Add(viewKey);
                } else if (EditorStyle.QuietButton("görbe##v", "A képlet helyére görbe kerül: bemenet → hatás, húzható pontokkal. (A mostani képlet lecserélődik.)")) {
                    string input = RuleEngine.Names(scope.Id, fields, dynamicsEditor.Draft.Stocks.Select(s => s.Id)).FirstOrDefault(n => n.Operation == BehaviorOperation.Input && n.Name != e.Target)?.Name ?? "lépés";
                    e.Formula = new RuleCurve { Input = input, Points = new() { (0, 0), (1, 1) }, Tail = " * lépés" }.ToFormula();
                    isCurve = RuleCurve.TryParse(e.Formula, out curve); formulaView.Remove(viewKey); finished = true;
                }
            }
            if (isCurve && !formulaView.Contains(viewKey)) {
                var inputs = RuleEngine.Names(scope.Id, fields, dynamicsEditor.Draft.Stocks.Select(s => s.Id)).Where(n => n.Operation == BehaviorOperation.Input).Select(n => n.Name).ToArray();
                EditorStyle.Text(HudTheme.Muted, e.Mode == "add" ? $"{e.Target} változása lépésenként:" : $"{e.Target} értéke:");
                if (CurveEditor(viewKey, curve, scope.Entity ?? scope.Id, inputs, ref finished)) e.Formula = curve.ToFormula();
            } else {
                ImGui.AlignTextToFramePadding();
                EditorStyle.Text(HudTheme.Muted, e.Mode == "add" ? "+=" : e.Mode == "action" ? "ha" : "=");
                ImGui.SameLine();
                string formula = e.Formula ?? "";
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##formula", e.Mode == "action" ? "üres: mindig" : "képlet", ref formula, 1000)) e.Formula = formula;
                if (ImGui.IsItemActive()) lastFormula = i;
                finished |= ImGui.IsItemDeactivatedAfterEdit();
            }
            ImGui.Unindent(24); ImGui.Spacing();
            ImGui.PopID();
        }
        if (up > 0) { (rule.Effects[up - 1], rule.Effects[up]) = (rule.Effects[up], rule.Effects[up - 1]); finished = true; }
        if (remove >= 0) { rule.Effects.RemoveAt(remove); finished = true; }
        if (ImGui.Button("+ Hatás")) rule.Effects.Add(new RuleEffect { Target = writable.FirstOrDefault() ?? "", Mode = "set", Formula = "" });
        ImGui.SameLine();
        EditorStyle.Text(HudTheme.Muted, "A hatások sorban futnak; a későbbi már a korábbiak eredményét látja.");

        ImGui.Spacing();
        if (ruleBookError != null) { ImGui.PushTextWrapPos(0); EditorStyle.Text(HudTheme.Bad, ruleBookError); ImGui.PopTextWrapPos(); }
        else EditorStyle.Text(HudTheme.Good, "A szabálykönyv érvényes; a tesztvilágban ez fut.");
        if (world.RuleBook.LastError != null) { ImGui.PushTextWrapPos(0); EditorStyle.Text(HudTheme.AmberAccent, "Futás közben: " + world.RuleBook.LastError); ImGui.PopTextWrapPos(); }

        ImGui.Spacing();
        if (!rule.BuiltIn) {
            if (ImGui.Button("Szabály törlése")) { rules.RemoveAt(selectedBookRule); selectedBookRule = Math.Max(0, selectedBookRule - 1); ApplyRuleBook(world, "Szabály törölve."); ImGui.EndChild(); return; }
            ImGui.SameLine();
        }
        if (ImGui.Button("Másolat")) {
            var copy = RuleEngine.Clone(new List<WorldRule> { rule })[0];
            copy.BuiltIn = false; copy.Id = rule.Id + "-masolat-" + Guid.NewGuid().ToString("N")[..4]; copy.Name = rule.Name + " (másolat)"; copy.Enabled = false;
            rules.Add(copy); selectedBookRule = rules.Count - 1;
            message = "Másolat kikapcsolva készült: módosítsd, majd kapcsold be.";
        }
        if (finished) ApplyRuleBook(world, rule.Name + " módosítva.");
        ImGui.EndChild(); ImGui.SameLine();

        // ── Right: names the formulas may use, and custom fields ─────────
        ImGui.BeginChild("book-names", Vec2.Zero, ImGuiChildFlags.Borders);
        EditorStyle.Section("Saját mezők · " + scope.Name.ToLowerInvariant());
        foreach (var f in fields.Where(f => f.Scope == scope.Id).ToArray()) {
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(f.Name);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{f.Description}\nKezdő: {f.Initial:G6} · {f.Min:G6}–{f.Max:G6} {f.Unit}");
            ImGui.SameLine();
            if (EditorStyle.QuietButton("töröl##f" + f.Name, "A mező és értékei törlődnek.")) { fields.Remove(f); ApplyRuleBook(world, f.Name + " törölve."); }
        }
        string fieldName = newField.Name; ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##field-name", "saját.név", ref fieldName, 64); newField.Name = fieldName;
        double initial = newField.Initial; ImGui.SetNextItemWidth(110); ImGui.InputDouble("kezdőérték##fi", ref initial, 0, 0, "%.3g"); newField.Initial = initial;
        string unit = newField.Unit; ImGui.SameLine(); ImGui.SetNextItemWidth(70); ImGui.InputTextWithHint("##fu", "egység", ref unit, 24); newField.Unit = unit;
        if (ImGui.Button("Saját mező felvétele")) {
            var field = new RuleFieldDefinition { Name = newField.Name.Trim(), Scope = scope.Id, Initial = newField.Initial, Unit = newField.Unit, Min = -1e9, Max = 1e9 };
            try { field.Validate(); if (fields.Exists(f => f.Name == field.Name)) throw new InvalidDataException("Ez a név már foglalt.");
                fields.Add(field); newField = new() { Name = "saját.", Scope = scope.Id }; ApplyRuleBook(world, field.Name + " felvéve."); }
            catch (InvalidDataException e) { ruleBookError = e.Message; }
        }
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "Saját állapot elemenként; a játék menti. Így új jelenség is leírható C# nélkül.");
        ImGui.PopTextWrapPos();

        EditorStyle.Section("Használható nevek");
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##name-filter", "szűrés…", ref nameFilter, 60);
        IReadOnlyList<FormulaName> names;
        try { names = RuleEngine.Names(scope.Id, fields, dynamicsEditor.Draft.Stocks.Select(s => s.Id)); } catch (InvalidDataException) { names = Array.Empty<FormulaName>(); }
        ImGui.BeginChild("names-scroll", Vec2.Zero);
        foreach (var n in names.Where(n => nameFilter.Length == 0 || n.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) || n.Help.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))) {
            bool isWritable = writable.Contains(n.Name);
            ImGui.PushStyleColor(ImGuiCol.Text, isWritable ? HudTheme.AmberAccent : n.Operation == BehaviorOperation.Input ? HudTheme.Parchment : HudTheme.Muted);
            if (ImGui.Selectable(n.Name + "##n" + n.Name)) InsertName(rule, n.Name);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(n.Help + (isWritable ? "\nÍrható: hatás célja lehet." : "") + "\nKattintás: a név a legutóbb szerkesztett képlet végére kerül.");
        }
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void InsertName(WorldRule rule, string name)
    {
        static string Append(string text, string name) => ((text ?? "").TrimEnd() + " " + name).TrimStart();
        if (lastFormula >= 0 && lastFormula < rule.Effects.Count) rule.Effects[lastFormula].Formula = Append(rule.Effects[lastFormula].Formula, name);
        else rule.Condition = Append(rule.Condition, name);
    }

    private static void Clause(string word, string text)
    {
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        EditorStyle.Text(HudTheme.AmberAccent, word);
        if (text != null) { ImGui.SameLine(80); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Parchment, text); }
        else ImGui.SameLine(80);
    }

    private static string ScopeName(string id) => RuleEngine.Scope(id)?.Name ?? id;
    private static int ElementCount(GameWorld world, RuleScope scope) { try { return scope.Elements(world).Length; } catch (Exception) { return 0; } }
}
