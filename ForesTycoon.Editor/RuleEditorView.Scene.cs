using System;
using System.IO;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private string scenePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "scenes", "opening.json");

    private void SceneFiles(GameWorld world)
    {
        if (ImGui.Button("Nyitójelenet átvétele")) Try(() => {
            scenario = WorldLoader.OpeningProject().World;
            seedEdit = scenario.Seed;
            ApplyScenario(world, "A nyitójelenet pontos másolata betöltve.");
        });
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##scene-path", ref scenePath, 1024);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Önálló jelenetprojekt fájlja: " + scenePath);
        if (ImGui.Button("Jelenet mentése")) Try(() => {
            var project = CollectProject(world); project.Algorithms = null;
            using var check = WorldLoader.CreateProject(project);
            RuleProject.WriteAtomic(scenePath, project.ToJson());
            message = "Jelenet és szabályok mentve: " + Path.GetFullPath(scenePath);
        });
        ImGui.SameLine();
        if (ImGui.Button("Megnyitás")) Try(() => {
            var project = RuleProject.FromJson(File.ReadAllText(scenePath));
            if (project.World == null) throw new InvalidDataException("A fájl nem tartalmaz jelenetet.");
            world.StartScenario(project.World, project);
            LoadProject(world, project);
            appliedScenario = ScenarioKey(); scenarioBuilt = true;
            message = "Jelenet megnyitva: " + Path.GetFullPath(scenePath);
        });
    }
}
