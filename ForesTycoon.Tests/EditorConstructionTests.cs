using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EditorConstructionTests
{
    private static SimulationWorld EmptyWorld()
    {
        var world = new SimulationWorld(new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4));
        world.Forest.Clear(); return world;
    }
    private static void Trees(SimulationWorld world, int tile, int count = 2) => world.Forest.IndividualTrees.Create(tile,
        new ForestStand(ForestSpecies.Oak, 60, .7f, 1), world.Forest.ForestYear, 42, authoredCount: count);

    [Theory]
    [InlineData((byte)RoadPaving.Dirt, "DirtBuild")]
    [InlineData((byte)RoadPaving.Macadam, "MacadamBuild")]
    [InlineData((byte)RoadPaving.Asphalt, "AsphaltBuild")]
    public void RoadRecoversAllTimberChargesEditedPriceAndRepeatingIsFree(byte pavingValue, string priceKey)
    {
        var paving = (RoadPaving)pavingValue;
        using var world = FoundationPersistenceTests.FlatWorld(); world.Forest.Clear();
        var project = world.RuleProjectDocument; project.Tuning[priceKey] = 123;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        foreach (int tile in new[] { 34, 50, 66, 35, 33 }) Trees(world, tile);
        double before = Enumerable.Range(0, 256).Sum(t => (double)world.Forest.AvailableTimber(t));
        world.QueueRoadPath(34, 66, false, paving); world.ExecutePendingCommands();
        Assert.Equal(3, world.RoadCount); Assert.Equal(369, world.Expenses);
        Assert.All(new[] { 34, 50, 66 }, t => { Assert.Equal(paving, world.Map.GetRoadPaving(t)); Assert.False(world.Forest.HasPhysicalObjects(t)); });
        var stack = Assert.Single(world.Logistics.Stacks); Assert.True(stack.Volume > 0);
        Assert.False(world.Map.IsNetworkTile(stack.Tile)); Assert.False(world.Forest.HasPhysicalObjects(stack.Tile));
        double after = Enumerable.Range(0, 256).Sum(t => (double)world.Forest.AvailableTimber(t)) + stack.Volume;
        Assert.Equal(before, after, 3); Assert.Equal(0, world.Logistics.Income);
        world.QueueRoadPath(34, 66, false, paving); world.ExecutePendingCommands();
        Assert.Equal(369, world.Expenses); Assert.Equal(stack.Volume, world.Logistics.Stacks.Sum(s => s.Volume));
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = FoundationPersistenceTests.FlatWorld(); restored.Load(save);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }

    [Fact]
    public void NoLandingSpaceRejectsConstructionWithoutDestroyingWoodOrCharging()
    {
        using var world = EmptyWorld(); Trees(world, 34);
        world.Map.GroundReserved = t => t is 18 or 33 or 35 or 50;
        float before = world.Forest.AvailableTimber(34);
        world.QueueRoadPath(34, 34, false, RoadPaving.Dirt); world.ExecutePendingCommands();
        Assert.Equal(0, world.RoadCount); Assert.Equal(0, world.Expenses);
        Assert.Equal(before, world.Forest.AvailableTimber(34)); Assert.Empty(world.Logistics.Stacks);
    }

    [Fact]
    public void RoadAlsoRecoversDeadTreesAndPreviouslyFelledDepot()
    {
        using var world = EmptyWorld(); Trees(world, 34, 3);
        Assert.True(world.Forest.IndividualTrees.TryGet(34, out var patch));
        var dead = patch.Trees[0]; world.Forest.IndividualTrees.RemoveLiving(patch, 0);
        patch.DeadTrees = new() { new(dead, world.Forest.ForestYear) }; patch.Depot = 7.5f;
        double before = world.Forest.AvailableTimber(34) + ForestTree.Volume(dead.Dimensions);
        world.QueueRoadPath(34, 34, false, RoadPaving.Dirt); world.ExecutePendingCommands();
        Assert.False(world.Forest.HasPhysicalObjects(34));
        Assert.Equal(before, world.Logistics.Stacks.Sum(s => (double)s.Volume), 4);
    }

    [Fact]
    public void ExplicitEditorFellingPreservesWoodOnSiteAndCannotBePlantedOver()
    {
        using var world = EmptyWorld(); Trees(world, 34, 3);
        float before = world.Forest.AvailableTimber(34);
        using var work = world.Forest.FellForEditorWork(new[] { 34 }).GetEnumerator();
        while (SimulationWork.MoveNext(work, 1)) Assert.Equal(1, work.Current);
        Assert.True(world.Forest.IndividualTrees.TryGet(34, out var patch));
        Assert.Equal(0, patch.Count); Assert.Equal(before, patch.Depot, 4); Assert.NotEmpty(patch.Stumps);
        world.QueuePlantForest(34, ForestSpecies.Oak); world.ExecutePendingCommands();
        Assert.Equal(0, patch.Count); Assert.Equal(before, patch.Depot, 4);
        var scene = SimulationWorld.DefaultScenario(42, 16); scene.ForestFelling.Add(34);
        var project = RuleProject.CreateDefault(); project.World = scene;
        Assert.Equal(new[] { 34 }, RuleProject.FromJson(project.ToJson()).World.ForestFelling);
        Assert.Empty(SimulationLoader.CaptureScenario(world).ForestFelling);
    }

    [Fact]
    public void SceneFellingRestoresCapturedTreesAsVisibleTimberAndRecaptureDoesNotRepeatIt()
    {
        using var world = FoundationPersistenceTests.FlatWorld(); world.Forest.Clear(); Trees(world, 34, 3);
        float before = world.Forest.AvailableTimber(34);
        var scene = SimulationLoader.CaptureScenario(world); scene.ForestFelling.Add(34);
        world.StartScenario(scene);
        Assert.True(world.Forest.IndividualTrees.TryGet(34, out var patch));
        Assert.Equal(0, patch.Count); Assert.Equal(before, patch.Depot, 4);
        Assert.True(world.DescribeTile(34).ContainsKey("forest.groundTimber"));
        var captured = SimulationLoader.CaptureScenario(world); Assert.Empty(captured.ForestFelling);
        world.StartScenario(captured);
        Assert.Equal(before, world.Forest.AvailableTimber(34), 4);
    }

    [Fact]
    public void DirtWeatherAndTrafficRulesApplyAndOldProjectsGainTheirDefaultsOnlyOnce()
    {
        var project = RuleProject.CreateDefault(); project.Version = 5;
        foreach (string key in new[] { "DirtBuild", "DirtWeather", "DirtRainWeather", "AsphaltBufferMetres", "MacadamBufferMetres", "DirtBufferMetres", "TrailBufferMetres" }) {
            project.Tuning.Remove(key); project.Behaviors.Graphs.RemoveAll(g => g.Hook == "tuning." + key);
        }
        RuleProjectMigrations.UpgradeProject(project); string upgraded = project.ToJson();
        RuleProjectMigrations.UpgradeProject(project); Assert.Equal(upgraded, project.ToJson());
        Assert.Equal(6, project.Version); Assert.Equal(70, project.Tuning["DirtBuild"]);
        using var world = EmptyWorld();
        world.Map.BuildRoadTilePath(34, 66, RoadPaving.Dirt);
        world.RuleBook.Run(world, 1, 0);
        Assert.Null(world.RuleBook.LastError); Assert.True(world.Map.GetRoadCondition(34) < 1);
        Assert.Equal(RoadSurface.Dirt, world.DrivingSurface(34).Surface);
    }

    [Fact]
    public void GroundObjectsBlockRoadBuildingsPlantingAndTerrainChanges()
    {
        using var world = EmptyWorld(); world.Map.BuildRoadTilePath(34, 210);
        Assert.True(world.Logistics.PlaceStack(195)); world.Logistics.StackAt(195).Add(10, 100);
        int[] corners = world.Map.TileCorners(195).ToArray();
        var heights = corners.Select(world.Map.NodeHeight).ToArray();
        world.QueueRoadPath(195, 195, false, RoadPaving.Dirt);
        world.QueuePlaceSawmill(195); world.QueuePlaceDepot(195);
        world.QueuePlantForest(195, ForestSpecies.Oak);
        world.QueueElevationEdit(corners[0], 1, 0, 1); world.ExecutePendingCommands();
        Assert.False(world.Map.IsRoadTile(195)); Assert.False(world.Forest.HasPhysicalObjects(195));
        Assert.Empty(world.Logistics.Mills); Assert.Empty(world.Logistics.Depots);
        Assert.Equal(heights, corners.Select(world.Map.NodeHeight)); Assert.Equal(10, world.Logistics.StackAt(195).Volume);
        Assert.False(world.Map.CanEditNode(corners[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildingsCannotReplaceForestEvenWhenAuthoredPlacementAllowsIt(bool depot)
    {
        using var world = EmptyWorld(); world.Map.BuildRoadTilePath(34, 210);
        Trees(world, 195); float volume = world.Forest.AvailableTimber(195);
        Assert.False(depot ? world.Logistics.PlaceDepot(195) : world.Logistics.PlaceMill(195));
        Assert.Equal(volume, world.Forest.AvailableTimber(195)); Assert.False(world.Logistics.PlaceStack(195));
    }

    [Fact]
    public void TerrainChangesBesideTreesAndTrailsAreRejectedAndPlantingCannotCoverTrail()
    {
        using var world = EmptyWorld(); Trees(world, 195);
        int corner = world.Map.TileCorners(195).First(), height = world.Map.NodeHeight(corner);
        world.QueueElevationEdit(corner, 1, 0, 1); world.ExecutePendingCommands();
        Assert.Equal(height, world.Map.NodeHeight(corner)); Assert.True(world.Forest.HasPhysicalObjects(195));
        world.Map.BuildRoadTilePath(34, 66);
        world.QueueSkidTrailPath(66, 114, false); world.ExecutePendingCommands();
        Assert.True(world.Map.IsSkidTrail(98));
        world.QueuePlantForest(98, ForestSpecies.Oak); world.ExecutePendingCommands();
        Assert.False(world.Forest.HasPhysicalObjects(98));
        corner = world.Map.TileCorners(98).First(); height = world.Map.NodeHeight(corner);
        world.QueueElevationEdit(corner, 1, 0, 1); world.ExecutePendingCommands();
        Assert.Equal(height, world.Map.NodeHeight(corner));
    }

    [Fact]
    public void AllRoadTypesAndTrailsFormOneNetworkAndIsolatedTrailIsRejected()
    {
        using var world = EmptyWorld();
        world.QueueSkidTrailPath(178, 210, false); world.ExecutePendingCommands();
        Assert.Equal(0, world.Map.SkidTrailCount); Assert.Equal(0, world.Expenses);
        world.QueueRoadPath(34, 66, false, RoadPaving.Asphalt);
        world.QueueRoadPath(82, 114, false, RoadPaving.Macadam);
        world.QueueRoadPath(130, 162, false, RoadPaving.Dirt);
        world.QueueSkidTrailPath(178, 210, false); world.ExecutePendingCommands();
        Assert.Equal(210, world.Map.FindNetworkPath(34, 210)[^1]);
        Assert.Equal(34, world.Map.FindNetworkPath(210, 34)[^1]);
    }

    [Fact]
    public void BuffersUseMetresEditedRadiiAndCountOverlapsOnlyOnce()
    {
        using var world = EmptyWorld();
        world.Map.BuildRoadTilePath(34, 66, RoadPaving.Dirt); Trees(world, 51); Trees(world, 255);
        var tuning = world.RuleProjectDocument; tuning.Tuning["DirtBufferMetres"] = 50;
        world.QueueRuleProject(tuning); world.ExecutePendingCommands();
        var result = new WorkResult<RoadTimberSurvey>();
        {
            using var work = world.RoadTimberSurveyWork(result).GetEnumerator();
            while (SimulationWork.MoveNext(work, 1)) Assert.InRange(work.Current, 1, 1);
        }
        double available = world.Forest.AvailableTimber(51);
        Assert.True(available > 0); Assert.Equal(available, result.Value.TotalTimber, 4);
        Assert.True(result.Value.TimberByRoad.Values.Sum() > result.Value.TotalTimber);
        Assert.NotEqual(0u, result.Value.Overlay[51]); Assert.Equal(0u, result.Value.Overlay[255]);
        tuning.Tuning["DirtBufferMetres"] = 0; world.QueueRuleProject(tuning); world.ExecutePendingCommands();
        SimulationWork.Complete(world.RoadTimberSurveyWork(result)); Assert.Equal(0, result.Value.TotalTimber);
    }

    [Fact]
    public void ChangingSurfaceInvalidatesNavigationAndUsesTheNewBufferRadius()
    {
        using var world = EmptyWorld(); world.Map.BuildRoadTilePath(34, 66, RoadPaving.Dirt);
        var project = world.RuleProjectDocument; project.Tuning["DirtBufferMetres"] = 5; project.Tuning["AsphaltBufferMetres"] = 100;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        ulong before = world.Map.NavigationVersion;
        Assert.Equal(5, world.RoadBufferMetres(50));
        world.QueueRoadPath(34, 66, false, RoadPaving.Asphalt); world.ExecutePendingCommands();
        Assert.True(world.Map.NavigationVersion > before); Assert.Equal(100, world.RoadBufferMetres(50));
    }

    [Fact]
    public void EditorTerrainPreflightRejectsAnObjectReachedOnlyByTheSlopeCascade()
    {
        using var world = EmptyWorld(); Trees(world, 179);
        int node = world.Map.GetNode(10, 3).Id;
        Assert.True(world.Map.CanEditNode(node));
        var before = world.Map.Capture().Heights;
        Assert.False(world.Map.CanApplyHeightTargets(new Dictionary<int, int> { [node] = 6 }));
        Assert.True(world.Map.CanApplyHeightTargets(new Dictionary<int, int> { [node] = world.Map.NodeHeight(node) }));
        Assert.Equal(before, world.Map.Capture().Heights);
    }
}
