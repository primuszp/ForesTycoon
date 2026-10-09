using System.Linq;
using System.Text.Json;

namespace ForesTycoon.Tests;

public class FleetTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static readonly int[] Stand = { 136, 137, 152, 153 };

    private sealed record Scene(TerrainMap Map, ForestSystem Forest, ForestryLogistics Logistics, VehicleSystem Vehicles);

    // Road along v = 2, a skid trail (8, 3) → (8, 7) into a 2×2 oak stand, a mill at (12, 3) and a depot at (4, 3).
    private static Scene Build()
    {
        var map = new TerrainMap(Settings, (_, _) => 4);
        var stands = new ForestStand[map.Tiles.Count];
        foreach (int id in Stand) stands[id] = new ForestStand(ForestSpecies.Oak, 60, 0.7f, 1);
        var forest = new ForestSystem(map, stands);
        map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        map.MarkSkidTrailPath(131, 135);
        var logistics = new ForestryLogistics(map, forest) { MachinesEnabled = true };
        var vehicles = new VehicleSystem(new TimberCargoSystem(), route => VehicleRoadRoute.Create(map, route))
        { SourceLoader = logistics.Load, DestinationReceiver = logistics.Deliver, RouteValidator = logistics.RouteConnected };
        logistics.Vehicles = vehicles;
        Assert.True(logistics.PlaceMill(195), logistics.Status);
        return new Scene(map, forest, logistics, vehicles);
    }

    private static void Run(Scene s, double seconds)
    {
        for (int i = 0; i < seconds * 30; i++) { s.Logistics.Update(1.0 / 30); s.Vehicles.Update(1.0 / 30); }
    }

    [Fact]
    public void FirstDepotHoldsTheStartingFleetAndNothingWorksUntilSent()
    {
        var s = Build();
        Assert.False(s.Logistics.PlaceDepot(118), "A depot away from roads was accepted.");
        Assert.True(s.Logistics.PlaceDepot(67), s.Logistics.Status);
        Assert.Equal(new[] { ForestMachineKind.Harvester, ForestMachineKind.Forwarder }, s.Logistics.Machines.Select(m => m.Kind));
        Assert.Single(s.Logistics.Trucks);
        s.Logistics.Designate(Stand);
        Run(s, 20);
        Assert.All(s.Logistics.Machines, m => Assert.Equal(67, m.Tile));
        Assert.Equal(TruckPhase.Parked, s.Logistics.Trucks[0].Phase);
        Assert.Empty(s.Vehicles.Vehicles);
    }

    [Fact]
    public void SentVehiclesClearTheSiteAndReturnHome()
    {
        var s = Build();
        s.Logistics.PlaceDepot(67); s.Logistics.Designate(Stand);
        var site = s.Logistics.Sites[0];
        float initial = s.Logistics.Volume(site);
        foreach (var m in s.Logistics.Machines) Assert.True(s.Logistics.AssignMachine(m, site), s.Logistics.Status);
        Assert.True(s.Logistics.AssignTruck(s.Logistics.Trucks[0], site), s.Logistics.Status);
        Assert.Equal(TruckPhase.ToWork, s.Logistics.Trucks[0].Phase);
        Run(s, 30);
        Assert.Contains(s.Logistics.Machines[0].Tile, Stand);                 // the processor drove out to the felling
        Run(s, 1600);
        Assert.True(s.Logistics.Volume(site) < 0.01f);
        Assert.Equal(initial, s.Logistics.Mills[0].Received, 1);
        Assert.All(s.Logistics.Machines, m => { Assert.Null(m.Site); Assert.Equal(67, m.Tile); Assert.Equal(ForestMachineState.Parked, m.State); });
        Assert.Equal(TruckPhase.Parked, s.Logistics.Trucks[0].Phase);
        Assert.Empty(s.Vehicles.Vehicles);
    }

    [Fact]
    public void CalledHomeTheProcessorStopsFelling()
    {
        var s = Build();
        s.Logistics.PlaceDepot(67); s.Logistics.Designate(Stand);
        var processor = s.Logistics.Machines[0];
        s.Logistics.AssignMachine(processor, s.Logistics.Sites[0]);
        Run(s, 30);
        Assert.Equal(ForestMachineState.Felling, processor.State);
        s.Logistics.SendHome(processor);
        Run(s, 60);
        Assert.Null(processor.Site); Assert.Equal(67, processor.Tile);
        Assert.True(s.Logistics.Volume(s.Logistics.Sites[0]) > 1, "It kept felling.");
    }

    [Fact]
    public void OrdersNeedATrailAndAConnectedMill()
    {
        var s = Build();
        s.Map.RemoveSkidTrailPath(131, 135);
        s.Logistics.PlaceDepot(67); s.Logistics.Designate(Stand);
        Assert.False(s.Logistics.AssignMachine(s.Logistics.Machines[0], s.Logistics.Sites[0]));
        Assert.Contains("közelítő nyom", s.Logistics.Status);
        Assert.False(s.Logistics.AssignTruck(s.Logistics.Trucks[0], s.Logistics.Sites[0]));
    }

    [Fact]
    public void CheckpointKeepsDepotsTrucksAndTheirVehicles()
    {
        var s = Build();
        s.Logistics.PlaceDepot(67); s.Logistics.Designate(Stand);
        foreach (var m in s.Logistics.Machines) s.Logistics.AssignMachine(m, s.Logistics.Sites[0]);
        s.Logistics.AssignTruck(s.Logistics.Trucks[0], s.Logistics.Sites[0]);
        Run(s, 40);
        var logistics = JsonSerializer.Deserialize<LogisticsCheckpoint>(JsonSerializer.Serialize(s.Logistics.Capture()))!;
        var vehicles = JsonSerializer.Deserialize<VehiclesCheckpoint>(JsonSerializer.Serialize(s.Vehicles.Capture()))!;
        var copy = Build();
        copy.Map.SetBuildingFootprint(s.Logistics.Depots[0].Footprint);
        copy.Logistics.Restore(logistics); copy.Vehicles.Restore(vehicles, copy.Map.Tiles.Count); copy.Logistics.BindVehicles(copy.Vehicles);
        Assert.Equal(JsonSerializer.Serialize(logistics), JsonSerializer.Serialize(copy.Logistics.Capture()));
        Assert.Same(copy.Vehicles.Vehicles.Single(v => v.Id == s.Logistics.Trucks[0].Vehicle!.Id), copy.Logistics.Trucks[0].Vehicle);
    }

    [Fact]
    public void FleetCommandsReplay()
    {
        Assert.IsType<PlaceDepotCommand>(WorldCommandFactory.Create(new PlaceDepotCommand(5).ToRecord(1)));
        Assert.IsType<SendVehicleCommand>(WorldCommandFactory.Create(new SendVehicleCommand(1, 9, true).ToRecord(1)));
        Assert.IsType<SendHomeCommand>(WorldCommandFactory.Create(new SendHomeCommand(1, false).ToRecord(1)));
    }
}
