using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class MountainSceneTests
{
    [Fact]
    public void MountainEstateStartsWithHabitatsConnectedRoadsAndAWorkingHaulageRoute()
    {
        var project = RuleProject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Content", "Scenes", "kettos-gerinc.project.json")));
        using var world = SimulationLoader.CreateProject(project);
        Assert.Empty(world.SceneProblems);
        Assert.Equal((64, 64), world.TileGrid);
        Assert.Equal(16, project.World.HeightPaint.Keys.Max(world.Map.NodeHeight));
        Assert.Equal(6, Enumerable.Range(0, world.Map.Tiles.Count).Select(world.TileBiomeIndex).Distinct().Count());
        Assert.Equal(0, world.Expenses);

        ForestSpecies[] expected = { ForestSpecies.Ash, ForestSpecies.Ash, ForestSpecies.Hazel,
            ForestSpecies.SessileOak, ForestSpecies.Beech, ForestSpecies.Juniper };
        for (int biome = 0; biome < expected.Length; biome++) {
            var trees = Enumerable.Range(0, world.Map.Tiles.Count).Where(t => world.TileBiomeIndex(t) == biome)
                .Select(world.TileSpecies).Where(s => s != ForestSpecies.None).ToArray();
            Assert.True(trees.Distinct().Count() >= 2, $"Biome {biome} needs a mix of species.");
            Assert.Equal(expected[biome], trees.GroupBy(s => s).MaxBy(g => g.Count())!.Key);
        }

        var depot = Assert.Single(world.Logistics.Depots);
        var mill = Assert.Single(world.Logistics.Mills);
        var truck = Assert.Single(world.Logistics.Trucks);
        Assert.Equal(2, world.Logistics.Machines.Count);
        int origin = world.Map.FindRoadDocks(depot.Footprint)[0];
        int[] reachable = Enumerable.Range(0, world.Map.Tiles.Count).Where(world.Map.IsNetworkTile).ToArray();
        Assert.Contains(reachable, world.Map.IsSkidTrail);
        Assert.All(reachable, tile => Assert.NotEmpty(world.Map.FindNetworkPath(origin, tile)));
        Assert.Equal(3, reachable.Where(world.Map.IsRoadTile).Select(world.Map.GetRoadPaving).Distinct().Count());

        int stackTile = Enumerable.Range(16 * world.TileGrid.Rows, 13 * world.TileGrid.Rows)
            .Where(world.Logistics.CanPlaceStack)
            .First(tile => world.Logistics.PreviewTruckRoute(tile, mill.TileId, truck) is { Length: >= 8 });
        world.QueueStackSite(stackTile, false);
        world.ExecutePendingCommands();
        var stack = world.Logistics.StackAt(stackTile);
        Assert.NotNull(stack);
        stack.Add(8, 240);
        var route = world.Logistics.PreviewTruckRoute(stackTile, mill.TileId, truck);
        Assert.True(route.Length > 2);
        world.QueueSendVehicle(truck.Id, stackTile, mill.TileId, true);
        world.QueueWeather(WeatherPreset.Rain, 30, 120);
        world.ExecutePendingCommands();
        Assert.True(truck.Source == stack, world.Logistics.Status);

        for (int i = 0; i < 30; i++) world.Update(1d / 30);
        using var save = new MemoryStream();
        world.Save(save);
        save.Position = 0;
        using var resumed = new SimulationWorld(TerrainSettings.Default.WithNodeSize(17, 42));
        resumed.Load(save);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(resumed.CaptureCheckpoint()));
        for (int i = 0; i < 30; i++) {
            world.Update(1d / 30);
            while (!resumed.ContinueUpdate(1d / 30, 32)) { }
        }
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(resumed.CaptureCheckpoint()));

        for (int i = 0; i < 45 * 30 && mill.Received < 7.999; i++) world.Update(1d / 30);
        Assert.Equal(8, mill.Received, 3);
        Assert.Equal(0, stack.Volume, 3);
        Assert.True(world.GetRoadCondition(route[1]) < 1);
        Assert.True(world.RuleBook.FlowTotals.GetValueOrDefault("forrásvíz") > 0);
        Assert.InRange(Math.Abs(world.Environment.BalanceError), 0, 1e-6 * world.Environment.StoredWater);
        Assert.Null(world.RuleBook.LastError);
    }
}
