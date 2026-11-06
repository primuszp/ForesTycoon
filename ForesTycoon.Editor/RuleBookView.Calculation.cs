using System;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
internal sealed partial class RuleBookView
{
    private ref string expressionKey => ref state.ExpressionKey;
    private ref string expressionSource => ref state.ExpressionSource;
    private ref bool editRuleCalculation => ref state.EditRuleCalculation;
    private ref int selectedCalculation => ref state.SelectedCalculation;
    internal void ShowRuleCalculation(int index, int effect = -1) { ShowRuleBook(index); editRuleCalculation = true; selectedCalculation = effect; expressionKey = null; }
    private void DrawCustomFields(GameWorld world, string scope)
    {
        if (!ImGui.CollapsingHeader("Saját állapottulajdonságok")) return;
        var fields = RuleBookFields(world);
        foreach (var f in fields.Where(f => f.Scope == scope).ToArray()) {
            ImGui.TextUnformatted(f.Name); ImGui.SameLine();
            if (ImGui.SmallButton("Törlés##custom-" + f.Name)) { ruleBook.DeleteField(f); ApplyRuleBook(world, "Tulajdonság törölve."); }
        }
        string name = newField.Name; ImGui.InputText("Kódnév", ref name, 64); newField.Name = name;
        double initial = newField.Initial; ImGui.InputDouble("Kezdőérték", ref initial); newField.Initial = initial;
        string unit = newField.Unit; ImGui.InputText("Mértékegység", ref unit, 32); newField.Unit = unit;
        if (ImGui.Button("Tulajdonság felvétele")) {
            try {
                ruleBook.AddField(scope, newField.Name, newField.Initial, newField.Unit); ApplyRuleBook(world, "Tulajdonság felvéve.");
            } catch (Exception e) when (Expected(e)) { ruleBookError = e.Message; }
        }
    }

    private void DrawRuleCalculation(GameWorld world, RuleView rule)
    {
        ImGui.BeginChild("rule-calculation", Vec2.Zero, ImGuiChildFlags.Borders);
        ImGui.PushItemWidth(-EditorStyle.LabelWidth());
        try { RuleCalculationBody(world, rule); } finally { ImGui.PopItemWidth(); }
        ImGui.EndChild();
    }

    private void RuleCalculationBody(GameWorld world, RuleView rule)
    {
        bool changed = false, delete = false; string name = rule.Name;
        RuleHeader(world, rule, ref changed, ref delete);
        if (delete) { DeleteRule(world, rule); return; }
        if (ImGui.InputText("Szabály neve", ref name, 120)) ruleBook.Edit(rule, r => r.Name = name); if (ImGui.IsItemDeactivatedAfterEdit()) { changed = true; }
        DrawSchedule(rule, ref changed); DrawCustomFields(world, rule.Scope);
        if (ImGui.BeginCombo("Mely objektumokon fusson?", ObjectDisplay(rule.Scope, rule.Scope))) {
            foreach (var candidate in RuleEngine.Scopes.Where(s => s.Fields.Any(f => f.Write != null) || s.Actions.Length > 0))
                if (ImGui.Selectable(candidate.Name, candidate.Id == rule.Scope)) { SetRuleScope(rule, candidate.Id); expressionKey = null; changed = true; }
            ImGui.EndCombo();
        }
        var scope = RuleEngine.Scope(rule.Scope);
        if (scope == null) { ImGui.TextWrapped("A szabály hatóköre nem érhető el: " + rule.Scope); return; }
        if (rule.Effects.All(e => e.Influence != null) && ImGui.Button("Hatáskapcsolatok gráfja")) editRuleCalculation = false;
        selectedCalculation = Math.Clamp(selectedCalculation, -1, rule.Effects.Count - 1);
        if (ImGui.BeginCombo("Számítás kiválasztása", selectedCalculation < 0 ? "A szabály teljesülési feltétele" : "Hatás: " + FieldDisplay(rule.Scope, rule.Effects[Math.Min(selectedCalculation, rule.Effects.Count - 1)].Target))) {
            if (ImGui.Selectable("A szabály teljesülési feltétele")) { selectedCalculation = -1; expressionKey = null; }
            for (int i = 0; i < rule.Effects.Count; i++)
                if (ImGui.Selectable($"{i + 1}. {FieldDisplay(rule.Scope, rule.Effects[i].Target)}")) { selectedCalculation = i; expressionKey = null; }
            ImGui.EndCombo();
        }
        selectedCalculation = Math.Clamp(selectedCalculation, -1, rule.Effects.Count - 1);
        var effect = selectedCalculation < 0 ? null : rule.Effects[selectedCalculation];
        if (effect != null) {
            changed |= ObjectFieldPicker("Mit változtasson?", rule.Scope, effect.Target,
                effect.Mode == "action" ? scope.Actions.Select(a => a.Name).ToArray() : WritableFields(world, scope), v => ruleBook.EditEffect(rule, effect, value => value.Target = v));
            string[] modes = { "set", "add", "approach", "action", "flow" };
            int mode = Array.IndexOf(modes, effect.Mode);
            if (ImGui.Combo("Hatás módja", ref mode, new[] { "Felülírja", "Hozzáad / levon", "Célértékhez közelít", "Művelet", "Készletátadás" }, 5)) { ruleBook.EditEffect(rule, effect, value => value.Mode = modes[mode]); changed = true; }
            if (effect.Mode == "approach") { double speed = effect.ApproachPerYear; if (CurveNumber("Közelítés / év", ref speed, 0, Math.Max(1, speed * 2), ref changed)) ruleBook.EditEffect(rule, effect, value => value.ApproachPerYear = speed); }
            if (effect.Mode == "flow") FlowSettings(world, rule, scope, effect, WritableFields(world, scope), ref changed);
        }
        if (ImGui.Button("Új hatás")) { ruleBook.AddCalculation(rule); selectedCalculation = rule.Effects.Count - 1; expressionKey = null; changed = true; }
        if (effect != null) { ImGui.SameLine(); if (ImGui.Button("Hatás törlése")) { ruleBook.DeleteEffect(rule, effect); selectedCalculation = -1; expressionKey = null; changed = true; } }
        if (selectedCalculation < 0 && (rule.ConditionGraph != null || rule.Condition.Length > 0)) {
            ImGui.SameLine(); if (ImGui.Button("Feltétel törlése · mindig teljesüljön")) { ruleBook.ClearCondition(rule); expressionKey = null; changed = true; }
        }
        if (changed) ApplyRuleBook(world, "Szabály módosítva.");
        try {
            effect = selectedCalculation < 0 ? null : rule.Effects[selectedCalculation];
            string source = effect == null ? JsonSerializer.Serialize(new { rule.Condition, rule.ConditionGraph }) : JsonSerializer.Serialize(effect);
            string key = rule.Id + ":" + selectedCalculation;
            var inputs = ruleBook.ExpressionNames(rule.Scope, context.StockIds());
            string[] fields = scope.Fields.Select(f => f.Name).Concat(RuleBookFields(world).Where(f => f.Scope == scope.Id).Select(f => f.Name)).ToArray();
            var hook = new BehaviorHook("rule.expression", rule.Name, "rule." + rule.Scope, "", -1e12, 1e12, "", "", "A kiválasztott hatás / feltétel", false, fields);
            if (expressionKey != key || expressionSource != source) {
                var existing = effect == null ? rule.ConditionGraph : effect.Computation;
                var graph = existing == null ? RuleFormula.Compile(effect == null ? (rule.Condition.Length == 0 ? "1" : rule.Condition) : effect.Executable().Formula.Length == 0 ? "1" : effect.Executable().Formula,
                    new BehaviorGraph { Id = "rule.expression", Hook = hook.Id, Kind = hook.Kind, Name = rule.Name }, inputs)
                    : new BehaviorModel { Graphs = { existing } }.Clone().Graphs[0];
                graph.Id = graph.Hook = hook.Id; graph.Kind = hook.Kind;
                expressionEditor.Configure(new[] { hook }, context.NamedValues(), world.ReadRuleValue);
                expressionEditor.LoadDraft(new BehaviorModel { Graphs = { graph } }); pendingExpressionChange = false; expressionKey = key; expressionSource = source;
            }
            expressionEditor.SourceDisplay = field => FieldTitle(rule.Scope, field);
            expressionEditor.PickField = (label, current, choices, set) => ObjectFieldPicker(label, rule.Scope, current, choices, set);
            string before = expressionEditor.Document.ToJson();
            expressionEditor.DrawEquation(world);
            string after = expressionEditor.Document.ToJson();
            if (before != after || pendingExpressionChange) {
                var graph = expressionEditor.Document.Graphs.Single();
                ruleBook.SetCalculation(rule, effect, graph);
                expressionSource = effect == null ? JsonSerializer.Serialize(new { rule.Condition, rule.ConditionGraph }) : JsonSerializer.Serialize(effect);
                pendingExpressionChange = ImGui.IsAnyItemActive(); context.RequestApply();
            }
        } catch (Exception e) when (Expected(e) || e is FormatException) { ImGui.TextWrapped(e.Message); }
        if (ruleBookError != null) ImGui.TextWrapped(ruleBookError);
    }
    private bool pendingExpressionChange;
}
