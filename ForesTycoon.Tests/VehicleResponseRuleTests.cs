using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class VehicleResponseRuleTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Fixed(BehaviorModel model, string hook, double value)
    {
        var graph = model.Graphs.Single(g => g.Kind == "vehicleResponse" && g.Hook == hook);
        graph.Disabled = true; graph.DisabledValue = value;
    }
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
    [Fact]
    public void EveryDecisionHasADiscoveredObjectFieldAndAnExplicitGraph()
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        Assert.Equal(Enum.GetValues<VehicleResponse>().Length, VehicleResponseRules.Hooks.Length);
        foreach (var hook in VehicleResponseRules.Hooks) {
            var graph = model.Graphs.Single(g => g.Hook == hook.Id && g.Kind == hook.Kind);
            Assert.DoesNotContain(graph.Nodes, n => n.Operation == BehaviorOperation.Native);
            Assert.NotEmpty(ResponseRuleDescription.For(hook.Id).Consumers);
            foreach (string input in hook.Inputs) {
                var parts = input.Split('.');
                var relation = GameWorld.RuleObjects.Find(hook.Kind).Relations.Single(r => r.Name == parts[0]);
                Assert.NotEmpty(GameWorld.RuleObjects.Find(relation.Target).Fields.Single(f => f.Name == parts[1]).Display);
            }
        }
    }
    [Fact]
    public void AuthoredCargoCurveControlsActualMassAndFuelGraphControlsTheIntegrator()
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        var graph = model.Graphs.Single(g => g.Hook == "vehicle.mass");
        graph.Nodes = new() {
            new() { Id = "cargo", Name = "Rakomány", Operation = BehaviorOperation.Input, Key = "vehicle.cargo" },
            new() { Id = "curve", Name = "Rakomány tömeggörbéje", Operation = BehaviorOperation.Curve, A = "cargo",
                Key = new RuleCurve { Input = BehaviorCurves.Input, Points = new() { (0, 10000), (20, 30000) } }.ToFormula() }
        }; graph.Output = "curve";
        Fixed(model, "vehicle.fuel", .75); Fixed(model, "vehicle.force", 0); Fixed(model, "vehicle.resistance", 0);
        var spec = TruckSpec.Default with { Rules = new VehicleRuleProgram(model) };
        Assert.Equal(10000, VehicleDynamics.Mass(spec, 0)); Assert.Equal(20000, VehicleDynamics.Mass(spec, 10));
        var result = VehicleDynamics.Step(spec, 20000, 0, 10, 0, RoadSurface.Asphalt, 0, .1f);
        Assert.Equal(0, result.Speed); Assert.Equal(.75f, result.Fuel);
    }
    [Fact]
    public void AuthoredUpkeepControlsWearBreakdownRepairServiceAndTheirCosts()
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        foreach (var (hook, value) in new[] { ("upkeep.wear", .01), ("upkeep.breakdown", 1d), ("upkeep.repairSeconds", 3d),
            ("upkeep.repairRelief", .05), ("upkeep.repairCost", 127d), ("upkeep.serviceRelief", .02),
            ("upkeep.serviceCost", 9d), ("upkeep.fuelFactor", 2d), ("upkeep.paceFactor", .5) }) Fixed(model, hook, value);
        var tuning = GameTuning.Default.WithBehaviors(null, new VehicleRuleProgram(model));
        var upkeep = new VehicleUpkeep(42) { Wear = .1f };
        Assert.False(upkeep.Operate(1, 1, out _, tuning)); Assert.True(upkeep.Broken); Assert.Equal(3, upkeep.RepairLeft);
        Assert.Equal(.11f, upkeep.Wear, 6); Assert.True(upkeep.Operate(3, 1, out double cost, tuning));
        Assert.Equal(127, cost); Assert.Equal(.06f, upkeep.Wear, 6);
        Assert.Equal(9, upkeep.Service(1, tuning)); Assert.Equal(.04f, upkeep.Wear, 6);
        Assert.Equal(2, upkeep.Fuel(tuning)); Assert.Equal(.5f, upkeep.Pace(tuning));
    }
    [Fact]
    public void DefaultProgramsDoNotShareMutableScratchAcrossParallelCallers()
    {
        Parallel.For(0, 32, workerIndex => {
            var upkeep = new VehicleUpkeep(3);
            for (int i = 0; i < 100; i++) Assert.True(upkeep.Operate(1, 1, out _));
            Assert.Equal(.025f, upkeep.Wear, 5);
            Assert.Equal(32000, VehicleDynamics.Mass(GameTuning.Default.Truck, 20));
        });
    }
    [Fact]
    public void ExistingAndRestoredVehiclesReadTheCurrentWorldProgramWithoutAFleetScan()
    {
        using var world = World();
        var scenario = GameWorld.DefaultScenario(42, 16);
        for (int id = 0; id < 17 * 17; id++) scenario.HeightPaint[id] = 4;
        world.StartScenario(scenario);
        int start = Enumerable.Range(0, world.Map.Tiles.Count - 3).First(t => t / world.TileGrid.Rows == (t + 2) / world.TileGrid.Rows
            && Enumerable.Range(t, 3).All(world.Map.CanCarrySkidTrail));
        world.QueueRoadPath(start, start + 2, false, RoadPaving.Macadam); world.ExecutePendingCommands();
        int[] route = world.Map.FindNetworkPath(start, start + 2); Assert.True(route.Length >= 2);
        var truck = world.Logistics.Vehicles.SpawnTransit(route);
        var project = world.RuleProjectDocument; Fixed(project.Behaviors, "vehicle.mass", 23456); Fixed(project.Behaviors, "vehicle.fuel", .01);
        world.QueueRuleProject(project); world.ExecutePendingCommands(); Assert.Equal(23456, truck.Mass);
        world.Update(1d / 30); Assert.True(truck.FuelUsed > 0);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save); Assert.Equal(State(world), State(restored));
        Assert.Equal(23456, restored.Logistics.Vehicles.Vehicles.Single().Mass);
        for (int tick = 0; tick < 90; tick++) {
            world.Update(1d / 30); while (!restored.ContinueUpdate(1d / 30, 3)) { }
        }
        Assert.Equal(State(world), State(restored));
    }
    [Fact]
    public void PendingProjectSaveAndJournalReplayRetainAllVehicleGraphsAndDoNotRefreshTheForest()
    {
        using var world = World(); var project = world.RuleProjectDocument;
        Fixed(project.Behaviors, "upkeep.breakdown", 0); Fixed(project.Behaviors, "vehicle.mass", 19000);
        world.Update(1d / 30); // Finish the initial forest-rate refresh.
        world.QueueRuleProject(project);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        world.ExecutePendingCommands(); do { restored.ContinueCommands(1); } while (restored.CommandPending);
        Assert.Equal(State(world), State(restored));
        Assert.False(world.CaptureCheckpoint().PendingForestRateRefresh);
        save.Position = 0; var data = WorldSaveSerializer.Read(save);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
            Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate, Tick = data.Tick,
            TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; restored.Load(journal); restored.ExecutePendingCommands(); Assert.Equal(State(world), State(restored));
    }
}
