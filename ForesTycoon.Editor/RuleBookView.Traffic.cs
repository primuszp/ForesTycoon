using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleBookView
{
    private ref int passageTile => ref state.PassageTile;
    private ref int passageKind => ref state.PassageKind;
    private ref float passageMass => ref state.PassageMass;
    private ref float passageFill => ref state.PassageFill;

    private void SetRuleScope(RuleView rule, string scope) => ruleBook.SetScope(rule, scope);

    private void DrawSchedule(RuleView rule, ref bool changed)
    {
        if (rule.Scope == "passage") { ImGui.TextUnformatted("Minden áthaladáskor"); return; }
        var schedules = ruleBook.SchedulesFor(rule);
        int choice = Math.Max(0, Array.IndexOf(schedules, rule.Schedule));
        var options = schedules.Select(s => s == "step" ? "Időszakonként" : s == "month" ? "Erdőhónaponként" : s == "wildlife" ? "Állati lépésenként (másodperc)" : s == "population" ? "Állománykezeléskor, élőhelyfelmérés után" : s == "birth" ? "Létrehozáskor, az új egyedeken" : "Növekedési ráta frissítésekor").ToArray();
        if (ImGui.Combo("Mikor fusson?", ref choice, options, options.Length)) {
            ruleBook.SetSchedule(rule, schedules[choice]); changed = true;
        }
    }

    private void PassagePreviewArea(GameWorld world, RuleView rule)
    {
        ImGui.TextWrapped("Válassz utat vagy nyomot és egy járművet. Az előnézet egy áthaladás hatását mutatja; a próbagomb a tesztvilágot is megváltoztatja.");
        if (!world.Map.IsRoadTile(passageTile) && !world.Map.IsSkidTrail(passageTile))
            passageTile = world.Map.RoadTiles.Concat(world.Map.SkidTrailTiles).FirstOrDefault(-1);
        if (passageTile < 0) { ImGui.TextWrapped("A Pálya lapon építs utat vagy jelölj ki közelítőnyomot a próbához."); return; }
        string Label(int tile) => (world.Map.IsRoadTile(tile) ? "Út" : "Nyom") + " · csempe " + tile;
        if (ImGui.BeginCombo("Hely", Label(passageTile))) {
            foreach (int tile in world.Map.RoadTiles.Concat(world.Map.SkidTrailTiles).Take(400))
                if (ImGui.Selectable(Label(tile), tile == passageTile)) passageTile = tile;
            ImGui.EndCombo();
        }
        ImGui.InputInt("Csempeazonosító", ref passageTile);
        ImGui.Combo("Jármű", ref passageKind, new[] { "Teherautó", "Harveszter", "Kihordó" }, 3);
        ImGui.SliderFloat("Össztömeg (kg)", ref passageMass, 0, 1000000, "%.0f");
        ImGui.InputFloat("Tömeg számmal (kg)", ref passageMass, 100, 1000, "%.0f");
        ImGui.SliderFloat("Rakomány aránya", ref passageFill, 0, 1, "%.2f");
        ImGui.InputFloat("Rakomány számmal", ref passageFill, .01f, .1f, "%.2f");
        if (!float.IsFinite(passageMass)) passageMass = 36000;
        if (!float.IsFinite(passageFill)) passageFill = 1;
        passageMass = Math.Clamp(passageMass, 0, 1000000); passageFill = Math.Clamp(passageFill, 0, 1);
        if (!world.Map.IsRoadTile(passageTile) && !world.Map.IsSkidTrail(passageTile)) { ImGui.TextWrapped("Ezen a csempén nincs út vagy nyom."); return; }
        var passage = new TrafficPassage(passageTile, (TrafficVehicleKind)passageKind, 0, passageMass, passageFill);
        try {
            if (world.RuleBook.Rules.Any(r => r.Id == rule.Id && r.Schedule == "passage"))
                foreach (var row in world.PreviewPassage(rule.Id, passage)) {
                    ImGui.TextUnformatted($"{FieldDisplay(rule.Scope, row.Target)}: {row.Before:0.######} → {row.After:0.######}");
                    foreach (var part in row.Contributions)
                        EditorStyle.Text(part.Change == 0 ? HudTheme.Muted : HudTheme.Parchment, $"  {part.Name}: {part.Change:+0.######;-0.######;0}");
                }
            if (ImGui.Button("Egy áthaladás a tesztvilágban")) world.QueuePassage(passage);
            ImGui.TextWrapped("A próbagomb az összes bekapcsolt áthaladási szabályt futtatja, a szabálylista sorrendjében. A művelet a mentés és a visszajátszás naplójába is bekerül.");
        } catch (Exception e) when (Expected(e)) { EditorStyle.Text(HudTheme.Bad, e.Message); }
    }
}
