using System.Linq;
using System.Text.Json;

namespace ForesTycoon.Tests;

/// <summary>
/// The timber chain with the starting fleet: processor fells into a stack beside the skid trail, the forwarder carries
/// that stack to the roadside, the truck hauls the roadside stack to the mill, where the wood is sold.
/// </summary>
public partial class FleetTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static readonly int[] Stand = { 136, 137, 152, 153 };         // tiles (8..9, 8..9)
    private const int Depot = 67, Mill = 195, ForestStack = 151, RoadStack = 147;

    private sealed record Scene(TerrainMap Map, ForestSystem Forest, ForestryLogistics Logistics, VehicleSystem Vehicles);

    // Road along v = 2, a skid trail (8, 3) → (8, 7) to the stand, a mill at (12, 3), the depot at (4, 3).
    private static Scene Build(bool stacks = true, bool mixedRoads = false, RoadPaving firstSurface = RoadPaving.Asphalt)
    {
        var map = new TerrainMap(Settings, (_, _) => 4);
        var stands = new ForestStand[map.Tiles.Count];
        foreach (int id in Stand) stands[id] = new ForestStand(ForestSpecies.Oak, 60, 0.7f, 1);
        var forest = new ForestSystem(map, stands);
        if (mixedRoads)
        {
            // Separate strokes meet at adjacent endpoints, without repainting an overlap tile.
            map.BuildRoadTilePath(34, 82, firstSurface);
            map.BuildRoadTilePath(98, 130, firstSurface == RoadPaving.Asphalt ? RoadPaving.Macadam : RoadPaving.Asphalt);
            map.BuildRoadTilePath(146, 210, firstSurface);
        }
        else map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        map.MarkSkidTrailPath(130, 135);
        var logistics = new ForestryLogistics(map, forest) { MachinesEnabled = true };
        var vehicles = new VehicleSystem(new TimberCargoSystem(), route => VehicleRoadRoute.Create(map, route))
        { SourceLoader = logistics.Load, DestinationReceiver = logistics.Deliver, RouteValidator = logistics.RouteConnected };
        logistics.Vehicles = vehicles;
        Assert.True(logistics.PlaceMill(Mill), logistics.Status);
        Assert.True(logistics.PlaceDepot(Depot), logistics.Status);
        logistics.Designate(Stand);
        if (stacks)
        {
            Assert.True(logistics.PlaceStack(ForestStack), logistics.Status);
            Assert.True(logistics.PlaceStack(RoadStack), logistics.Status);
        }
        return new Scene(map, forest, logistics, vehicles);
    }

    private static void Run(Scene s, double seconds)
    {
        for (int i = 0; i < seconds * 30; i++) { s.Logistics.Update(1.0 / 30); s.Vehicles.Update(1.0 / 30); }
    }

    private static ForestMachine Processor(Scene s) => s.Logistics.Machines.Single(m => m.Kind == ForestMachineKind.Harvester);
    private static ForestMachine Forwarder(Scene s) => s.Logistics.Machines.Single(m => m.Kind == ForestMachineKind.Forwarder);

    [Fact]
    public void ControllerHomeActionFinishesHeldLogAndDeliversBeforeReturningToDepot()
    {
        var scene = Build(); var forwarder = Forwarder(scene);
        var source = scene.Logistics.StackAt(ForestStack); var destination = scene.Logistics.StackAt(RoadStack);
        source.Volume = 4; source.Value = 400;
        Assert.True(scene.Logistics.AssignForwarder(forwarder, source, RoadStack));
        for (int i = 0; i < 6000 && forwarder.LogTransferVolume == 0; i++) Run(scene, 1.0 / 30);
        Assert.True(forwarder.LogTransferVolume > 0);
        var controller = new ForesTycoon.Rules.BehaviorController { Id = "home", Kind = "forwarder" };
        controller.States[0].Action = ForesTycoon.Rules.BehaviorAction.ReturnHome;
        scene.Logistics.Controllers = new(new() { Controllers = new() { controller } });
        Run(scene, 240);
        Assert.False(forwarder.Working); Assert.Equal(Depot, forwarder.Tile); Assert.Equal(ForestMachineState.Parked, forwarder.State);
        Assert.True(source.Volume > 0); Assert.True(destination.Volume > 0);
        Assert.Equal(0, forwarder.LogTransferVolume); Assert.Equal(0, forwarder.Cargo);
        Assert.Equal(4, source.Volume + destination.Volume, 4); Assert.Equal(400, source.Value + destination.Value, 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessorReachesTheFellingOverSeparatelyBuiltMixedSurfaceRoads(bool macadamFirst)
    {
        var s = Build(mixedRoads: true, firstSurface: macadamFirst ? RoadPaving.Macadam : RoadPaving.Asphalt);
        var processor = Processor(s);
        Assert.True(s.Logistics.AssignProcessor(processor, s.Logistics.StackAt(ForestStack)!), s.Logistics.Status);
        Run(s, 60);
        Assert.True(s.Logistics.StackAt(ForestStack)!.Volume > 0, "The processor did not reach the felling and deliver timber.");
        Assert.True(processor.FuelUsed > 0);
        Assert.Equal(0, s.Logistics.StackAt(RoadStack)!.Volume);
    }

    [Fact]
    public void FirstDepotHoldsTheStartingFleetAndNothingWorksUntilSent()
    {
        var s = Build();
        Assert.Single(s.Logistics.Trucks);
        Run(s, 20);
        Assert.All(s.Logistics.Machines, m => Assert.Equal(Depot, m.Tile));
        Assert.Equal(TruckPhase.Parked, s.Logistics.Trucks[0].Phase);
        Assert.Empty(s.Vehicles.Vehicles);
    }

    [Fact]
    public void ProcessorCarriesToTheStackItWasSentTo()
    {
        var s = Build(stacks: false);
        Assert.Null(s.Logistics.StackAt(ForestStack));
        Assert.True(s.Logistics.PlaceStack(ForestStack)); Assert.True(s.Logistics.PlaceStack(RoadStack));
        Assert.False(s.Logistics.PlaceStack(Mill), "A stack on the mill was accepted.");
        Assert.True(s.Logistics.AssignProcessor(Processor(s), s.Logistics.StackAt(ForestStack)!), s.Logistics.Status);
        Assert.Same(s.Logistics.StackAt(ForestStack), Processor(s).Target);
        Run(s, 60);
        var stack = s.Logistics.StackAt(ForestStack);
        Assert.True(stack.Volume > 1, "The processor did not stack any wood.");
        Assert.Equal(55, stack.UnitValue, 3);                               // oak price per m³
        Assert.Equal(0, s.Logistics.StackAt(RoadStack).Volume);
        Assert.True(Processor(s).FuelUsed > 0 && s.Logistics.RunningCosts > 0);
    }

    [Fact]
    public void FartherStacksCostFuelAndOutput()
    {
        (float Volume, double Fuel) Fell(int stackTile)
        {
            var s = Build(stacks: false);
            s.Logistics.PlaceStack(stackTile);
            Assert.True(s.Logistics.AssignProcessor(Processor(s), s.Logistics.StackAt(stackTile)!), s.Logistics.Status);
            Run(s, 90);
            return (s.Logistics.StackAt(stackTile).Volume, Processor(s).FuelUsed);
        }
        var near = Fell(ForestStack);
        var far = Fell(RoadStack);
        Assert.True(far.Volume < near.Volume * 0.8f, $"far {far.Volume} vs near {near.Volume}");
        Assert.True(far.Fuel / far.Volume > near.Fuel / near.Volume * 1.2, "Carrying farther did not cost more fuel per m³.");
    }

    [Fact]
    public void TheChainBringsEveryCubicMetreToTheMillAndEveryoneHome()
    {
        var s = Build();
        float initial = s.Logistics.Volume(s.Logistics.Sites[0]);
        double value = initial * TimberPriceOak;
        Assert.True(s.Logistics.AssignProcessor(Processor(s), s.Logistics.StackAt(ForestStack)!), s.Logistics.Status);
        Assert.True(s.Logistics.AssignForwarder(Forwarder(s), s.Logistics.StackAt(ForestStack), RoadStack), s.Logistics.Status);
        Assert.True(s.Logistics.AssignTruck(s.Logistics.Trucks[0], s.Logistics.StackAt(RoadStack), Mill), s.Logistics.Status);
        Run(s, 2400);
        Assert.Equal(initial, s.Logistics.Mills[0].Received, 1);
        Assert.Equal(value, s.Logistics.Income, 0);
        Assert.True(s.Logistics.Remaining < 0.01f);
        Assert.All(s.Logistics.Machines, m => { Assert.False(m.Working); Assert.Equal(Depot, m.Tile); Assert.Equal(ForestMachineState.Parked, m.State); });
        Assert.Equal(TruckPhase.Parked, s.Logistics.Trucks[0].Phase);
        Assert.Empty(s.Vehicles.Vehicles);
        Assert.True(s.Map.GetSkidTrailWear(133) > 0.3f);
    }

    private const double TimberPriceOak = 55;

    [Fact]
    public void TruckFuelIsChargedAcrossApproachShuttleAndReturnVehicles()
    {
        var s = Build();
        s.Logistics.Tuning = GameTuning.FromOverrides(new Dictionary<string, double>
        { ["WearPerSecond"] = 0, ["BreakdownHazard"] = 0, ["WearFuelPenalty"] = 0 });
        var truck = s.Logistics.Trucks[0];
        var source = s.Logistics.StackAt(RoadStack)!; source.Add(20, 20 * 30);
        Assert.True(s.Logistics.AssignTruck(truck, source, Mill), s.Logistics.Status);
        var usedVehicles = new HashSet<Vehicle>();
        for (int i = 0; i < 30000 && truck.Phase != TruckPhase.Parked; i++)
        {
            if (truck.Vehicle != null) usedVehicles.Add(truck.Vehicle);
            s.Logistics.Update(1.0 / 30); s.Vehicles.Update(1.0 / 30);
        }
        Assert.Equal(TruckPhase.Parked, truck.Phase); Assert.Equal(3, usedVehicles.Count);
        Assert.All(usedVehicles, v => Assert.True(v.FuelUsed > 0));
        Assert.Equal(usedVehicles.Sum(v => v.FuelUsed) * s.Logistics.Tuning[Tune.DieselPrice], s.Logistics.RunningCosts, 8);
    }

    [Fact]
    public void ForwarderCanTakeAStackStraightToTheMill()
    {
        var s = Build();
        s.Logistics.StackAt(ForestStack)!.Add(20, 20 * 30);
        Assert.True(s.Logistics.AssignForwarder(Forwarder(s), s.Logistics.StackAt(ForestStack), Mill), s.Logistics.Status);
        Run(s, 800); // 32 physical logs need individual loading and unloading crane cycles.
        Assert.Equal(20, s.Logistics.Mills[0].Received, 2);
        Assert.Equal(600, s.Logistics.Income, 1);
        Assert.False(Forwarder(s).Working);
    }

    [Fact]
    public void CalledHomeTheProcessorPutsDownItsLoadAndLeaves()
    {
        var s = Build();
        var processor = Processor(s);
        s.Logistics.AssignProcessor(processor, s.Logistics.StackAt(ForestStack)!);
        Run(s, 40);
        s.Logistics.SendHome(processor);
        Run(s, 80);
        Assert.False(processor.Working); Assert.Equal(Depot, processor.Tile); Assert.Equal(0, processor.Cargo);
        Assert.True(s.Logistics.Volume(s.Logistics.Sites[0]) > 1, "It kept felling.");
    }

    [Fact]
    public void CheckpointKeepsStacksOrdersAndMoney()
    {
        var s = Build();
        s.Logistics.AssignProcessor(Processor(s), s.Logistics.StackAt(ForestStack)!);
        s.Logistics.AssignForwarder(Forwarder(s), s.Logistics.StackAt(ForestStack), RoadStack);
        s.Logistics.AssignTruck(s.Logistics.Trucks[0], s.Logistics.StackAt(RoadStack), Mill);
        Run(s, 200);
        var logistics = JsonSerializer.Deserialize<LogisticsCheckpoint>(JsonSerializer.Serialize(s.Logistics.Capture()))!;
        var vehicles = JsonSerializer.Deserialize<VehiclesCheckpoint>(JsonSerializer.Serialize(s.Vehicles.Capture()))!;
        var copy = Build(stacks: false);
        copy.Logistics.Restore(logistics); copy.Vehicles.Restore(vehicles, copy.Map.Tiles.Count); copy.Logistics.BindVehicles(copy.Vehicles);
        Assert.Equal(JsonSerializer.Serialize(logistics), JsonSerializer.Serialize(copy.Logistics.Capture()));
        Assert.Same(copy.Logistics.StackAt(RoadStack), copy.Logistics.Trucks[0].Source);
    }

    [Fact]
    public void CheckpointResumesAReservedLogWithoutDuplication()
    {
        var s = Build();
        var source = s.Logistics.StackAt(ForestStack)!;
        source.Volume = 3 * ForwarderLoading.LogVolume; source.Value = 300;
        var machine = Forwarder(s);
        Assert.True(s.Logistics.AssignForwarder(machine, source, RoadStack));
        for (int i = 0; i < 6000 && machine.LogTransferVolume == 0; i++) Run(s, 1.0 / 30);
        Assert.True(machine.LogTransferVolume > 0);
        Assert.True(s.Map.IsNetworkTile(machine.Tile));
        var checkpoint = JsonSerializer.Deserialize<LogisticsCheckpoint>(JsonSerializer.Serialize(s.Logistics.Capture()))!;
        var copy = Build(stacks: false); copy.Logistics.Restore(checkpoint);
        var restored = Forwarder(copy);
        Assert.Equal(machine.LogTransferVolume, restored.LogTransferVolume);
        Assert.Equal(machine.LogTransferValue, restored.LogTransferValue);
        Assert.Equal(machine.WorkTime, restored.WorkTime);
        Run(s, 20); Run(copy, 20);
        Assert.Equal(machine.Cargo, restored.Cargo, 4);
        Assert.Equal(machine.CargoValue, restored.CargoValue, 3);
        Assert.Equal(source.Volume, copy.Logistics.StackAt(ForestStack)!.Volume, 4);
        Assert.Equal(3 * ForwarderLoading.LogVolume,
            restored.Cargo + restored.LogTransferVolume + copy.Logistics.StackAt(ForestStack)!.Volume, 4);
    }

    [Fact]
    public void FleetCommandsReplay()
    {
        Assert.IsType<PlaceDepotCommand>(WorldCommandFactory.Create(new PlaceDepotCommand(5).ToRecord(1)));
        var send = new SendVehicleCommand(1, 9, -1, false).ToRecord(1);
        Assert.Equal(0, send.C);
        Assert.IsType<SendVehicleCommand>(WorldCommandFactory.Create(send));
        Assert.IsType<StackSiteCommand>(WorldCommandFactory.Create(new StackSiteCommand(9, true).ToRecord(1)));
        Assert.IsType<SendHomeCommand>(WorldCommandFactory.Create(new SendHomeCommand(1, false).ToRecord(1)));
    }
}
