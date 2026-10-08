using System.Text.Json;

namespace ForesTycoon.Tests;

public class EcologyCheckpointTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42).WithForestPattern(ForestPattern.LargeMixed);
    private static string Json<T>(T state) => JsonSerializer.Serialize(state);

    [Theory]
    [InlineData(120, 9.733333)]
    [InlineData(1200, 99.966667)]
    [InlineData(120, 121.13333)]
    public void CheckpointContinuesExactlyAcrossMonthsRandomWeatherEditsAndRegeneration(double year, double seconds)
    {
        var map = new TerrainMap(Settings); var world = new Ecosystem(map, year, SoilLandscapeDefinition.Default);
        world.Environment.ForceWeather(WeatherPreset.Storm, 32, 20); world.Update(seconds);
        // Create real dictionary holes before capturing, then exercise insertion after restoration.
        var occupied = world.Forest.IndividualTrees.Patches.Select(p => p.Key).Take(3).ToArray();
        world.Forest.ClearTerrainTiles(occupied);
        var checkpoint = JsonSerializer.Deserialize<EcologyCheckpoint>(Json(world.Capture()))!;
        var terrain = map.Capture();
        var cloneMap = new TerrainMap(Settings); var clone = new Ecosystem(cloneMap, year, SoilLandscapeDefinition.Default);
        cloneMap.Restore(terrain); clone.Restore(checkpoint);
        Assert.Equal(Json(world.Capture()), Json(clone.Capture()));
        foreach (int id in occupied) {
            Assert.Equal(world.Forest.Plant(id, ForestSpecies.Oak), clone.Forest.Plant(id, ForestSpecies.Oak));
        }
        for (int i = 0; i < 1000; i++) { world.Update(.133333333); clone.Update(.133333333); }
        Assert.Equal(Json(world.Capture()), Json(clone.Capture()));
        foreach (int id in occupied) {
            Assert.Equal(world.Forest.Harvest(id, out _), clone.Forest.Harvest(id, out _));
        }
        world.Update(350); clone.Update(350);
        Assert.Equal(Json(world.Capture()), Json(clone.Capture()));
    }

    [Fact]
    public void MidMonthCheckpointPreservesCachedCanopyWaterDemand()
    {
        var map = new TerrainMap(Settings);
        var original = new Ecosystem(map, 120, SoilLandscapeDefinition.Default);
        original.Update(30.5);
        var cloneMap = new TerrainMap(Settings);
        var clone = new Ecosystem(cloneMap, 120, SoilLandscapeDefinition.Default);
        cloneMap.Restore(map.Capture());
        clone.Restore(JsonSerializer.Deserialize<EcologyCheckpoint>(Json(original.Capture()))!);
        for (int i = 0; i < 4000; i++) { original.Update(1.0 / 30); clone.Update(1.0 / 30); }
        Assert.Equal(Json(original.Capture()), Json(clone.Capture()));
    }

    [Fact]
    public void CapturedArraysAreOwnedAndMalformedStocksClocksIdsAndModelsAreRejected()
    {
        var map = new TerrainMap(Settings); var world = new Ecosystem(map, soils: SoilLandscapeDefinition.Default);
        world.Update(1.1); var s = world.Capture(); string before = Json(world.Capture());
        s.Environment.Fields[0][0] = -1;
        Assert.Equal(before, Json(world.Capture()));
        Assert.Throws<InvalidDataException>(() => new Ecosystem(new TerrainMap(Settings), soils: SoilLandscapeDefinition.Default).Restore(s));
        s = world.Capture();
        Assert.Throws<InvalidDataException>(() => world.Restore(s with { Clock = s.Clock with { PendingSeconds = .5 } }));
        s = world.Capture(); var patch = s.Forest.Trees.Patches.First(p => p.Trees.Length > 0);
        patch.Trees[0] = patch.Trees[0] with { Id = s.Forest.Trees.NextId };
        Assert.Throws<InvalidDataException>(() => new Ecosystem(new TerrainMap(Settings), soils: SoilLandscapeDefinition.Default).Restore(s));
        Assert.Throws<InvalidDataException>(() => new Ecosystem(new TerrainMap(Settings), soils: SoilLandscapeDefinition.Default).Restore(
            world.Capture() with { SoilHash = "wrong" }));
    }

    [Fact]
    public void TerrainCheckpointPreservesFrozenRoadsAndDerivedSurfaces()
    {
        var map = new TerrainMap(Settings, (_, _) => 4);
        map.BuildRoadTilePath(34, 82);
        map.EditElevationAtNode(map.GetNode(4, 4).Id, -1, 0, 1);
        var s = map.Capture(); var clone = new TerrainMap(Settings); clone.Restore(s);
        Assert.Equal(Json(s), Json(clone.Capture()));
        Assert.True(clone.SurfaceCacheMatchesFreshCalculation());
        foreach (int id in map.Roads.Tiles) {
            Assert.True(map.TryGetRoadSurface(id, out var p, out var g));
            Assert.True(clone.TryGetRoadSurface(id, out var actual, out var gradient));
            Assert.Equal(p, actual); Assert.Equal(g, gradient);
        }
    }

    [Fact]
    public void VehicleCheckpointPreservesMidTransferAndCapturedRoadPhysics()
    {
        var map = new TerrainMap(Settings, (_, _) => 4); map.BuildRoadTilePath(34, 82);
        var inventory = new TimberCargoSystem(); inventory.AddHarvested(50);
        var system = new VehicleSystem(inventory, route => VehicleRoadRoute.Create(map, route));
        system.Spawn(new[] { 34, 50, 66, 82 }); system.Update(.73);
        var state = JsonSerializer.Deserialize<VehiclesCheckpoint>(Json(system.Capture()))!;
        var otherInventory = new TimberCargoSystem(); otherInventory.Restore(inventory.Available, inventory.Delivered);
        var other = new VehicleSystem(otherInventory, route => VehicleRoadRoute.Create(map, route)); other.Restore(state, map.Tiles.Count);
        for (int i = 0; i < 5000; i++) { system.Update(1.0 / 30); other.Update(1.0 / 30); }
        Assert.Equal(Json(system.Capture()), Json(other.Capture()));
        Assert.Equal(inventory.Available, otherInventory.Available); Assert.Equal(inventory.Delivered, otherInventory.Delivered);
    }

    [Fact]
    public void WildlifeAndForageResumeFromTheirOwnClocksAndSeeds()
    {
        var map = new TerrainMap(Settings); var forest = new ForestSystem(map);
        var original = new WildlifeSystem(); original.Update(1.33, map, forest, null);
        var clone = new WildlifeSystem(); clone.Restore(JsonSerializer.Deserialize<WildlifeCheckpoint>(Json(original.Capture()))!, map.Tiles.Count);
        for (int i = 0; i < 100; i++) { original.Update(.1333, map, forest, null); clone.Update(.1333, map, forest, null); }
        Assert.Equal(Json(original.Capture()), Json(clone.Capture()));
    }

    [Fact]
    public void BlockedTruckCanRestoreItsFrozenTrajectoryAfterRoadRemoval()
    {
        var map = new TerrainMap(Settings, (_, _) => 4); map.BuildRoadTilePath(34, 82);
        var system = new VehicleSystem(roadRouteFactory: route => VehicleRoadRoute.Create(map, route));
        var vehicle = system.SpawnLogistics(new[] { 34, 50, 66, 82 }, new[] { 33 }, 84);
        map.RemoveRoadTilePath(50, 50); system.RemoveInvalidRoutes(map.IsRoadTile);
        Assert.True(vehicle.RouteBlocked);
        var clone = new VehicleSystem(roadRouteFactory: route => VehicleRoadRoute.Create(map, route));
        clone.Restore(system.Capture(), map.Tiles.Count);
        Assert.Equal(Json(system.Capture()), Json(clone.Capture()));
        system.Update(10); clone.Update(10); Assert.Equal(Json(system.Capture()), Json(clone.Capture()));
    }

    [Fact]
    public void EffectCheckpointPreservesInterpolationBeforeTheNextTick()
    {
        var effects = new WorldEffectSystem(); effects.Spawn(WorldEffectKind.TreePlanted, new(2, 3, 4));
        effects.Update(.13); effects.Update(.07);
        var clone = new WorldEffectSystem(); clone.Restore(effects.Capture());
        Assert.Equal(effects.Active[0].Timeline.SampleProgress(.3f), clone.Active[0].Timeline.SampleProgress(.3f));
        effects.Update(.2); clone.Update(.2); Assert.Equal(Json(effects.Capture()), Json(clone.Capture()));
    }
}
