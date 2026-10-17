using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void DepotDenialDoesNotMutateAndObjectGraphControlsActualStartingFleet()
    {
        var scene = Build();
        var allow = TransportConstant("depot.allow", "depot", 0);
        SetTransportModel(scene, new() { Graphs = new() { allow } });
        string before = JsonSerializer.Serialize(scene.Logistics.Capture() with { Status = "" });
        Assert.False(scene.Logistics.PlaceDepot(99));
        Assert.Equal(before, JsonSerializer.Serialize(scene.Logistics.Capture() with { Status = "" }));
        allow.Nodes[0].Value = 1;
        SetTransportModel(scene, new() { Graphs = new() { allow,
            TransportConstant("depot.processors", "depot", 2.9, 99),
            TransportConstant("depot.forwarders", "depot", 0, 99),
            TransportConstant("depot.trucks", "depot", 3, 99) } });
        Assert.True(scene.Logistics.PlaceDepot(99), scene.Logistics.Status);
        Assert.Equal(2, scene.Logistics.Machines.Count(m => m.Home.TileId == 99));
        Assert.All(scene.Logistics.Machines.Where(m => m.Home.TileId == 99), m => Assert.Equal(ForestMachineKind.Harvester, m.Kind));
        Assert.Equal(3, scene.Logistics.Trucks.Count(t => t.Home.TileId == 99));
        Assert.False(scene.Logistics.PlaceDepot(99)); // Even an allow graph cannot overlap a building.
    }

    [Fact]
    public void LargeDepotCreatesEachActorInSeparateGuardedUnitWithStableIdsAndSeeds()
    {
        var whole = Build(); var sliced = Build();
        foreach (var scene in new[] { whole, sliced }) SetTransportModel(scene, new() { Graphs = new() {
            TransportConstant("depot.processors", "depot", 1024),
            TransportConstant("depot.forwarders", "depot", 1024),
            TransportConstant("depot.trucks", "depot", 1024) } });
        Assert.True(whole.Logistics.PlaceDepot(99));
        var result = new WorkResult<bool>(); int units = 0;
        using (var work = sliced.Logistics.PlaceDepotWork(99, result).GetEnumerator())
            while (SimulationWork.MoveNext(work)) units++;
        Assert.True(result.Value); Assert.True(units >= 3072);
        Assert.Equal(JsonSerializer.Serialize(whole.Logistics.Capture()), JsonSerializer.Serialize(sliced.Logistics.Capture()));
    }
}
