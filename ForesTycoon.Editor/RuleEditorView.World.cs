using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The world pages: the generated and painted map, the types it is painted with (soil, species, surfaces), the vehicles'
/// data and game parameters, and the rain trial. <see cref="WorldTab"/> and <see cref="TypeTab"/> remain the entry points
/// other code uses to open a page.
/// </summary>
internal sealed partial class RuleEditorView
{
    private ref MapLayer mapLayer => ref ui.Map.Layer;
    private string appliedScenario;
    private TerrainMap appliedScenarioMap;
    private (Task<TerrainMap> Completion, string Key) previewBuild;
    private ref int selectedTile => ref ui.Map.SelectedTile;
    private ref int selectedSoilType => ref ui.Soils.Selected;
    private ref int selectedSpecies => ref ui.Species.Selected;
    private ref int seedEdit => ref ui.Map.Seed;
    private HashSet<int> stroke => ui.Map.Stroke;


    private TrialService trials => trialEditor.Service;

    internal void ShowWorldTrial(WorldTrialResult result) { ShowPage(EditorPage.Trial); trials.Show(result); }
    internal void ShowWorld(WorldTab tab, MapLayer layer = MapLayer.Soil, int tile = -1, string rule = null)
    {
        ShowPage(tab switch {
            WorldTab.Types => EditorPage.Soils, WorldTab.Objects => EditorPage.Objects, WorldTab.Missions => EditorPage.Missions,
            WorldTab.Rules => EditorPage.Rules, WorldTab.Trial => EditorPage.Trial, _ => EditorPage.Map });
        mapLayer = layer; if (tile >= 0) selectedTile = tile;
    }
    /// <summary>A spring on the map, then some simulated time: shows where its water goes.</summary>
    internal void AddSpring(GameWorld world, int tile, double rate, double seconds)
    {
        document.Scene.PaintSprings(new[] { tile }, rate); ApplyProject(world, false); world.ExecutePendingCommands();
        for (int i = 0; i < seconds * 30; i++) world.Update(1.0 / 30);
    }
    private ref bool scrollToSoilProfile => ref ui.Soils.ScrollToProfile;
    internal void ShowSoilProfileForTest(int index) { ShowWorldTypes(TypeTab.Soil, index); scrollToSoilProfile = true; }
    internal void ShowWorldTypes(TypeTab tab, int index = 0)
    {
        ShowPage(tab switch {
            TypeTab.Species => EditorPage.Species, TypeTab.Surfaces => EditorPage.Surfaces, TypeTab.Vehicles => EditorPage.Vehicles,
            TypeTab.Parameters => EditorPage.Parameters, _ => EditorPage.Soils });
        if (tab == TypeTab.Soil) selectedSoilType = index;
        else if (tab == TypeTab.Species) selectedSpecies = index;
    }

    private void SoilTypesTab(GameWorld world) => soilEditor.Draw(world);
    private void SpeciesTab(GameWorld world) => speciesEditor.Draw(world);

    private WorldScenario Scenario(GameWorld world) => document.World;
    /// <summary>The editor's test world is rebuilt from the map whenever the map or a type changes.</summary>
    private void ApplyScenario(GameWorld world, string what)
    {
        MarkDocumentChanged();
        try {
            document.World.Validate();
            var project = CollectProject(); project.Algorithms = null;
            RequestPreviewBuild(world, project);
            message = what + " A tesztvilág újraépítése folyamatban.";
        } catch (Exception e) when (e is System.IO.InvalidDataException or ArgumentException or InvalidOperationException) { message = e.Message; }
    }

    private void RequestPreviewBuild(GameWorld world, RuleProject project)
    {
        previewBuild = (world.RequestScenario(project.World, project), document.PreviewMapKey());
        statusFrame = -1000;
    }

    private void RefreshPreviewBuild(GameWorld world)
    {
        if (appliedScenarioMap != world.Map) { appliedScenario = null; appliedScenarioMap = null; statusFrame = -1000; }
        if (previewBuild.Completion?.IsCompleted != true) return;
        if (previewBuild.Completion.GetAwaiter().GetResult() == world.Map) {
            appliedScenario = previewBuild.Key;
            appliedScenarioMap = world.Map;
            message = world.SceneProblems.Count == 0 ? "A tesztvilág felépült."
                : $"A tesztvilág felépült, {world.SceneProblems.Count} építési hibával. {world.SceneProblems[0]}";
        } else {
            message = world.GenerationError ?? "A tesztvilág építése megszakadt.";
        }
        previewBuild = default;
        statusFrame = -1000;
    }

    private bool PreviewCurrent() => previewBuild.Completion == null && appliedScenario == document.PreviewMapKey();

    // ── Where the work stands: file, test world, game ────────────────────
    private (string Path, DateTime Written, string Key) sceneFile;
    /// <summary>The scene file's content in the same normal form as the edited document; re-read only when the file changes.</summary>
    private string SceneFileKey()
    {
        try {
            if (!System.IO.File.Exists(scenePath)) return null;
            var written = System.IO.File.GetLastWriteTimeUtc(scenePath);
            if (sceneFile.Path != scenePath || sceneFile.Written != written) {
                sceneFile = (scenePath, written, SceneKey(RuleProject.FromJson(System.IO.File.ReadAllText(scenePath))));
            }
            return sceneFile.Key;
        } catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException) { return null; }
    }
    private (bool SceneSaved, bool WorldCurrent) statusCache;
    private int statusFrame = -1000;

    private string SceneDocumentKey(GameWorld world) => SceneKey(CollectProject());
    // The saved file and the edited document are compared in the form the game runs: migrated and normalised alike.
    private static string SceneKey(RuleProject project)
    {
        project = project.Clone();
        try { project = GameWorld.NormalizeRuleProject(project); } catch (Exception e) when (Expected(e)) { }
        return EditorDocument.RevisionOf(project);
    }

    private void SaveAll(GameWorld world) => world.AtTickBoundary(() => Try(() => SaveAllAtBoundary(world)));

    private void SaveAllAtBoundary(GameWorld world)
    {
        CompleteEdits(); document.Invalidate();
        ApplyWorkflowResult(workflow.SaveAll(CollectProject(), Runtime(world)));
        RefreshWorkshopRevision(world);
    }

    private void RefreshStatus(GameWorld world)
    {
        // Serializing the whole scene is not free: refresh twice a second.
        if (ImGui.GetFrameCount() - statusFrame > 30) {
            statusFrame = ImGui.GetFrameCount();
            statusCache = (SceneFileKey() is string saved && saved == SceneDocumentKey(world), PreviewCurrent());
        }
    }


    private bool scenarioBuilt;
    private void MapTab(GameWorld world) => mapEditor.Draw(world);
    private void CancelStroke() => mapEditor.CancelStroke();

    internal void ShowBiomes(int index = 0, int tab = 0) => biomeEditor.Show(index, tab);
    internal void ShowMissions(int index = 0) => missionEditor.Show(index);

    internal static WorldTrialResult RunWorldTrial(RuleProject baseline, RuleProject edited, int intensity, int seconds)
        => TrialService.Run(baseline, edited, intensity, seconds);
}
