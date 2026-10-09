using System.Linq;
using System.Text.Json;

namespace ForesTycoon.Tests;

public class ForestMachineTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static readonly int[] Stand = { 136, 137, 152, 153 };   // tiles (8..9, 8..9)

    private sealed record Scene(TerrainMap Map, ForestSystem Forest, ForestryLogistics Logistics, VehicleSystem Vehicles, TimberCargoSystem Cargo);

    /// <summary>A 2×2 oak stand in the middle, a road along v = 2, and a skid trail from the road (8, 3) to the stand (8, 7).</summary>
    private static Scene Build(bool trail = true)
    {
        var map = new TerrainMap(Settings, (_, _) => 4);
        var stands = new ForestStand[map.Tiles.Count];
        foreach (int id in Stand) stands[id] = new ForestStand(ForestSpecies.Oak, 60, 0.7f, 1);
        var forest = new ForestSystem(map, stands);
        map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        if (trail) map.MarkSkidTrailPath(131, 135);
        var logistics = new ForestryLogistics(map, forest) { MachinesEnabled = true, AutoMachines = true };
        var cargo = new TimberCargoSystem();
        var vehicles = new VehicleSystem(cargo, route => VehicleRoadRoute.Create(map, route))
        {
            SourceLoader = logistics.Load, DestinationReceiver = logistics.Deliver, RouteValidator = logistics.RouteConnected
        };
        return new Scene(map, forest, logistics, vehicles, cargo);
    }

    private static void Run(Scene s, double seconds)
    {
        for (int i = 0; i < seconds * 30; i++) { s.Logistics.Update(1.0 / 30); s.Vehicles.Update(1.0 / 30); }
    }

    [Fact]
    public void WithoutATrailTheMachinesCannotReachTheFelling()
    {
        var s = Build(trail: false);
        Assert.Equal(4, s.Logistics.Designate(Stand));
        Assert.Equal(-1, s.Logistics.Sites[0].Landing);
        Assert.Empty(s.Logistics.Machines);
        Assert.Contains("közelítő nyom", s.Logistics.Status);
        // Marking the trail later connects the site and brings the machines.
        s.Map.MarkSkidTrailPath(131, 135); s.Logistics.TrailsChanged();
        Assert.Equal(131, s.Logistics.Sites[0].Landing);
        Assert.Equal(2, s.Logistics.Machines.Count);
    }

    [Fact]
    public void HarvesterFellsForwarderSkidsToTheLandingAndTheTruckHaulsToTheMill()
    {
        var s = Build();
        Assert.True(s.Logistics.PlaceMill(195), s.Logistics.Status);
        s.Logistics.Designate(Stand);
        var site = s.Logistics.Sites[0];
        float initial = s.Logistics.Volume(site);
        Assert.Equal(131, site.Landing);
        Assert.Equal(new[] { ForestMachineKind.Harvester, ForestMachineKind.Forwarder }, s.Logistics.Machines.Select(m => m.Kind));

        Run(s, 20);
        var harvester = s.Logistics.Machines[0];
        Assert.Contains(harvester.Tile, Stand);
        Assert.True(site.Piles.Count > 0 || site.LandingStock > 0, "Nothing was felled.");
        Assert.True(s.Map.GetSkidTrailWear(133) > 0, "Machines left no ruts on the trail.");
        // Trucks only load what the forwarder brought to the landing.
        Assert.True(s.Logistics.Dispatch(s.Vehicles), s.Logistics.Status);
        Run(s, 400);
        float delivered = s.Logistics.Mills[0].Received;
        Assert.True(delivered > 0, "No timber reached the mill.");
        float inTrucks = s.Vehicles.Vehicles.Sum(v => v.CargoAmount);
        float onForwarder = s.Logistics.Machines.Sum(m => m.Cargo);
        Assert.Equal(initial, s.Logistics.Volume(site) + delivered + inTrucks + onForwarder, 2);
        Assert.True(s.Map.GetSkidTrailWear(133) > s.Map.GetSkidTrailWear(131) * 0.5f);
    }

    [Fact]
    public void FinishedSiteSendsItsMachinesAway()
    {
        var s = Build();
        s.Logistics.PlaceMill(195); s.Logistics.Designate(Stand); s.Logistics.Dispatch(s.Vehicles);
        Run(s, 1500);
        Assert.True(s.Logistics.Volume(s.Logistics.Sites[0]) < 0.01f);
        Assert.Empty(s.Logistics.Machines);
    }

    [Fact]
    public void CheckpointRestoresMachinesMidWork()
    {
        var s = Build(); s.Logistics.Designate(Stand); Run(s, 25);
        var state = JsonSerializer.Deserialize<LogisticsCheckpoint>(JsonSerializer.Serialize(s.Logistics.Capture()))!;
        Assert.NotEmpty(state.Machines!); Assert.True(state.Sites[0].Piles!.Length + state.Sites[0].LandingStock > 0);
        var copy = Build(); copy.Logistics.Restore(state);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(copy.Logistics.Capture()));
        // A machine pointing at a missing site is rejected.
        var bad = state with { Machines = new[] { state.Machines![0] with { Site = 5 } } };
        Assert.ThrowsAny<System.Exception>(() => Build().Logistics.Restore(bad));
    }
}
