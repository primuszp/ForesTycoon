using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class SceneStructureTests
{
    /// <summary>A road across a generated map, then a mill and a depot on dry 2×2 spots beside it.</summary>
    private static RuleProject Scene(out int depot, out int mill)
    {
        var project = GameWorld.DefaultRuleProject(); project.Algorithms = null;
        project.World = GameWorld.DefaultScenario(7, 32);
        using var probe = WorldLoader.CreateProject(project);
        int rows = probe.TileGrid.Rows;
        // The longest dry run along one column becomes the road.
        var (column, start, end) = Enumerable.Range(2, 28).SelectMany(c => Runs(probe, c, rows)).OrderByDescending(r => r.End - r.Start).First();
        project.World.Roads.Add(new RoadSegment { From = column * rows + start, To = column * rows + end, Surface = "Macadam" });
        using var withRoad = WorldLoader.CreateProject(project);
        var spots = Enumerable.Range(0, rows * rows).Where(t => withRoad.Map.TryGetSawmillFootprint(t, out var f, out _)
            && f.All(x => !withRoad.Map.IsRoadTile(x)) && withRoad.Map.FindRoadDocks(f).Count > 0).ToList();
        Assert.NotEmpty(spots);
        depot = spots[0];
        withRoad.Map.TryGetSawmillFootprint(depot, out var taken, out _);
        // A mill needs no road dock to be placed; any free dry spot away from the depot will do.
        mill = Enumerable.Range(0, rows * rows).First(t => withRoad.Map.TryGetSawmillFootprint(t, out var f, out _)
            && f.All(x => !withRoad.Map.IsRoadTile(x)) && !f.Intersect(taken).Any());
        project.World.Depots.Add(depot); project.World.Sawmills.Add(mill);
        return project;
    }

    private static IEnumerable<(int Column, int Start, int End)> Runs(GameWorld world, int column, int rows)
    {
        int start = -1;
        for (int r = 0; r <= rows; r++) {
            bool dry = r < rows && !world.IsWaterTile(column * rows + r);
            if (dry && start < 0) start = r;
            if (!dry && start >= 0) { if (r - 1 - start >= 6) yield return (column, start, Math.Min(r - 1, start + 12)); start = -1; }
        }
    }

    [Fact]
    public void AuthoredOrderFinishesAtSaveBoundaryAndResumesWithExactSlicedSimulation()
    {
        var project = Scene(out _, out _);
        for (int tile = 0; tile < 32 * 32; tile++) project.World.ForestPaint[tile] = "";
        using var world = WorldLoader.CreateProject(project);
        var machine = world.Logistics.Machines.Single(m => m.Kind == ForestMachineKind.Forwarder);
        int[] stacks = Enumerable.Range(0, world.Map.Tiles.Count).Where(t => world.Logistics.CanPlaceStack(t)
            && world.Logistics.PreviewMachinePath(machine.Tile, t, machine) != null).Take(2).ToArray();
        Assert.Equal(2, stacks.Length);
        foreach (int tile in stacks) world.QueueStackSite(tile, false);
        world.ExecutePendingCommands();
        world.QueueSendVehicle(machine.Id, stacks[0], stacks[1], false);
        world.ContinueCommands(1);
        using var save = new MemoryStream(); world.AtTickBoundary(() => world.Save(save));
        while (world.CommandPending) world.ContinueCommands(1);
        Assert.NotNull(machine.Source); Assert.Equal(stacks[1], machine.Destination);
        save.Position = 0;
        using var restored = WorldLoader.CreateProject(project); restored.Load(save);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        for (int tick = 0; tick < 30; tick++) {
            world.Update(1d / 30); while (!restored.ContinueUpdate(1d / 30, 3)) { }
        }
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }

    [Fact]
    public void AuthoredDepotFleetAppliesDuringGenerationAndPendingCommandSurvivesSave()
    {
        var project = Scene(out int depot, out _);
        foreach (var graph in project.Behaviors.Graphs.Where(g => g.Kind == "logisticsDepot")) {
            graph.Disabled = true;
            graph.DisabledValue = graph.Hook switch { "depot.allow" => 1, "depot.processors" => 4, "depot.forwarders" => 2, _ => 3 };
        }
        using var world = WorldLoader.CreateProject(project);
        Assert.Empty(world.SceneProblems);
        Assert.Equal(6, world.Logistics.Machines.Count); Assert.Equal(3, world.Logistics.Trucks.Count);
        int next = Enumerable.Range(0, world.Map.Tiles.Count).First(t => world.Map.TryGetSawmillFootprint(t, out _, out _));
        world.QueuePlaceDepot(next);
        world.ContinueCommands(1); // Save must finish this begun construction at its command boundary.
        using var save = new MemoryStream(); world.AtTickBoundary(() => world.Save(save));
        while (world.CommandPending) world.ContinueCommands(1);
        save.Position = 0;
        using var restored = WorldLoader.CreateProject(project); restored.Load(save);
        world.ExecutePendingCommands(); do { restored.ContinueCommands(1); } while (restored.CommandPending);
        Assert.Equal(2, world.Logistics.Depots.Count); Assert.Equal(12, world.Logistics.Machines.Count);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }

    [Fact]
    public void SceneBuildsRoadsMillAndDepotWithStartingFleetForFree()
    {
        var project = Scene(out int depot, out int mill);
        using var world = WorldLoader.CreateProject(RuleProject.FromJson(project.ToJson()));
        Assert.Empty(world.SceneProblems);
        Assert.True(world.RoadCount > 0);
        Assert.Single(world.Logistics.Depots); Assert.Equal(depot, world.Logistics.Depots[0].TileId);
        Assert.Contains(world.Logistics.Mills, m => m.TileId == mill);
        Assert.Equal(2, world.Logistics.Machines.Count); Assert.Single(world.Logistics.Trucks);
        Assert.Equal(0, world.Expenses);
    }

    [Fact]
    public void SceneStructuresLoadDeterministicallyAndReportBadPlacements()
    {
        var project = Scene(out _, out _);
        using var a = WorldLoader.CreateProject(project);
        using var b = WorldLoader.CreateProject(project);
        for (int i = 0; i < 30; i++) { a.Update(1d / 30); b.Update(1d / 30); }
        Assert.Equal(JsonSerializer.Serialize(a.CaptureCheckpoint()), JsonSerializer.Serialize(b.CaptureCheckpoint()));
        project.World.Depots.Add(project.World.Depots[0]);
        using var twice = WorldLoader.CreateProject(project);
        Assert.Single(twice.SceneProblems);
        Assert.Single(twice.Logistics.Depots);
        project.World.Roads.Add(new RoadSegment { From = 0, To = 5000, Surface = "Asphalt" });
        Assert.Throws<InvalidDataException>(project.World.Validate);
    }
}
