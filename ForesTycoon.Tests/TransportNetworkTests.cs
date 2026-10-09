using System.Linq;

namespace ForesTycoon.Tests;

public class TransportNetworkTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static TerrainMap Flat() => new(Settings, (_, _) => 4);

    [Fact]
    public void TrailsJoinRoadsIntoOneNetwork()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 130, RoadPaving.Macadam);      // along u at v = 2
        map.MarkSkidTrailPath(130, 135);                          // from the road up to (8, 7)
        int[] path = map.FindNetworkPath(34, 135);
        Assert.Equal(new[] { 34, 50, 66, 82, 98, 114, 130, 131, 132, 133, 134, 135 }, path);
        // A trail drawn from beside the road (its loose end) joins too.
        map.MarkSkidTrailPath(19, 23);                            // (1, 3) → (1, 7): no road beside it, so not connected
        Assert.Empty(map.FindNetworkPath(34, 23));
        map.MarkSkidTrailPath(35, 39);                            // (2, 3) → (2, 7), loose end beside road tile 34
        Assert.Equal(39, map.FindNetworkPath(34, 39)[^1]);
    }

    [Fact]
    public void PathsPreferRoadsOverTrails()
    {
        var map = Flat();
        // Two ways from (2, 2) to (2, 6): a direct trail of 4 tiles, or a road detour of 8 tiles.
        map.BuildRoadTilePath(34, 66, RoadPaving.Asphalt); map.BuildRoadTilePath(66, 70, RoadPaving.Asphalt); map.BuildRoadTilePath(70, 38, RoadPaving.Asphalt);
        map.MarkSkidTrailPath(34, 38);
        Assert.Contains(66, map.FindNetworkPath(34, 38));
        Assert.True(map.NetworkCost(36) > 3 * map.NetworkCost(50));
    }

    [Fact]
    public void TruckCrawlsAndPitchesOnATrail()
    {
        double TopSpeed(bool trail, out float damage)
        {
            var map = Flat();
            int[] route = System.Linq.Enumerable.Range(2, 13).Select(u => u * 16 + 2).ToArray();
            if (trail) map.MarkSkidTrailPath(route[0], route[^1]); else map.BuildRoadTilePath(route[0], route[^1], RoadPaving.Macadam);
            var cargo = new TimberCargoSystem(); cargo.AddHarvested(100);
            var system = new VehicleSystem(cargo, r => VehicleRoadRoute.Create(map, r))
            {
                RoadState = id => map.IsRoadTile(id) ? (RoadSurface.Gravel, map.GetRoadCondition(id)) : (RoadSurface.Dirt, 0.2f - 0.2f * map.GetSkidTrailWear(id)),
                RoadWear = (id, amount) => { if (map.IsSkidTrail(id)) map.DriveSkidTrail(id, amount * 20); }
            };
            var truck = system.Spawn(route);
            double top = 0; damage = 0;
            for (int i = 0; i < 900; i++) { system.Update(1.0 / 30); if (truck.CurrentSpeed > top) { top = truck.CurrentSpeed; damage = truck.RoadDamage; } }
            return top;
        }
        double road = TopSpeed(false, out _), trail = TopSpeed(true, out float pitch);
        Assert.True(trail < road * 0.7, $"trail {trail} vs road {road}");
        Assert.True(pitch >= 0.8f);
    }
}
