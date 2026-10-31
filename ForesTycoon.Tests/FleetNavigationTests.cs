using System.Linq;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void ForwarderDestinationRemainsReservedWhileEmpty()
    {
        var s = Build(); var machine = Forwarder(s); var source = s.Logistics.StackAt(ForestStack);
        source.Add(2, 200);
        Assert.True(s.Logistics.AssignForwarder(machine, source, RoadStack));
        Assert.False(s.Logistics.RemoveStack(RoadStack));
        Run(s, 120);
        Assert.Equal(2, s.Logistics.StackAt(RoadStack).Volume, 4);
        Assert.Equal(200, s.Logistics.StackAt(RoadStack).Value, 3);
    }

    [Fact]
    public void TruckSelectsADockReachableFromItsDepotInsteadOfAnIsolatedCheaperPair()
    {
        var s = Build();
        s.Map.BuildRoadTilePath(148, 180, RoadPaving.Asphalt);
        var source = s.Logistics.StackAt(RoadStack); source.Add(5, 500);
        var truck = s.Logistics.Trucks[0];
        Assert.Null(s.Logistics.CheckDestination(RoadStack, Mill, true, unit: truck));
        Assert.True(s.Logistics.AssignTruck(truck, source, Mill), s.Logistics.Status);
        Assert.Equal(146, truck.Vehicle.Route[^1]);
        Run(s, 240);
        Assert.Equal(5, s.Logistics.Mills[0].Received, 4); Assert.Equal(500, s.Logistics.Income, 3);
    }

    [Fact]
    public void TruckCanLeaveAndReturnThroughAnyReachableDepotDock()
    {
        var s = Build();
        s.Map.RemoveRoadTilePath(50, 82);
        s.Map.BuildRoadTilePath(66, 65); s.Map.BuildRoadTilePath(82, 81);
        Assert.Empty(s.Map.FindNetworkPath(66, 146));
        var source = s.Logistics.StackAt(RoadStack); source.Add(5, 500);
        var truck = s.Logistics.Trucks[0];
        Assert.True(s.Logistics.AssignTruck(truck, source, Mill), s.Logistics.Status);
        Assert.Equal(82, truck.Vehicle.Route[0]);
        Run(s, 240);
        Assert.Equal(TruckPhase.Parked, truck.Phase); Assert.Equal(5, s.Logistics.Mills[0].Received, 4);
    }

    [Fact]
    public void ArrivingTruckStartsItsShuttleAtItsPhysicalDockAfterANewShortcutIsBuilt()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(5, 500);
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        for (int i = 0; i < 10000 && !truck.Vehicle.TransitArrived; i++) s.Vehicles.Update(1d / 30);
        Assert.True(truck.Vehicle.TransitArrived); int at = truck.Vehicle.Route[^1];
        s.Map.BuildRoadTilePath(148, 180, RoadPaving.Asphalt); s.Map.BuildRoadTilePath(178, 180, RoadPaving.Asphalt);
        s.Logistics.Update(1d / 30);
        Assert.Equal(TruckPhase.Working, truck.Phase); Assert.Equal(at, truck.Vehicle.Route[0]);
        Run(s, 240); Assert.Equal(5, s.Logistics.Mills[0].Received, 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwarderDoesNotCrossARemovedTrailEvenInsideADesignatedWorksite(bool detour)
    {
        var s = Build(); var machine = Forwarder(s);
        machine.Path = new[] { 130, 131, 132, 133 }; machine.State = ForestMachineState.Driving; machine.Goal = ForestMachineState.Parked;
        machine.Cargo = 5; machine.CargoValue = 500;
        s.Logistics.Sites.Add(new HarvestSite { Tiles = new[] { 131, 132 } });
        s.Map.RemoveSkidTrailPath(131, 132);
        if (detour) { s.Map.MarkSkidTrailPath(114, 117); s.Map.MarkSkidTrailPath(117, 133); }
        Run(s, 30);
        Assert.Equal(detour ? 133 : 130, machine.Tile);
        Assert.Equal(5, machine.Cargo); Assert.Equal(500, machine.CargoValue);
        if (detour) Assert.Contains(117, machine.Path); else Assert.Equal(0, machine.PathPosition);
    }

    private static (Scene Scene, FleetTruck Truck) LoadedShuttle(bool physics = false)
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(5, 500);
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        s.Vehicles.Remove(truck.Vehicle); s.Vehicles.UseRoadPhysics = physics;
        truck.Vehicle = s.Vehicles.SpawnLogistics(new[] { 146, 162, 178, 194 }, new[] { RoadStack }, Mill);
        truck.Phase = TruckPhase.Working; truck.Vehicle.Load(5); truck.Vehicle.TransportState = VehicleTransportState.Hauling;
        source.Take(5); truck.CargoValue = 500;
        return (s, truck);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedTruckReroutesWithoutReplacingItsIdentityCargoOrCurrentSegment(bool betweenTiles)
    {
        var (s, truck) = LoadedShuttle(); var vehicle = truck.Vehicle;
        if (betweenTiles) vehicle.Update(.2);
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out int to, out float amount);
        s.Map.BuildRoadTilePath(146, 145); s.Map.BuildRoadTilePath(145, 193); s.Map.BuildRoadTilePath(193, 194);
        if (betweenTiles) { s.Map.BuildRoadTilePath(162, 161); s.Map.RemoveRoadTilePath(162, 194); }
        else s.Map.RemoveRoadTilePath(146, 178);
        double fuel = vehicle.FuelUsed, distance = vehicle.Distance;
        s.Logistics.Update(1d / 30);
        Assert.Same(vehicle, truck.Vehicle); Assert.False(vehicle.RouteBlocked);
        Assert.Equal(5, vehicle.CargoAmount); Assert.Equal(500, truck.CargoValue);
        Assert.Equal(fuel, vehicle.FuelUsed); Assert.Equal(distance, vehicle.Distance);
        vehicle.GetSegment(vehicle.RoutePosition, out int nextFrom, out int nextTo, out float nextAmount);
        Assert.Equal(from, nextFrom); Assert.Equal(amount, nextAmount, 5);
        if (betweenTiles) Assert.Equal(to, nextTo);
        Assert.Contains(161, vehicle.Route);
        Run(s, 240); Assert.Equal(5, s.Logistics.Mills[0].Received, 4); Assert.Equal(500, s.Logistics.Income, 3);
    }

    [Fact]
    public void TruckWaitsInsideARemovedSegmentInsteadOfTeleportingToADetour()
    {
        var (s, truck) = LoadedShuttle(); var vehicle = truck.Vehicle; vehicle.Update(.2);
        double before = vehicle.RoutePosition;
        s.Map.BuildRoadTilePath(146, 145); s.Map.BuildRoadTilePath(145, 193); s.Map.BuildRoadTilePath(193, 194);
        s.Map.RemoveRoadTilePath(146, 178);
        Run(s, 10);
        Assert.True(vehicle.RouteBlocked); Assert.Equal(before, vehicle.RoutePosition); Assert.Equal(5, vehicle.CargoAmount);
        s.Map.BuildRoadTilePath(146, 178); Run(s, 240);
        Assert.Equal(5, s.Logistics.Mills[0].Received, 4);
    }

    [Fact]
    public void TruckCanDeliverItsLoadWhenTheSourceHasBeenCutOffBehindIt()
    {
        var (s, truck) = LoadedShuttle(); var vehicle = truck.Vehicle; vehicle.Update(1); // physical position 162 → 178
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out _, out _); Assert.Equal(162, from);
        s.Map.RemoveRoadTilePath(130, 162); // 146 disappears, the current segment remains.
        Run(s, 120);
        Assert.Equal(5, s.Logistics.Mills[0].Received, 4); Assert.Equal(500, s.Logistics.Income, 3);
        Assert.Equal(0, vehicle.CargoAmount);
    }

    [Fact]
    public void AdjacentStacksCanUseTheSameTruckDockWithoutInventingADrivingLoop()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(2, 200);
        Assert.True(s.Logistics.PlaceStack(145));
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, 145));
        Run(s, 120);
        Assert.Equal(2, s.Logistics.StackAt(145).Volume, 4); Assert.Equal(200, s.Logistics.StackAt(145).Value, 4);
        Assert.Equal(0, s.Logistics.Income); Assert.Equal(TruckPhase.Parked, truck.Phase);
    }

    [Fact]
    public void CargoCannotBeLoadedOrDeliveredRemotelyAndTinyStacksCannotBeDeleted()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(2, 200);
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        Assert.Equal(0, s.Logistics.Load(truck.Vehicle, 1)); Assert.Equal(2, source.Volume);
        truck.Vehicle.Load(1); truck.CargoValue = 100;
        float taken = truck.Vehicle.TakeCargo(1); s.Logistics.Deliver(truck.Vehicle, taken);
        Assert.Equal(1, truck.Vehicle.CargoAmount); Assert.Equal(100, truck.CargoValue);
        Assert.Equal(0, s.Logistics.Income); Assert.Equal(0, s.Logistics.Mills[0].Received);
        s.Logistics.StackAt(ForestStack).Add(.005f, .5); Assert.False(s.Logistics.RemoveStack(ForestStack));
    }

    [Fact]
    public void ANewMachineOrderFinishesOnlyItsCurrentSegmentBeforeReplanning()
    {
        var s = Build(); var machine = Processor(s);
        machine.Path = new[] { 66, 82, 98, 114, 130 }; machine.PathPosition = machine.PreviousPathPosition = .25;
        machine.State = ForestMachineState.Driving; machine.Goal = ForestMachineState.Parked;
        Assert.True(s.Logistics.AssignProcessor(machine, s.Logistics.StackAt(ForestStack)));
        Assert.Equal(new[] { 66, 82 }, machine.Path); Assert.Equal(.25, machine.PathPosition);
        Run(s, 120); Assert.True(s.Logistics.StackAt(ForestStack).Volume > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReroutingWithCargoHasIdenticalSlicedAndSavedContinuation(bool physics)
    {
        var (fixture, truck) = LoadedShuttle(physics); truck.Vehicle.Update(.2);
        fixture.Map.BuildRoadTilePath(146, 145); fixture.Map.BuildRoadTilePath(145, 193); fixture.Map.BuildRoadTilePath(193, 194);
        fixture.Map.BuildRoadTilePath(162, 161); fixture.Map.RemoveRoadTilePath(162, 194);
        using var world = new GameWorld(Settings, enableRendering: false);
        world.Map.Restore(fixture.Map.Capture()); world.Forest.Clear();
        world.Logistics.Restore(fixture.Logistics.Capture()); world.VehicleState.Restore(fixture.Vehicles.Capture(), world.Map.Tiles.Count);
        world.Logistics.BindVehicles(world.VehicleState);
        using (var work = world.Logistics.UpdateWork(1d / 30).GetEnumerator()) {
            int units = 0; while (SimulationWork.MoveNext(work, 1)) { Assert.Equal(1, work.Current); Assert.True(++units < 50000); }
        }
        Assert.Contains(161, world.Logistics.Trucks[0].Vehicle.Route);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = new GameWorld(Settings, enableRendering: false); restored.Load(save);
        for (int i = 0; i < 60; i++) {
            world.Update(1d / 30); while (!restored.ContinueUpdate(1d / 30, 3)) { }
        }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()),
            System.Text.Json.JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }

    [Fact]
    public void ReturningTruckPreservesItsDirectionAndFractionWhenItNeedsADetour()
    {
        var (s, truck) = LoadedShuttle(); var vehicle = truck.Vehicle;
        for (int i = 0; i < 10000 && vehicle.TransportState != VehicleTransportState.Returning; i++) s.Vehicles.Update(1d / 30);
        Assert.Equal(VehicleTransportState.Returning, vehicle.TransportState);
        s.Vehicles.Update(.2);
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out int to, out float fraction);
        s.Map.BuildRoadTilePath(146, 145); s.Map.BuildRoadTilePath(145, 193); s.Map.BuildRoadTilePath(193, 194);
        s.Map.RemoveRoadTilePath(146, 178); s.Map.BuildRoadTilePath(178, 177);
        s.Logistics.Update(1d / 30);
        Assert.Same(vehicle, truck.Vehicle); Assert.False(vehicle.RouteBlocked);
        vehicle.GetSegment(vehicle.RoutePosition, out int nextFrom, out int nextTo, out float nextFraction);
        Assert.Equal(from, nextFrom); Assert.Equal(to, nextTo); Assert.Equal(fraction, nextFraction, 5);
        Run(s, 240); Assert.Equal(TruckPhase.Parked, truck.Phase); Assert.Equal(500, s.Logistics.Income, 3);
    }

    [Fact]
    public void StationaryCargoRouteHasFiniteGeometryAndResumesItsCheckpoint()
    {
        var map = new TerrainMap(Settings, (_, _) => 4); map.BuildRoadTilePath(146, 178);
        var system = new VehicleSystem(roadRouteFactory: r => VehicleRoadRoute.Create(map, r));
        var vehicle = system.SpawnLogistics(new[] { 146 }, new[] { 147 }, 145);
        vehicle.Load(2); vehicle.TransportState = VehicleTransportState.Hauling;
        vehicle.RoadRoute.GetPose(0, out var centre, out var direction, out _, out _);
        Assert.True(float.IsFinite(centre.X) && float.IsFinite(direction.X));
        var copy = new VehicleSystem(); copy.Restore(system.Capture(), map.Tiles.Count);
        float delivered = 0; copy.DestinationReceiver = (_, v) => delivered += v;
        copy.Update(10);
        Assert.Equal(2, delivered, 4); Assert.Equal(0, copy.Vehicles[0].CargoAmount);
        Assert.Equal(0, copy.Vehicles[0].RoutePosition); Assert.Equal(0, copy.Vehicles[0].Distance);
    }

    [Fact]
    public void EarlierNonPhysicalOvershootImportsAtItsTransferStopWithoutChangingCargo()
    {
        var (s, truck) = LoadedShuttle();
        var vehicle = truck.Vehicle.Capture() with { Position = 3.05, PreviousPosition = 3.04, State = VehicleTransportState.Unloading };
        using var world = new GameWorld(Settings, enableRendering: false);
        var state = world.CaptureCheckpoint() with { Vehicles = s.Vehicles.Capture() with { Vehicles = new[] { vehicle } } };
        var save = new WorldSaveData("forestycoon-simulation/2026-10-31.2") { Checkpoint = state };
        var restored = Assert.Single(save.ReplayCheckpoint.Vehicles.Vehicles);
        Assert.Equal(3, restored.Position); Assert.Equal(3, restored.PreviousPosition);
        Assert.Equal(vehicle.Cargo, restored.Cargo); Assert.Equal(vehicle.FuelUsed, restored.FuelUsed);
    }

    [Fact]
    public void DepotCanUseSkidTrailDocksAsPartOfTheSharedNetwork()
    {
        var s = Build(); s.Map.RemoveRoadTilePath(50, 98); s.Map.MarkSkidTrailPath(50, 98);
        Assert.Empty(s.Map.FindRoadDocks(s.Logistics.Depots[0].Footprint));
        Assert.NotEmpty(s.Map.FindNetworkDocks(s.Logistics.Depots[0].Footprint));
        var source = s.Logistics.StackAt(RoadStack); source.Add(2, 200); var truck = s.Logistics.Trucks[0];
        Assert.True(s.Logistics.AssignTruck(truck, source, Mill), s.Logistics.Status);
        Run(s, 240);
        Assert.Equal(2, s.Logistics.Mills[0].Received, 4); Assert.Equal(TruckPhase.Parked, truck.Phase);
    }

    [Fact]
    public void SourcePickerRejectsAStackInADisconnectedNetworkComponent()
    {
        var s = Build(); s.Map.RemoveRoadTilePath(98, 146);
        Assert.NotEmpty(s.Map.FindNetworkDocks(new[] { ForestStack }));
        var truck = s.Logistics.Trucks[0];
        Assert.NotNull(s.Logistics.CheckSource(ForestStack, true, Depot, unit: truck));
    }
}
