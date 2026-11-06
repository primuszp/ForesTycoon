using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
namespace ForesTycoon.Editor;
internal sealed partial class RuleWorkspaceView
{
    private ref bool scrollRuleCard => ref ui.Rules.ScrollRuleCard;
    private ref string requestedResponseOff => ref ui.Rules.RequestedResponseOff;
    private ref double requestedResponseValue => ref ui.Rules.RequestedResponseValue;
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
