using System.Reflection;
using OpenTK.Mathematics;

public class TerrainMapTests
{
    private static TerrainMap Create() => new TerrainMap(TerrainSettings.Default);

    [Fact]
    public void TheMapBuildsAndAnswersGroundQueriesWithoutAGraphicsContext()
    {
        var map = Create();
        Assert.Equal(64 * 64, map.Tiles.Count);
        Assert.True(map.TryGetTileCenter(100, out var centre));
        Assert.True(map.TryGetSurfaceZ(centre.X, centre.Y, out _));
        Assert.True(map.TryRaycast(new Vector3(centre.X, centre.Y, 500), new Vector3(centre.X, centre.Y, -500), out var hit));
        Assert.InRange(hit.X, centre.X - 1, centre.X + 1);
        Assert.NotNull(map.HoveredTile);
    }

    [Fact]
    public void EditingRaisesTheTerrainAndTellsFollowersWhatChanged()
    {
        var map = Create();
        int changedNodes = 0, flushes = 0;
        map.NodesChanged += changed => changedNodes += changed.Count;
        map.EditsFlushed += () => flushes++;

        int nodeId = map.Tiles[300].W.Id;
        int before = map.Nodes[nodeId].W;
        map.EditElevationAtNode(nodeId, +1, 0, 1);

        Assert.Equal(before + 1, map.Nodes[nodeId].W);
        Assert.Equal(map.Nodes[nodeId].W * map.TileSizeM, map.Nodes[nodeId].zPos);
        Assert.True(changedNodes >= 1);
        Assert.True(flushes >= 1);
    }

    [Fact]
    public void RoadsFreezeTheirSurfaceAndInvalidateTheSurfaceClassification()
    {
        var map = Create();
        // Pick a flat stretch so the road is legal.
        int start = -1;
        for (int u = 5; u < 55 && start < 0; u++)
            for (int v = 5; v < 55; v++)
            {
                int id = u * 64 + v;
                if (Enumerable.Range(0, 6).All(i => map.IsRoadBuildable(map.Tiles[id + i * 64]))) { start = id; break; }
            }
        Assert.True(start >= 0, "no flat road site found");

        ulong version = map.SurfaceVersion;
        bool flipped = false;
        map.RoadDiagonalsChanged += () => flipped = true;
        map.BuildRoadTilePath(start, start + 5 * 64);

        Assert.True(map.RoadCount > 0);
        Assert.True(map.SurfaceVersion > version);
        Assert.True(flipped);
        Assert.True(map.IsRoadTile(start));
        Assert.True(map.TryGetRoadSurface(start, out _, out _));
        Assert.True(map.SurfaceCacheMatchesFreshCalculation());
    }

    [Fact]
    public void TheMapIsTheHabitatOfTheEcosystem()
    {
        var map = Create();
        var ecosystem = new Ecosystem(map);
        for (int i = 0; i < 30; i++) ecosystem.Update(1.0 / 30);
        Assert.True(ecosystem.Environment.Time > 0);
    }

    [Fact]
    public void TheMapAssemblyNeverTouchesGraphicsWindowingOrUi()
    {
        var assembly = typeof(TerrainMap).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => !n.StartsWith("System") && n != "netstandard" && n != "mscorlib").OrderBy(n => n);
        Assert.Contains("ForesTycoon.Ecology", references);
        Assert.All(references, n => Assert.Contains(n, new[] { "ForesTycoon.Ecology", "ForesTycoon.Engine", "OpenTK.Mathematics" }));
        Assert.All(assembly.GetTypes().Where(t => !t.FullName!.Contains('<')),
            t => Assert.StartsWith("ForesTycoon.Map", t.Namespace));
    }

    [Fact]
    public void HabitatQueriesForWildlifeFishAndWeatherLiveOnTheMap()
    {
        var map = Create();
        var forest = new ForestSystem(map);
        var spots = new List<WildlifeSpot>();
        map.CollectWildlifeSpots(forest, spots);
        Assert.All(spots, s => Assert.True(map.TryGetWildlifeDestination(s.TileId, out _)));

        var fish = new List<FishHabitat>();
        map.CollectFishHabitats(fish, node => map.Settings.SeaLevel);

        var heights = new float[map.Settings.TileColumns * map.Settings.TileRows];
        map.FillWeatherHeights(heights, forest);
        Assert.All(heights, h => Assert.True(h >= map.Settings.SeaLevel));
        map.GetWeatherBounds(out var min, out var max);
        Assert.True(max.X > min.X && max.Z > min.Z);
    }
}
