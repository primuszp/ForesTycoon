using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    internal System.Collections.Generic.Dictionary<string, Vec2> RuleCardAnchors { get; } = new();
    private string cardUsesSignature;
    private System.Collections.Generic.IReadOnlyList<FieldUse> cardUsesCache;
    private bool scrollRuleCard;
    private string requestedResponseOff;
    private double requestedResponseValue;
    // The same sidebar card is used for effects and calculated responses.
    private bool RuleCardEntry(string id, string name, string kind, bool selected, bool enabled, Action select, Action<bool> enable, string tooltip)
    {
        ImGui.PushID(id);
        bool on = enabled;
        if (ImGui.Checkbox("##enabled", ref on)) enable(on);
        RuleCardAnchors["enable:" + id] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.SameLine();
        if (ImGui.Selectable(name, selected)) select();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.PopID();
        if (selected && scrollRuleCard) { ImGui.SetScrollHereY(.4f); scrollRuleCard = false; }
        return on != enabled;
    }
    private void Facts(string name, string kind, string when, string[] reads, string targets, string consumers, bool enabled, string off)
    {
        EditorStyle.Text(HudTheme.Parchment, name);
        EditorStyle.Text(HudTheme.Muted, kind + " · " + (enabled ? "Aktív" : "Kikapcsolva"));
        bool details = ImGui.CollapsingHeader("Mikor fut, mire hat, ki használja?##rule-details");
        if (details && EditorStyle.BeginFacts("rule-card-facts")) {
            EditorStyle.Fact("Mikor fut?", when);
            string inputs = kind == "Értéket számol" && !enabled ? "A megadott fix értékből; a gráf forrásait most nem olvassa." : reads.Length == 0 ? "Állandókból; nincs bekötött forrás." : string.Join("; ", reads.Take(4)) + (reads.Length > 4 ? $"; +{reads.Length - 4} további forrás a gráfon" : "");
            EditorStyle.Fact("Miből számol?", inputs);
            if (ImGui.IsItemHovered() && reads.Length > 0) ImGui.SetTooltip(string.Join("\n", reads));
            EditorStyle.Fact("Mire hat?", targets);
            EditorStyle.Fact("Ki használja?", consumers);
            ImGui.EndTable();
        }
        if (details || !enabled) ImGui.TextWrapped(off);
        ImGui.Separator();
    }
    private void WorldRuleCard(GameWorld world, WorldRule rule)
    {
        var all = RuleBookRules(world); int self = all.FindIndex(r => r.Id == rule.Id);
        string signature = System.Text.Json.JsonSerializer.Serialize(all);
        if (signature != cardUsesSignature) { cardUsesCache = FieldUses(all); cardUsesSignature = signature; }
        var uses = cardUsesCache;
        // Include directly authored sources too, including custom fields absent from the built-in object schema.
        var authored = rule.Effects.Where(e => e.Influence != null).SelectMany(e => e.Influence.Terms
            .SelectMany(t => GraphInputs(rule.Scope, e, t).Select(input => input.Source).Append(t.AmountField)))
            .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => FieldTitle(rule.Scope, f));
        var equations = rule.Effects.Where(e => e.Computation != null).Select(e => e.Computation)
            .Concat(rule.ConditionGraph == null ? Array.Empty<BehaviorGraph>() : new[] { rule.ConditionGraph });
        var equationReads = equations.SelectMany(ResponseRuleDescription.ConnectedNodes)
            .Where(n => n.Operation is BehaviorOperation.Input or BehaviorOperation.NamedValue).Select(n => FieldTitle(rule.Scope, n.Key));
        var reads = authored.Concat(equationReads).Concat(uses.Where(u => u.Readers.Any(r => r.Rule == self)).Select(u => KeyDisplay(u.Key))).Distinct().ToArray();
        var targets = rule.Effects.SelectMany(e => e.Mode == "flow" ? new[] { e.Target, e.To } : new[] { e.Target }).Where(f => f.Length > 0).Select(f => FieldTitle(rule.Scope, f)).Distinct();
        var consumers = uses.Where(u => u.Writers.Any(w => w.Rule == self)).SelectMany(u => u.Readers.Where(r => r.Rule != self && r.Enabled).Select(r => r.Name)).Distinct().ToArray();
        string when = rule.Schedule switch { "passage" => "Jármű áthaladásakor", "month" => "Minden erdőhónap végén", "growth" => "A fa növekedési rátájának frissítésekor", "wildlife" => "Minden állati lépésben; a lépés időegysége másodperc", "population" => "Állománykezeléskor, élőhelyfelmérés után; másodpercben", "birth" => "Létrehozáskor, csak az új egyedeken; egyszeri kezdőállapot", _ => "0,5 játékmásodpercenként, szeletelt feldolgozással" };
        Facts(rule.Name, "Állapotot változtat", when, reads, string.Join("; ", targets),
            consumers.Length > 0 ? string.Join("; ", consumers) : "Az érintett objektum állapotát használó folyamatok, a megjelenítés és a mentés.", rule.Enabled,
            "Kikapcsolva: nem hajtja végre a hatásait. A korábbi változtatásokat nem vonja vissza; más szabályok tovább hathatnak az objektumra.");
    }
    private void ProcessRuleCard(BehaviorGraph graph)
    {
        var hook = WorldBehaviorPolicy.Hooks.Single(h => h.Id == graph.Hook && h.Kind == graph.Kind);
        var nodes = ResponseRuleDescription.ConnectedNodes(graph);
        var reads = nodes.Where(n => BehaviorModel.Inputs(n.Operation) == 0).Select(n => n.Operation switch {
            BehaviorOperation.Input or BehaviorOperation.NamedValue => FieldTitle(graph.Kind, n.Key),
            BehaviorOperation.Native => "A beépített alapfolyamat eredménye",
            BehaviorOperation.State => hook.State,
            BehaviorOperation.Amount => hook.Amount,
            BehaviorOperation.Constant => "Állandó: " + n.Value.ToString("G5"),
            _ => n.Name
        }).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        Facts(graph.Name, "Folyamatkapcsolat", hook.Schedule, reads, hook.Name + " · " + hook.Unit,
            "A kapcsolt " + ObjectDisplay(graph.Kind, graph.Kind) + " folyamat. Egyedi kötés esetén ez helyettesíti a típusszintű gráfot.", true,
            "A kötés törlése a típusszintű gráfra, annak hiányában a beépített alapfolyamatra áll vissza. Nem állítja le a teljes folyamatot.");
        if (nodes.Any(n => n.Operation == BehaviorOperation.Native))
            ImGui.TextWrapped("Ez a gráf beépített alapfolyamat eredményét is használja. Az alapfolyamat teljes gráfos átvezetése még hátravan.");
    }
    private void ResponseRuleCard(BehaviorGraph graph)
    {
        var hook = WorldBehaviorPolicy.Hooks.Single(h => h.Id == graph.Hook && h.Kind == graph.Kind);
        var info = ResponseRuleDescription.For(graph.Hook);
        var reads = ResponseRuleDescription.ConnectedInputs(graph).Select(f => FieldTitle(graph.Kind, f)).ToArray();
        var downstream = info.GraphConsumers(behaviors.Document.Graphs.Where(g => g.Id != graph.Id));
        string consumers = "Bekötött szabályok: " + (downstream.Length == 0 ? "nincs" : string.Join("; ", downstream)) + ". Játékbeli felhasználás: " + info.Consumers;
        string when = LogisticsRules.IsKind(graph.Kind) ? hook.Schedule : graph.Kind switch {
            "gameplay" or "environment" or "vehicleResponse" => hook.Schedule,
            "initialTree" => "Új fa létrehozásakor: generálás, festés, ültetés vagy újulat",
            "regeneration" => "Az erdőhónap végén, az újulat megtelepedésénél",
            "competitionPair" => "A szomszédos fák versengésének frissítésekor, fapáronként",
            "competition" => "A fa összegzett fény- és térválaszának frissítésekor",
            "resource" => "Növekedés, egészség és stressz értékelésekor",
            _ => graph.Hook == "forest.site" ? "Generálás, ültetés, újulat és a fák erőforrásainak frissítésekor"
                : graph.Hook == "water.stress" ? "A fák pillanatnyi és havi vízválaszának lekérdezésekor" : "Környezeti frissítéskor, csempénként"
        };
        Facts(graph.Name, "Értéket számol", when, graph.Disabled ? Array.Empty<string>() : reads, info.Result, consumers, !graph.Disabled,
            graph.Disabled ? $"Kikapcsolva: a gráf helyett mindig {(graph.DisabledValue ?? 0).ToString("G12", System.Globalization.CultureInfo.GetCultureInfo("hu-HU"))} {hook.Unit} az eredmény. {info.FixedMeaning}"
                : "Kikapcsoláskor előbb kiválasztod a helyettesítő értéket. A gráf megmarad, visszakapcsoláskor ismét az összefüggése fut.");
        if (requestedResponseOff == graph.Id) {
            ImGui.TextWrapped("Mi legyen az eredmény, amíg ez a szabály ki van kapcsolva?");
            ImGui.InputDouble("Fix eredmény" + (hook.Unit.Length > 0 ? " (" + hook.Unit + ")" : ""), ref requestedResponseValue, 0, 0, "%.12g");
            ImGui.TextWrapped(info.FixedMeaning);
            bool valid = double.IsFinite(requestedResponseValue) && requestedResponseValue >= hook.Min && requestedResponseValue <= hook.Max;
            if (!valid) ImGui.TextWrapped($"Az érték {RuleSentence.Number(hook.Min)} és {RuleSentence.Number(hook.Max)} közé essen.");
            ImGui.BeginDisabled(!valid);
            if (ImGui.Button("Kikapcsolás ezzel az eredménnyel")) {
                behaviors.SetAvailability(graph.Id, true, requestedResponseValue); pendingCommonChange = true; requestedResponseOff = null;
            }
            RuleCardAnchors["off.confirm"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.EndDisabled(); ImGui.SameLine(); if (ImGui.Button("Mégsem")) requestedResponseOff = null;
            RuleCardAnchors["off.cancel"] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            ImGui.Separator();
        }
        if (graph.Disabled) {
            double fixedValue = graph.DisabledValue ?? info.SuggestedOffValue;
            if (ImGui.InputDouble("Helyettesítő eredmény" + (hook.Unit.Length > 0 ? " (" + hook.Unit + ")" : ""), ref fixedValue, 0, 0, "%.12g") && double.IsFinite(fixedValue) && fixedValue >= hook.Min && fixedValue <= hook.Max) {
                behaviors.SetAvailability(graph.Id, true, fixedValue); pendingCommonChange = true;
            }
        }
    }
    private void RequestResponseAvailability(BehaviorGraph graph, bool enabled)
    {
        ShowCommonRule("behavior:" + graph.Id);
        if (enabled) { behaviors.SetAvailability(graph.Id, false); pendingCommonChange = true; requestedResponseOff = null; }
        else { requestedResponseOff = graph.Id; requestedResponseValue = graph.DisabledValue ?? ResponseRuleDescription.For(graph.Hook).SuggestedOffValue; }
    }
}
