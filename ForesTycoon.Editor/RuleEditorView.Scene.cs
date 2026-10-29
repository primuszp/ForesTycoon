using System;
using System.IO;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>The scene document: one file holds the map, missions and rules. The header's Jelenet menu opens and saves it.</summary>
internal sealed partial class RuleEditorView
{
    // The game starts from this document: saving it here is what the game plays next.
    private string scenePath = WorldLoader.OpeningScenePath()
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "scenes", "opening.json");
    private string scenePathEdit;

    private Task<string> sceneSaveTask;

    private bool EditingGameScene => WorldLoader.OpeningScenePath() is string game && Path.GetFullPath(scenePath) == Path.GetFullPath(game);

    /// <summary>The Jelenet button and its menu; Ctrl+O opens it.</summary>
    private void SceneMenu(GameWorld world)
    {
        if (ImGui.Button("Jelenet…") || ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.O)) { scenePathEdit = scenePath; ImGui.OpenPopup("scene-menu"); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Megnyitás, mentés, mentés másként · Ctrl+O");
        if (!ImGui.BeginPopup("scene-menu")) return;
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
            Try(() => {
                string previous = scenePath; scenePath = scenePathEdit;
                try { SaveScene(world); } catch { scenePath = previous; throw; }
            });
            ImGui.CloseCurrentPopup();
        }

        EditorStyle.Section("Kiindulás");
        if (gameScene != null && ImGui.Button("A játék nyitójelenete", new Vec2(220, 0))) { Try(() => OpenScene(world, gameScene)); ImGui.CloseCurrentPopup(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(gameScene);
        if (ImGui.Button("Eredeti generált nyitójelenet", new Vec2(220, 0))) {
            Try(() => {
                scenario = WorldLoader.OpeningProject().World;
                seedEdit = scenario.Seed;
                ApplyScenario(world, "Az eredeti, generált nyitójelenet betöltve (küldetések nélkül). Mentéskor az aktív fájlba kerül.");
            });
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A beépített generátor kezdővilága, ahogy a szerkesztés előtt volt.");
        ImGui.EndPopup();
    }

    private void SaveScene(GameWorld world)
    {
        if (sceneSaveTask != null) return;
        var project = CollectProject(world); project.Algorithms = null;
        string destination = Path.GetFullPath(scenePath);
        object species = world.WithSpecies(ForestSpeciesBook.CaptureContext);
        sceneSaveTask = Task.Run(() => ForestSpeciesBook.WithContext(species, () => {
            using var check = WorldLoader.CreateProject(project);
            RuleProject.WriteAtomic(destination, project.ToJson()); return destination;
        }));
        message = "A jelenet ellenőrzése és mentése folyamatban…";
    }

    private void RefreshSceneSave()
    {
        if (sceneSaveTask?.IsCompleted != true) return;
        try { message = "Jelenet és szabályok mentve: " + sceneSaveTask.GetAwaiter().GetResult(); statusFrame = -1000; }
        catch (Exception error) { message = "A jelenet nem menthető: " + error.Message; }
        finally { sceneSaveTask = null; }
    }

    private void OpenScene(GameWorld world, string path)
    {
        var project = RuleProject.FromJson(File.ReadAllText(path));
        if (project.World == null) throw new InvalidDataException("A fájl nem tartalmaz jelenetet.");
        world.RequestScenario(project.World, project);
        LoadProject(world, project);
        scenePath = path;
        appliedScenario = ScenarioKey(); scenarioBuilt = true;
        statusFrame = -1000; ClearHistory();
        message = "Jelenet megnyitva: " + Path.GetFullPath(path);
    }
}
