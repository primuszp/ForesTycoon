namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void ANewDepotDepartureDiscardsPresentationOfThePreviousMechanicalProfile()
    {
        var scene = Build(); var machine = Processor(scene);
        var profile = VehicleGeometryProfiles.Machine(GameTuning.Default, machine.Kind);
        machine.SteeringDriver = new(new VehicleKinematics(profile, default, 0));
        machine.Presentation.Publish(new(machine.Kinematics.CapturePresentation(), true));
        Assert.True(scene.Logistics.AssignProcessor(machine, scene.Logistics.StackAt(ForestStack)));
        Assert.Null(machine.SteeringDriver); Assert.Null(machine.Presentation.Current); Assert.Null(machine.Presentation.Previous);
    }

    [Fact]
    public void NativeLayoutFinishesWithTheWorldsConfiguredPhysicalGeometry()
    {
        var map = new TerrainMap(Settings, (_, _) => 4);
        using var world = new SimulationWorld(map);
        world.Forest.Clear();
        int[] stand = [136, 137, 138, 152, 153, 154];
        world.Forest.PaintInitialForest(stand.ToDictionary(id => id, _ => ForestSpecies.Spruce));
        map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        map.MarkSkidTrailPath(115, 119); map.MarkSkidTrailPath(119, 135); map.MarkSkidTrailPath(117, 85);
        var logistics = world.Logistics; var vehicles = logistics.Vehicles;
        Assert.True(logistics.PlaceMill(195)); Assert.True(logistics.PlaceDepot(67)); logistics.Designate(stand);
        Assert.True(logistics.PlaceStack(120)); Assert.True(logistics.PlaceStack(99));
        foreach (var m in logistics.Machines)
            Assert.True(m.Kind == ForestMachineKind.Harvester ? logistics.AssignProcessor(m, logistics.StackAt(120)) :
                logistics.AssignForwarder(m, logistics.StackAt(120), 99));
        Assert.True(logistics.AssignTruck(logistics.Trucks[0], logistics.StackAt(99), 195));
        double budget = logistics.Remaining / ForwarderLoading.LogVolume * 24 + 2700;
        for (int i = 0; i < budget * 30; i++) {
            logistics.Update(1d / 30); vehicles.Update(1d / 30); SimulationWork.Complete(world.TrafficWork());
            if (logistics.Machines.Any(m => m.SteeringDriver?.BlockedReason != null)) break;
            if (logistics.Machines.All(m => !m.Working && m.Tile == 67) && logistics.Trucks[0].Phase == TruckPhase.Parked) break;
        }
        string diagnostic = logistics.Status + "\n" + string.Join("\n", logistics.Machines.Select(m =>
            $"{m.Kind}: {m.State}, tile={m.Tile}, path={string.Join(',', m.Path)}, position={m.PathPosition:R}, cargo={m.Cargo}, pose={m.Kinematics?.Units[0]}, blocked={m.SteeringDriver?.BlockedReason}"));
        Assert.True(logistics.Machines.All(m => !m.Working && m.Tile == 67), diagnostic);
        Assert.Equal(TruckPhase.Parked, logistics.Trucks[0].Phase);
        Assert.True(logistics.Remaining < .01, diagnostic); Assert.True(logistics.Mills[0].Received > 1);
    }

    [Fact]
    public void ConfiguredTruckAndPassiveTrailerCompleteTheTimberCycleAndReturnWithoutLosingWood()
    {
        var s = Build(); s.Vehicles.Spec = GameTuning.Default.Truck;
        var truck = s.Logistics.Trucks[0]; var source = s.Logistics.StackAt(RoadStack);
        source.Add(3, 300);
        Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        for (int i = 0; i < 9000 && truck.Phase != TruckPhase.Parked; i++) Run(s, 1d / 30);
        Assert.Equal(3, s.Logistics.Mills[0].Received, 3);
        Assert.Equal(300, s.Logistics.Income, 3);
        Assert.Equal(TruckPhase.Parked, truck.Phase); Assert.Equal(0, source.Volume);
        Assert.Empty(s.Vehicles.Vehicles);
    }
}
