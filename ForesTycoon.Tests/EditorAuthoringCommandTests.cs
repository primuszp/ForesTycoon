namespace ForesTycoon.Tests;

public sealed class EditorAuthoringCommandTests
{
    private static (EditorDocument Document, SceneSession Scene) Open()
    {
        var document = new EditorDocument(); var project = SimulationWorld.DefaultRuleProject();
        project.World = SimulationWorld.DefaultScenario(42, 16); document.Load(project);
        return (document, new(document.Scene));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingAZoneRebindsItsBiomesAndUndoRestoresBoth(bool highest)
    {
        var (document, scene) = Open(); var zones = scene.World.ElevationZones;
        var removed = zones[highest ? zones.Count - 1 : 0]; var biome = scene.World.Biomes.First(b => b.Zone == removed.Id);
        string old = biome.Zone; string replacement = zones[highest ? zones.Count - 2 : 1].Id;
        scene.DeleteElevationZone(removed); Assert.Equal(replacement, biome.Zone); Assert.DoesNotContain(zones, z => z.Id == old);
        document.History.Undo(); Assert.Contains(scene.World.ElevationZones, z => z.Id == old);
        Assert.Contains(scene.World.Biomes, b => b.Zone == old);
        Assert.Throws<InvalidOperationException>(() => scene.DeleteElevationZone(removed));
        document.History.Redo(); Assert.DoesNotContain(scene.World.Biomes, b => b.Zone == old);
    }
    [Fact]
    public void ZoneBoundariesStayOrderedAndTheLastZoneCannotBeRemoved()
    {
        var (document, scene) = Open(); var zones = scene.World.ElevationZones;
        scene.SetElevationBoundary(zones[0], int.MaxValue); Assert.Equal(zones[1].MaxLevel - 1, zones[0].MaxLevel);
        scene.SetElevationBoundary(zones[0], int.MinValue); Assert.Equal(0, zones[0].MaxLevel);
        Assert.Throws<InvalidOperationException>(() => scene.SetElevationBoundary(zones[^1], 2));
        while (zones.Count > 1) scene.DeleteElevationZone(zones[0]);
        Assert.Throws<InvalidOperationException>(() => scene.DeleteElevationZone(zones[0]));
        Assert.All(scene.World.Biomes, biome => Assert.Equal(zones[0].Id, biome.Zone));
    }
    [Fact]
    public void AddingZonesAndBiomesEnforcesLimitsWithoutCreatingHistoryOnFailure()
    {
        var (document, scene) = Open();
        while (scene.CanAddElevationZone) scene.AddElevationZone();
        string revision = document.Revision(); int count = document.History.Count;
        Assert.Throws<InvalidOperationException>(() => scene.AddElevationZone()); Assert.Equal(revision, document.Revision()); Assert.Equal(count, document.History.Count);
        Assert.Throws<ArgumentException>(() => scene.AddBiome("missing"));
        while (scene.World.Biomes.Count < 32) scene.AddBiome(scene.World.ElevationZones[0].Id);
        Assert.Throws<InvalidOperationException>(() => scene.AddBiome(scene.World.ElevationZones[0].Id));
    }
    [Fact]
    public void BiomeBindingsAndDefaultsAlwaysReferToExistingZonesAndMaterials()
    {
        var (document, scene) = Open(); var biome = scene.World.Biomes[0]; string revision = document.Revision();
        Assert.Throws<ArgumentException>(() => scene.SetBiomeZone(biome, "missing"));
        Assert.Throws<ArgumentException>(() => scene.SetBiomeGround(biome, BiomeGroundKind.Dry, "missing"));
        Assert.Equal(revision, document.Revision());
        scene.SetBiomeZone(biome, scene.World.ElevationZones[^1].Id); scene.SetBiomeGround(biome, BiomeGroundKind.Dry, scene.World.GroundMaterials[^1].Id);
        while (scene.World.ElevationZones.Count > 1) scene.DeleteElevationZone(scene.World.ElevationZones[0]);
        scene.ResetBiomes();
        Assert.All(scene.World.Biomes, value =>
        {
            Assert.Contains(scene.World.ElevationZones, zone => zone.Id == value.Zone);
            Assert.Contains(scene.World.GroundMaterials, ground => ground.Id == value.DryGround);
        });
        scene.AddElevationZone(); scene.SetBiomeZone(scene.World.Biomes[0], scene.World.ElevationZones[0].Id); scene.ResetElevationZones();
        Assert.All(scene.World.Biomes, value => Assert.Contains(scene.World.ElevationZones, zone => zone.Id == value.Zone));
    }
    [Fact]
    public void MissionValidationIsEnforcedWithoutAViewAndKeepsRedoOnRejection()
    {
        var (document, scene) = Open(); scene.AddMission(); var mission = scene.AddMission(); scene.RenameMission(mission, "unique"); document.History.Undo();
        mission = scene.World.Missions[^1]; long version = document.Version; int count = document.History.Count;
        Assert.Throws<ArgumentException>(() => scene.RenameMission(mission, scene.World.Missions[0].Id));
        Assert.Throws<ArgumentException>(() => scene.SetMissionTitle(mission, " "));
        Assert.Throws<ArgumentException>(() => scene.SetMissionMetric(mission, "tree.health"));
        Assert.Throws<ArgumentException>(() => scene.SetMissionCondition(mission, "!=", 1));
        Assert.Throws<ArgumentException>(() => scene.SetMissionCondition(mission, ">=", double.NaN));
        Assert.Throws<ArgumentException>(() => scene.SetMissionDeadline(mission, double.PositiveInfinity));
        Assert.Equal(version, document.Version); Assert.Equal(count, document.History.Count); Assert.True(document.History.CanRedo);
        scene.SetMissionDeadline(mission, 2000); Assert.Equal(1000, mission.DeadlineYear);
    }
    [Fact]
    public void HorizonCommandsMaterializeInheritedProfilesAndEnforceLimits()
    {
        var (document, scene) = Open(); var soil = scene.World.SoilTypes[0]; scene.ResetHorizons(soil);
        Assert.Empty(soil.Horizons); var inherited = soil.Profile();
        scene.SetHorizonDepth(soil, 0, 100); Assert.Equal(5, soil.Horizons[0].Depth);
        Assert.Equal(inherited.Count, soil.Horizons.Count);
        string name = soil.Horizons[0].Name; scene.MoveHorizonUp(soil, 1); Assert.Equal(name, soil.Horizons[1].Name);
        while (soil.Horizons.Count < 8) scene.AddHorizon(soil);
        Assert.Throws<InvalidOperationException>(() => scene.AddHorizon(soil));
        Assert.Throws<ArgumentException>(() => scene.SetHorizonDepth(soil, 0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.DeleteHorizon(soil, 99));
        Assert.Throws<ArgumentException>(() => scene.SetHorizonColour(soil, 0, "invalid"));
        while (soil.Horizons.Count > 1) scene.DeleteHorizon(soil, 0);
        Assert.Throws<InvalidOperationException>(() => scene.DeleteHorizon(soil, 0));
        scene.ResetHorizons(soil); Assert.Empty(soil.Horizons); Assert.NotEmpty(soil.Profile());
    }
    [Fact]
    public void NamedCommandsJoinOneGestureAndRejectForeignHandles()
    {
        var (document, scene) = Open(); var soil = scene.World.SoilTypes[0]; int count = document.History.Count;
        using (var gesture = document.History.BeginEdit("profile"))
        {
            scene.SetHorizonName(soil, 0, "Surface"); scene.SetHorizonColour(soil, 0, "#AABBCC"); gesture.Commit();
        }
        Assert.Equal(count + 1, document.History.Count); document.History.Undo();
        Assert.Throws<InvalidOperationException>(() => scene.SetHorizonName(soil, 0, "stale"));
        var other = new SceneSession(document.Scene);
        Assert.Throws<InvalidOperationException>(() => other.AddMaterial(scene.World.GroundMaterials[0]));
        document.History.Redo(); Assert.Equal("Surface", scene.World.SoilTypes[0].Horizons[0].Name);
    }
    [Fact]
    public void AppearanceCommandsValidateValuesAndMaterializeOptionalShapeDataOnlyOnEdit()
    {
        var (document, scene) = Open(); var species = scene.World.Species[0];
        scene.Edit(species, value => value.Shape = new() { All = null!, Phases = null! });
        string revision = document.Revision();
        Assert.Throws<ArgumentException>(() => scene.SetShapeParameter(species, "missing", "1Branches", 3));
        Assert.Throws<ArgumentException>(() => scene.SetShapeParameter(species, null, "1Branches", double.NaN));
        Assert.Throws<ArgumentException>(() => scene.SetSpeciesColour(species, SpeciesColourChannel.Bark, "bad"));
        Assert.Throws<ArgumentException>(() => scene.SetSpeciesColour(species, SpeciesColourChannel.BarkPattern, "bad"));
        Assert.Equal(revision, document.Revision());
        scene.SetShapeParameter(species, null, "1Branches", 9); Assert.Equal(9, species.Shape!.All["1Branches"]);
        scene.SetShapeParameter(species, "Old", "1Branches", 5); Assert.Equal(5, species.Shape.Phases["Old"]["1Branches"]);
        scene.ResetShapePhase(species, "Old"); Assert.Empty(species.Shape.Phases);
        scene.SetShapeParameter(species, null, "1Branches", null); Assert.Null(species.Shape);
        scene.SetSpeciesColour(species, SpeciesColourChannel.Bark, "#112233"); Assert.Equal("#112233", species.Colours!.Bark);
        scene.SetSpeciesColour(species, SpeciesColourChannel.BarkPattern, "birch"); Assert.Equal("birch", species.Colours!.BarkPattern);
        scene.ResetSpeciesColours(species); Assert.Null(species.Colours);
        document.History.Undo(); Assert.Equal("birch", scene.World.Species[0].Colours!.BarkPattern);
    }
    [Fact]
    public void RestoringSpeciesKeepsItsIndexButRejectsTheReplacedHandle()
    {
        var (document, scene) = Open(); var species = scene.World.Species[0]; string id = species.Id, name = species.Name;
        scene.Edit(species, value => value.Name = "Changed"); scene.RestoreSpecies(species);
        Assert.Equal(id, scene.World.Species[0].Id); Assert.Equal(name, scene.World.Species[0].Name);
        Assert.Throws<InvalidOperationException>(() => scene.RestoreSpecies(species));
        document.History.Undo(); Assert.Equal("Changed", scene.World.Species[0].Name);
        document.History.Redo(); Assert.Equal(name, scene.World.Species[0].Name);
    }
}
