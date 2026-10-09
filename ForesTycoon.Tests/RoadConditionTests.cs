using System.Linq;
using System.Text.Json;

namespace ForesTycoon.Tests;

public class RoadConditionTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static TerrainMap Flat() => new(Settings, (_, _) => 4);

    [Fact]
    public void NewRoadsCarryTheirSurfaceAndStartNew()
    {
        var map = Flat();
        Assert.Equal(4, map.BuildRoadTilePath(34, 82, RoadPaving.Macadam).Length);
        Assert.Equal(RoadPaving.Macadam, map.GetRoadPaving(50));
        Assert.Equal(1f, map.GetRoadCondition(50));
        // Building asphalt over macadam resurfaces the existing tiles; the new ones get asphalt too.
        int[] changed = map.BuildRoadTilePath(34, 98, RoadPaving.Asphalt);
        Assert.Equal(5, changed.Length);
        Assert.All(new[] { 34, 50, 66, 82, 98 }, id => Assert.Equal(RoadPaving.Asphalt, map.GetRoadPaving(id)));
    }

    [Fact]
    public void MacadamWashesOutFasterThanAsphaltAndRainSpeedsItUp()
    {
        var macadam = Flat(); macadam.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        var asphalt = Flat(); asphalt.BuildRoadTilePath(34, 82, RoadPaving.Asphalt);
        var wet = Flat(); wet.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        macadam.WeatherRoads(1, 0); asphalt.WeatherRoads(1, 0); wet.WeatherRoads(1, 0.8f);
        Assert.True(macadam.GetRoadCondition(50) < asphalt.GetRoadCondition(50) - 0.05f);
        Assert.True(wet.GetRoadCondition(50) < macadam.GetRoadCondition(50) - 0.2f);
        Assert.InRange(asphalt.GetRoadCondition(50), 0.9f, 1f);
    }

    [Fact]
    public void RepairRestoresOnlyTheWornTilesOfTheMarkedSection()
    {
        var map = Flat(); map.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        map.WearRoad(50, 0.6f); map.WearRoad(82, 0.3f);
        var plan = map.PlanRoadRepair(34, 66);
        Assert.Equal(new[] { 50 }, System.Array.ConvertAll(plan, p => p.Tile));
        var repaired = map.RepairRoadTilePath(34, 66);
        Assert.Single(repaired); Assert.Equal(0.6f, repaired[0].Damage, 4);
        Assert.Equal(1f, map.GetRoadCondition(50));
        Assert.Equal(0.7f, map.GetRoadCondition(82), 4); // outside the marked section
    }

    [Fact]
    public void CheckpointKeepsSurfaceAndConditionAndOldSavesLoadAsNewAsphalt()
    {
        var map = Flat(); map.BuildRoadTilePath(34, 82, RoadPaving.Macadam); map.WearRoad(66, 0.4f);
        var state = JsonSerializer.Deserialize<TerrainCheckpoint>(JsonSerializer.Serialize(map.Capture()))!;
        var restored = Flat(); restored.Restore(state);
        Assert.Equal(RoadPaving.Macadam, restored.GetRoadPaving(66));
        Assert.Equal(0.6f, restored.GetRoadCondition(66), 4);
        var legacy = state with { Roads = System.Array.ConvertAll(state.Roads, r => new RoadCheckpoint(r.TileId, r.Edges)) };
        var old = Flat(); old.Restore(legacy);
        Assert.Equal(RoadPaving.Asphalt, old.GetRoadPaving(66)); Assert.Equal(1f, old.GetRoadCondition(66));
    }

    [Fact]
    public void RoadCommandsRecordTheSurfaceAndReplayIt()
    {
        var record = new RoadPathCommand(1, 2, false, RoadPaving.Macadam).ToRecord(7);
        Assert.Equal((int)RoadPaving.Macadam, record.C);
        Assert.IsType<RoadPathCommand>(WorldCommandFactory.Create(record));
        Assert.IsType<RoadRepairCommand>(WorldCommandFactory.Create(new RoadRepairCommand(1, 2).ToRecord(7)));
        Assert.Throws<System.InvalidOperationException>(() => WorldCommandFactory.Create(record with { C = 9 }));
    }

    [Fact]
    public void WornRoadSlowsAndShakesTheTruckAndTrafficWearsMacadam()
    {
        double Run(float condition, out float damage, out TerrainMap map)
        {
            // A long straight so the top speed is set by the road, not by braking for the end of the route.
            var m = Flat(); m.BuildRoadTilePath(34, 226, RoadPaving.Macadam);
            int[] route = System.Linq.Enumerable.Range(2, 13).Select(u => u * 16 + 2).ToArray();
            foreach (int id in route) m.WearRoad(id, 1 - condition);
            var cargo = new TimberCargoSystem(); cargo.AddHarvested(100);
            var system = new VehicleSystem(cargo, route => VehicleRoadRoute.Create(m, route))
            {
                RoadState = id => (m.GetRoadPaving(id) == RoadPaving.Asphalt ? RoadSurface.Asphalt : RoadSurface.Gravel, m.GetRoadCondition(id)),
                RoadWear = (id, amount) => m.WearRoad(id, amount)
            };
            var truck = system.Spawn(route);
            double top = 0; damage = 0;
            for (int i = 0; i < 900; i++)
            {
                system.Update(1.0 / 30);
                if (truck.CurrentSpeed > top) { top = truck.CurrentSpeed; damage = truck.RoadDamage; }
            }
            map = m;
            return top;
        }
        double good = Run(1, out float none, out var fresh), bad = Run(0.2f, out float worn, out _);
        Assert.True(bad < good * 0.92, $"top speed worn {bad} vs new {good}");
        Assert.InRange(none, 0f, 0.05f); Assert.InRange(worn, 0.7f, 0.9f);
        Assert.True(fresh.GetRoadCondition(50) < 1f, "Traffic did not wear the macadam.");
    }
}
