#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;
/// <summary>Read-only scene query boundary. Editing delegates are the only API that receives an owned model.</summary>
internal sealed class SceneSession
{
    private readonly EditorSceneCommands editor;
    private readonly EditorReadCache<WorldScenario, SceneView> worldScenarios;
    private readonly EditorReadCache<SoilTypeDefinition, SoilView> soilTypeDefinitions;
    private readonly EditorReadCache<SoilHorizon, SoilHorizonView> soilHorizons;
    private readonly EditorReadCache<SpeciesDefinition, SpeciesView> speciesDefinitions;
    private readonly EditorReadCache<TreeShapeDefinition, TreeShapeView> treeShapeDefinitions;
    private readonly EditorReadCache<ElevationZoneDefinition, ElevationZoneView> elevationZoneDefinitions;
    private readonly EditorReadCache<BiomeDefinition, BiomeView> biomeDefinitions;
    private readonly EditorReadCache<GroundMaterialDefinition, GroundMaterialView> groundMaterialDefinitions;
    private readonly EditorReadCache<MissionGoal, MissionView> missionGoals;
    private readonly EditorReadCache<RoadSegment, RoadView> roadSegments;
    internal SceneView World => View(editor.World)!;
    internal SceneSession(EditorSceneCommands editor)
    {
        this.editor = editor;
        worldScenarios = new(value => new(value, this));
        soilTypeDefinitions = new(value => new(value, this));
        soilHorizons = new(value => new(value, this));
        speciesDefinitions = new(value => new(value, this));
        treeShapeDefinitions = new(value => new(value, this));
        elevationZoneDefinitions = new(value => new(value, this));
        biomeDefinitions = new(value => new(value, this));
        groundMaterialDefinitions = new(value => new(value, this));
        missionGoals = new(value => new(value, this));
        roadSegments = new(value => new(value, this));
    }
    internal SceneView? View(WorldScenario? value) => worldScenarios.View(value);
    private WorldScenario Resolve(SceneView value) => worldScenarios.Model(value);
    internal SoilView? View(SoilTypeDefinition? value) => soilTypeDefinitions.View(value);
    private SoilTypeDefinition Resolve(SoilView value) => soilTypeDefinitions.Model(value);
    internal SoilHorizonView? View(SoilHorizon? value) => soilHorizons.View(value);
    internal SpeciesView? View(SpeciesDefinition? value) => speciesDefinitions.View(value);
    private SpeciesDefinition Resolve(SpeciesView value) => speciesDefinitions.Model(value);
    internal TreeShapeView? View(TreeShapeDefinition? value) => treeShapeDefinitions.View(value);
    internal ElevationZoneView? View(ElevationZoneDefinition? value) => elevationZoneDefinitions.View(value);
    private ElevationZoneDefinition Resolve(ElevationZoneView value) => elevationZoneDefinitions.Model(value);
    internal BiomeView? View(BiomeDefinition? value) => biomeDefinitions.View(value);
    private BiomeDefinition Resolve(BiomeView value) => biomeDefinitions.Model(value);
    internal GroundMaterialView? View(GroundMaterialDefinition? value) => groundMaterialDefinitions.View(value);
    private GroundMaterialDefinition Resolve(GroundMaterialView value) => groundMaterialDefinitions.Model(value);
    internal MissionView? View(MissionGoal? value) => missionGoals.View(value);
    private MissionGoal Resolve(MissionView value) => missionGoals.Model(value);
    internal RoadView? View(RoadSegment? value) => roadSegments.View(value);
    internal void Edit(SceneView target, Action<WorldScenario> change) => editor.Edit(Resolve(target), change);
    internal void Edit(SoilView target, Action<SoilTypeDefinition> change) => editor.Edit(Resolve(target), change);
    internal void Edit(SpeciesView target, Action<SpeciesDefinition> change) => editor.Edit(Resolve(target), change);
    internal void Edit(ElevationZoneView target, Action<ElevationZoneDefinition> change) => editor.Edit(Resolve(target), change);
    internal void Edit(BiomeView target, Action<BiomeDefinition> change) => editor.Edit(Resolve(target), change);
    internal void Edit(GroundMaterialView target, Action<GroundMaterialDefinition> change) => editor.Edit(Resolve(target), change);
    internal void Edit(MissionView target, Action<MissionGoal> change) => editor.Edit(Resolve(target), change);
    internal Dictionary<ForestSpecies, ForestSpeciesEdit> SpeciesEdits() => SimulationWorld.SpeciesEdits(editor.World);
    internal IEnumerable<EditorMissionProgress> MissionStatus(SimulationWorld world)
    {
        foreach (var status in world.MissionStatus(editor.World.Missions)) yield return new(status.Value, status.State, status.Error);
    }
    internal void EditWorld(Action<WorldScenario> change) => editor.EditWorld(change);
    internal void Replace(WorldScenario scenario) => editor.Replace(scenario);
    internal void SetTuning(string key, double value) => editor.SetTuning(key, value);
    internal SoilView CopySoil(SoilView source) => View(editor.CopySoil(Resolve(source)))!;
    internal void DeleteSoil(SoilView soil, bool usedInRuntime) => editor.DeleteSoil(Resolve(soil), usedInRuntime);
    internal void EditHorizons(SoilView soil, Action<List<SoilHorizon>> edit) => editor.EditHorizons(Resolve(soil), edit);
    internal void EditShape(SpeciesView species, string? phase, Action<Dictionary<string, double>> edit) => editor.EditShape(Resolve(species), phase, edit);
    internal void EditColours(SpeciesView species, Action<TreeColoursDefinition> edit) => editor.EditColours(Resolve(species), edit);
    internal MissionView AddMission() => View(editor.AddMission())!;
    internal void DeleteMission(MissionView mission) => editor.DeleteMission(Resolve(mission));
    internal void ResetMaterials() => editor.ResetMaterials();
    internal bool MaterialUsed(GroundMaterialView material) => editor.MaterialUsed(Resolve(material));
    internal void DeleteMaterial(GroundMaterialView material) => editor.DeleteMaterial(Resolve(material));
    internal void Resize(int size) => editor.Resize(size);
    internal void SetRelief(int relief) => editor.SetRelief(relief);
    internal void PaintSoil(IEnumerable<int> tiles, string soil) => editor.PaintSoil(tiles, soil);
    internal void PaintForest(IEnumerable<int> eligibleTiles, string species) => editor.PaintForest(eligibleTiles, species);
    internal void PaintHeights(IEnumerable<KeyValuePair<int, int>> heights) => editor.PaintHeights(heights);
    internal void PaintSprings(IEnumerable<int> tiles, double? rate) => editor.PaintSprings(tiles, rate);
}

internal readonly record struct EditorMissionProgress(double Value, MissionState State, string? Error);
