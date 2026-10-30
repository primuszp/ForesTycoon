using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class SceneSoilEditingTests
{
    private static RuleProject Project()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var project = world.RuleProjectDocument;
        project.World = WorldLoader.CaptureScenario(world);
        return project;
    }
    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void PaintingChangesOnlyTheChosenSoilAndPreservesTerrainTreesAndWater()
    {
        var project = Project();
        using var original = WorldLoader.CreateProject(project);
        var source = original.CaptureCheckpoint();
        int tile = 42;
        int index = (original.SoilIndex(tile) + 1) % project.World.SoilTypes.Count;
        project.World.SoilPaint[tile] = project.World.SoilTypes[index].Id;
        using var edited = WorldLoader.CreateProject(RuleProject.FromJson(project.ToJson()));
        for (int id = 0; id < 256; id++)
            Assert.Equal(id == tile ? index : original.SoilIndex(id), edited.SoilIndex(id));
        Assert.Equal(Json(source.Terrain), Json(edited.CaptureCheckpoint().Terrain));
        Assert.Equal(Json(source.Ecology.Forest.Trees), Json(edited.CaptureCheckpoint().Ecology.Forest.Trees));
        Assert.Equal(original.Environment.StoredWater, edited.Environment.StoredWater, 7);
        Assert.InRange(Math.Abs(edited.Environment.BalanceError), 0, 1e-7);
        project.World.SoilPaint.Clear();
        edited.StartScenario(project.World, project);
        Assert.Equal(Json(source), Json(edited.CaptureCheckpoint()));
    }

    [Fact]
    public void LowerCapacityMovesExcessToSurfaceAndSurvivesSaveAndSimulation()
    {
        var project = Project();
        using var original = WorldLoader.CreateProject(project);
        int tile = 42; var type = project.World.SoilTypes[original.SoilIndex(tile)];
        type.Saturation = 5; type.FieldCapacity = 3; type.WiltingPoint = 1;
        type.InfiltrationPerHour = 0.25; type.BearingCapacity = .123;
        using var edited = WorldLoader.CreateProject(project);
        var old = original.Environment.Cell(tile); var cell = edited.Environment.Cell(tile);
        Assert.Equal(5, cell.Soil);
        Assert.Equal(old.Surface + old.Soil - 5, cell.Surface, 9);
        Assert.Equal(original.Environment.StoredWater, edited.Environment.StoredWater, 7);
        Assert.Equal(.25, RuleEngine.Scope("tile").Fields.Single(f => f.Name == "infiltration").Read(edited, tile));
        Assert.Equal(.123, edited.Soils.Profile(tile).Properties.BearingCapacity);
        Assert.Equal(Json(original.CaptureCheckpoint().Ecology.Forest.Trees), Json(edited.CaptureCheckpoint().Ecology.Forest.Trees));
        using var stream = new MemoryStream(); edited.Save(stream); stream.Position = 0;
        using var restored = WorldLoader.CreateProject(Project()); restored.Load(stream);
        Assert.Equal(Json(edited.CaptureCheckpoint()), Json(restored.CaptureCheckpoint()));
        for (int i = 0; i < 60; i++) { edited.Update(1d / 30); restored.Update(1d / 30); }
        double month = edited.Environment.ForestYearSeconds / 12 + .1;
        edited.Update(month); restored.Update(month);
        Assert.Equal(Json(edited.CaptureCheckpoint()), Json(restored.CaptureCheckpoint()));
        Assert.InRange(Math.Abs(restored.Environment.BalanceError), 0, 1e-6);
        var recaptured = project.Clone(); recaptured.World = WorldLoader.CaptureScenario(restored);
        using var reopened = WorldLoader.CreateProject(recaptured);
        Assert.Equal(restored.Environment.StoredWater, reopened.Environment.StoredWater, 7);
        Assert.Equal(Json(restored.CaptureCheckpoint().Ecology), Json(reopened.CaptureCheckpoint().Ecology));
    }

    [Fact]
    public void CatalogReorderingAndGeneratorPreferencesDoNotMoveExistingSoilPatches()
    {
        var project = Project();
        using var original = WorldLoader.CreateProject(project);
        project.World.SoilTypes.Reverse();
        foreach (var type in project.World.SoilTypes) { type.PreferredMoisture = 1 - type.PreferredMoisture; type.PreferredTexture = 0; }
        using var edited = WorldLoader.CreateProject(project);
        for (int tile = 0; tile < 256; tile++) Assert.Equal(original.Soils.Profile(tile).Id, edited.Soils.Profile(tile).Id);
        Assert.Equal(Json(original.CaptureCheckpoint().Ecology.Forest.Trees), Json(edited.CaptureCheckpoint().Ecology.Forest.Trees));
    }

    [Fact]
    public void NewTypeCanBePaintedAndUsedTypeCannotDisappearSilently()
    {
        var project = Project();
        using var world = WorldLoader.CreateProject(project);
        string initial = Json(world.CaptureCheckpoint());
        string used = world.Soils.Profile(42).Id;
        var deleted = project.Clone(); deleted.World.SoilTypes.RemoveAll(t => t.Id == used);
        Assert.Throws<InvalidDataException>(() => world.StartScenario(deleted.World, deleted));
        Assert.Equal(initial, Json(world.CaptureCheckpoint()));
        var added = project.Clone().World.SoilTypes[0]; added.Id = "new-soil"; added.Name = "Új talaj";
        project.World.SoilTypes.Add(added); project.World.SoilPaint[42] = added.Id;
        world.StartScenario(project.World, project);
        Assert.Equal(added.Id, world.Soils.Profile(42).Id);
        using var reopened = WorldLoader.CreateProject(project);
        Assert.Equal(Json(reopened.CaptureCheckpoint().Terrain), Json(world.CaptureCheckpoint().Terrain));
    }

    [Fact]
    public void PaintingTheExistingTypePreservesSimulationAndStoresTheAuthoredOverride()
    {
        var project = Project();
        using var world = WorldLoader.CreateProject(project);
        var initial = world.CaptureCheckpoint();
        project.World.SoilPaint[42] = world.Soils.Profile(42).Id;
        world.StartScenario(project.World, project);
        var after = world.CaptureCheckpoint();
        Assert.Equal(Json(initial with { Scene = null }), Json(after with { Scene = null }));
        var expectedScene = initial.Scene!.CloneContent(); expectedScene.SoilPaint[42] = world.Soils.Profile(42).Id;
        Assert.Equal(Json(expectedScene), Json(after.Scene));
    }

    [Fact]
    public void SoilEditWaterLedgerStillRejectsInventedWater()
    {
        var ecosystem = new Ecosystem(new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42)));
        var state = ecosystem.Capture();
        var invented = state.Environment with {
            InitialWater = state.Environment.InitialWater + 10,
            InitialWaterBeforeSoilEdit = state.Environment.InitialWater + 10
        };
        Assert.Throws<InvalidDataException>(() => ecosystem.Restore(state with { Environment = invented }));
    }
}
