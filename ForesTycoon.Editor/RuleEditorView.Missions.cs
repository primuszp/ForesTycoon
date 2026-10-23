using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>The scene's missions: goals over world values, checked by the game while it runs.</summary>
internal sealed partial class RuleEditorView
{
    private int selectedMission;

    internal void ShowMissions(int index = 0) { page = EditorPage.Missions; selectedMission = index; }

    private void MissionsTab(GameWorld world)
    {
        var missions = scenario.Missions;
        EditorStyle.BeginPanel("missions-left", new Vec2(280, 0));
        EditorStyle.Section("Küldetések");
        for (int i = 0; i < missions.Count; i++)
            if (ImGui.Selectable($"{missions[i].Title}##mission{i}", selectedMission == i)) selectedMission = i;
        if (missions.Count == 0) EditorStyle.Text(HudTheme.Muted, "Ennek a pályának még nincs küldetése.");
        ImGui.Spacing();
        if (ImGui.Button("Új küldetés") && missions.Count < 32) {
            int n = missions.Count + 1;
            while (missions.Any(m => m.Id == $"kuldetes-{n}")) n++;
            missions.Add(new MissionGoal { Id = $"kuldetes-{n}", Title = $"Küldetés {n}" });
            selectedMission = missions.Count - 1;
        }
        if (missions.Count > 0) {
            ImGui.SameLine();
            if (ImGui.Button("Törlés")) { missions.RemoveAt(Math.Clamp(selectedMission, 0, missions.Count - 1)); selectedMission = Math.Max(0, selectedMission - 1); }
        }
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A küldetések a jelenettel együtt mentődnek; a játék a futó világ értékeiből ellenőrzi őket.");
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        ImGui.SameLine();
        EditorStyle.BeginPanel("missions-body", Vec2.Zero);
        if (missions.Count == 0) {
            EditorStyle.Text(HudTheme.Muted, "Az „Új küldetés” gomb felvesz egy célt: mérőszám, küszöb és határidő.");
            ImGui.EndChild(); return;
        }
        selectedMission = Math.Clamp(selectedMission, 0, missions.Count - 1);
        var goal = missions[selectedMission];
        EditorStyle.Section("Küldetés");
        string title = goal.Title, description = goal.Description ?? "", id = goal.Id, metric = goal.Metric;
        ImGui.SetNextItemWidth(420); if (ImGui.InputText("Cím", ref title, 128) && title.Trim().Length > 0) goal.Title = title;
        ImGui.SetNextItemWidth(420); if (ImGui.InputText("Azonosító", ref id, 64) && id.Trim().Length > 0 && !missions.Any(m => m != goal && m.Id == id)) goal.Id = id;
        if (ImGui.InputTextMultiline("Leírás", ref description, 1024, new Vec2(420, 70))) goal.Description = description;

        EditorStyle.Section("Feltétel");
        string current = GameWorld.MissionMetrics.FirstOrDefault(m => m.Key == goal.Metric).Name ?? goal.Metric;
        ImGui.SetNextItemWidth(260);
        if (ImGui.BeginCombo("Mérőszám", current)) {
            foreach (var (key, name) in GameWorld.MissionMetrics)
                if (ImGui.Selectable(name, goal.Metric == key)) goal.Metric = key;
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("Kulcs", ref metric, 128) && (metric.StartsWith("world.", StringComparison.Ordinal) || metric.StartsWith("stock.", StringComparison.Ordinal)))
            goal.Metric = metric;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Bármely world.* mező vagy a dinamika egy stock.* készlete.");
        int comparison = Math.Max(0, Array.IndexOf(MissionGoal.Comparisons, goal.Comparison));
        ImGui.SetNextItemWidth(80);
        if (ImGui.Combo("##cmp", ref comparison, new[] { "legalább", "legfeljebb" }, 2)) goal.Comparison = MissionGoal.Comparisons[comparison];
        ImGui.SameLine(); double target = goal.Target; ImGui.SetNextItemWidth(160);
        if (ImGui.InputDouble("Cél", ref target, 0, 0, "%.2f") && double.IsFinite(target)) goal.Target = target;
        double deadline = goal.DeadlineYear; ImGui.SetNextItemWidth(160);
        if (ImGui.InputDouble("Határidő (erdőév, 0 = nincs)", ref deadline, 0, 0, "%.2f")) goal.DeadlineYear = Math.Clamp(deadline, 0, 1000);

        EditorStyle.Section("Most a tesztvilágban");
        try { goal.Validate(); } catch (System.IO.InvalidDataException e) { EditorStyle.Text(HudTheme.Bad, e.Message); }
        var status = world.MissionStatus(scenario.Missions).ElementAt(selectedMission);
        if (status.Error != null) EditorStyle.Text(HudTheme.Bad, status.Error);
        else EditorStyle.Text(status.State switch { MissionState.Reached => HudTheme.Good, MissionState.Failed => HudTheme.Bad, _ => HudTheme.Muted },
            $"Érték: {status.Value:0.##} · cél: {(goal.Comparison == "<=" ? "legfeljebb" : "legalább")} {goal.Target:0.##} · "
            + status.State switch { MissionState.Reached => "teljesül", MissionState.Failed => "lejárt", _ => "folyamatban" });
        ImGui.EndChild();
    }
}
