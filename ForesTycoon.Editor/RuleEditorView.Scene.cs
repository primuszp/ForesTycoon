using System;
using System.IO;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>The scene document: one file holds the map, missions and rules. The header's Jelenet menu opens and saves it.</summary>
internal sealed partial class RuleEditorView
{
    private string scenePath { get => workflow.ScenePath; set => workflow.ScenePath = value; }
    private string scenePathEdit;

    private bool EditingGameScene => WorldLoader.OpeningScenePath() is string game && Path.GetFullPath(scenePath) == Path.GetFullPath(game);

    /// <summary>The Jelenet button and its menu; Ctrl+O opens it.</summary>
    private void SceneMenu(GameWorld world)
    {
        if (ImGui.Button("Jelenet…") || ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.O)) { scenePathEdit = scenePath; ImGui.OpenPopup("scene-menu"); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Megnyitás, mentés, mentés másként · Ctrl+O");
        if (!ImGui.BeginPopup("scene-menu")) return;
        ImGui.BeginDisabled(savingScene || publishing);
        string gameScene = WorldLoader.OpeningScenePath();
        EditorStyle.Section("Aktív jelenet");
        ImGui.TextUnformatted(Path.GetFullPath(scenePath));
        if (EditingGameScene) EditorStyle.Text(HudTheme.Good, "Ez a játék indulójelenete: mentés után a játék ezt futtatja.");
        bool unsaved = !statusCache.SceneSaved;
        if (unsaved) EditorStyle.Text(HudTheme.AmberAccent, "Mentetlen módosítás: megnyitás előtt mentsd, különben elvész.");
        ImGui.Spacing();
        if (ImGui.Button("Mentés · Ctrl+S", new Vec2(220, 0))) { Try(() => SaveAll(world)); ImGui.CloseCurrentPopup(); }

        EditorStyle.Section("Másik fájl");
        ImGui.SetNextItemWidth(520);
        ImGui.InputText("##scene-path", ref scenePathEdit, 1024);
        if (ImGui.Button("Megnyitás", new Vec2(140, 0))) { Try(() => OpenScene(world, scenePathEdit)); ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Mentés másként", new Vec2(140, 0))) {
            Try(() => SaveScene(world, scenePathEdit));
            ImGui.CloseCurrentPopup();
        }

        EditorStyle.Section("Kiindulás");
        if (gameScene != null && ImGui.Button("A játék nyitójelenete", new Vec2(220, 0))) { Try(() => OpenScene(world, gameScene)); ImGui.CloseCurrentPopup(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(gameScene);
        if (ImGui.Button("Eredeti generált nyitójelenet", new Vec2(220, 0))) {
            Try(() => {
                document.Scene.Replace(WorldLoader.OpeningProject().World);
                seedEdit = document.World.Seed;
                ApplyScenario(world, "Az eredeti, generált nyitójelenet betöltve (küldetések nélkül). Mentéskor az aktív fájlba kerül.");
            });
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A beépített generátor kezdővilága, ahogy a szerkesztés előtt volt.");
        ImGui.EndDisabled(); ImGui.EndPopup();
    }

    private void SaveScene(GameWorld world, string destination = null, RuleProject snapshot = null)
    {
        CompleteEdits();
        ApplyWorkflowResult(workflow.BeginSceneSave(snapshot ?? CollectProject(), Runtime(world), destination));
    }

    private void OpenScene(GameWorld world, string path)
    {
        CompleteEdits();
        var operation = workflow.OpenScene(path, Runtime(world), project => {
            LoadProject(world, project);
            world.RequestScenario(project.World, EditorDocument.ForSandbox(project));
        });
        appliedScenario = ScenarioKey(); scenarioBuilt = true;
        ApplyWorkflowResult(operation);
    }
}
