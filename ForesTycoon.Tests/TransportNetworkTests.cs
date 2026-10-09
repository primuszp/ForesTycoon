using System.Linq;

namespace ForesTycoon.Tests;

public class TransportNetworkTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static TerrainMap Flat() => new(Settings, (_, _) => 4);

    [Fact]
    public void SeparatelyDrawnRoadAndTrailEndpointsJoinInBothDirectionsAndRenderBothArms()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 66, RoadPaving.Asphalt);
        map.BuildRoadTilePath(82, 114, RoadPaving.Macadam);
        map.MarkSkidTrailPath(130, 162);
        map.MarkSkidTrailPath(178, 210);
        int[] expected = Enumerable.Range(2, 12).Select(u => u * 16 + 2).ToArray();
        Assert.Equal(expected, map.FindNetworkPath(34, 210));
        Assert.Equal(expected.Reverse(), map.FindNetworkPath(210, 34));
        foreach (int id in expected.Skip(1).SkipLast(1))
            Assert.Equal(RoadEdge.SE | RoadEdge.NW, map.GetNetworkEdges(id));
        var restored = Flat(); restored.Restore(map.Capture());
        Assert.Equal(expected, restored.FindNetworkPath(34, 210));
    }

    [Fact]
    public void AnUnconnectedGapAndParallelThroughRoadsStillBlockTravel()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 66, RoadPaving.Asphalt);
        map.BuildRoadTilePath(98, 130, RoadPaving.Macadam);
        Assert.Empty(map.FindNetworkPath(34, 130));
        map.BuildRoadTilePath(35, 67, RoadPaving.Macadam);
        Assert.False(map.AreNetworkNeighbours(50, 51));
    }

    [Fact]
    public void AddingBranchesPreservesTheExistingMixedSurfaceJoin()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 66, RoadPaving.Asphalt);
        map.BuildRoadTilePath(82, 114, RoadPaving.Macadam);
        Assert.NotEmpty(map.FindNetworkPath(34, 114));
        map.BuildRoadTilePath(66, 69, RoadPaving.Asphalt);
        map.BuildRoadTilePath(82, 85, RoadPaving.Macadam);
        Assert.NotEmpty(map.FindNetworkPath(34, 114));
        Assert.NotEmpty(map.FindNetworkPath(69, 85));
    }

    [Fact]
    public void RoadExitAndTrailArmsAgreeWithRoutesAndDisappearAfterRemoval()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 130, RoadPaving.Macadam);
        map.MarkSkidTrailPath(131, 135);
        Assert.True((map.GetNetworkEdges(130) & RoadEdge.EN) != 0);
        Assert.True((map.GetNetworkEdges(131) & RoadEdge.WS) != 0);
        int[] path = map.FindNetworkPath(34, 135);
        Assert.Equal(path.Reverse(), map.FindNetworkPath(135, 34));
        for (int i = 1; i < path.Length; i++) Assert.True(map.AreNetworkNeighbours(path[i - 1], path[i]));
        map.RemoveSkidTrailPath(131, 135);
        Assert.False((map.GetNetworkEdges(130) & RoadEdge.EN) != 0);
        Assert.Empty(map.FindNetworkPath(34, 135));
    }

    [Fact]
    public void AdjacentTrailsNeedReciprocalArmsAndVehiclesRejectDisconnectedRoutes()
    {
        var map = Flat();
        map.MarkSkidTrailPath(34, 66);
        map.MarkSkidTrailPath(35, 67);
        Assert.False(map.AreNetworkNeighbours(50, 51));
        Assert.Empty(map.FindNetworkPath(34, 35));
        Assert.Throws<ArgumentException>(() => VehicleRoadRoute.Create(map, new[] { 50, 51 }));
        Assert.Equal(0, map.GetNetworkNeighbours(-1, new int[4]));
    }

    [Fact]
    public void UpgradingATrailPreservesItsJunctionWithoutLeavingAnOverlappingTrail()
    {
        var map = Flat();
        map.MarkSkidTrailPath(34, 38);
        map.BuildRoadTilePath(36, 68, RoadPaving.Macadam);
        Assert.False(map.IsSkidTrail(36));
        Assert.True(map.IsRoadTile(36));
        Assert.NotEmpty(map.FindNetworkPath(34, 68));
        Assert.NotEmpty(map.FindNetworkPath(38, 68));
    }

    [Fact]
    public void TrailRampMeetsTheLockedRoadSurfaceAfterTerrainEditing()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 130);
        map.MarkSkidTrailPath(130, 135);
        var tile = map.Tiles[130];
        foreach (var node in new[] { tile.W, tile.S, tile.E, tile.N })
            map.EditElevationAtNode(node.Id, -1, 0, 1);
        map.GetTrailSurfaceCorners(131, out var w, out var s, out _, out _);
        Assert.Equal(map.RoadCorner(tile.N).Z, w.Z);
        Assert.Equal(map.RoadCorner(tile.E).Z, s.Z);
        var midpoint = (w + s) * 0.5f;
        Assert.True(map.TryGetDrivingSurfaceZ(midpoint.X, midpoint.Y, out float height));
        Assert.Equal(midpoint.Z, height, 4);
    }

    [Fact]
    public void ForestMachineWaitsAtABrokenConnectionAndResumesWhenItIsBuilt()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 66);
        map.BuildRoadTilePath(35, 67);
        var logistics = new ForestryLogistics(map, new ForestSystem(map, new ForestStand[256]));
        var machine = new ForestMachine { Path = new[] { 50, 51 }, State = ForestMachineState.Driving, Goal = ForestMachineState.Parked };
        logistics.Machines.Add(machine);
        logistics.MachinesEnabled = true;
        logistics.Update(1);
        Assert.Equal(0, machine.PathPosition);
        map.BuildRoadTilePath(50, 51);
        logistics.Update(1);
        Assert.True(machine.PathPosition > 0);
    }

    [Fact]
    public void RemovingOnlyAConnectionInvalidatesTheTruckRouteEvenWhenEveryTileRemains()
    {
        var map = Flat();
        map.BuildRoadTilePath(34, 66);
        map.BuildRoadTilePath(35, 67);
        map.BuildRoadTilePath(50, 51);
        var cargo = new TimberCargoSystem(); cargo.AddHarvested(25);
        var system = new VehicleSystem(cargo, path => VehicleRoadRoute.Create(map, path)) { UseCargoStops = false };
        system.RouteValidator = vehicle => vehicle.Route.Zip(vehicle.Route.Skip(1), map.AreNetworkNeighbours).All(connected => connected);
        system.Spawn(map.FindNetworkPath(34, 51));
        map.RemoveRoadTilePath(50, 51);
        Assert.True(map.IsRoadTile(50));
        Assert.True(map.IsRoadTile(51));
        Assert.Equal(1, system.RemoveInvalidRoutes(map.IsNetworkTile));
        Assert.Equal(25, cargo.Available);
    }

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
    [Fact]
    public void DrawingInterpolatesAcrossTheWholeTickNotJustItsLastSubstep()
    {
        var map = Flat();
        int[] route = Enumerable.Range(2, 13).Select(u => u * 16 + 2).ToArray();
        map.BuildRoadTilePath(route[0], route[^1], RoadPaving.Asphalt);
        var system = new VehicleSystem(new TimberCargoSystem(), r => VehicleRoadRoute.Create(map, r)) { TimeScale = 4, UseCargoStops = false };
        var truck = system.Spawn(route);
        for (int i = 0; i < 60; i++) system.Update(1.0 / 30);
        double before = truck.RoutePosition;
        system.Update(1.0 / 30);                       // four vehicle substeps
        Assert.Equal(before, truck.PreviousRoutePosition, 9);
        Assert.True(truck.RoutePosition - before > 0.05, "The truck did not move during the tick.");
    }
}
