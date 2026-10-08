namespace ForesTycoon.Tests;

public class TerrainEditSimulationTests
{
    [Fact]
    public void RoadEditIsLocalAndRepeatingTheSamePathDoesNothing()
    {
        var map = Map();
        var ecosystem = EcosystemFor(map);
        ecosystem.Update(21.5);
        var forest = ecosystem.Forest;
        var before = forest.IndividualTrees.Patches.ToDictionary(p => p.Key,
            p => p.Value.Trees.Take(p.Value.Count).ToArray());
        double year = forest.ForestYear, time = ecosystem.Environment.Time;
        int[] changed = map.BuildRoadTilePath(132, 134);
        Assert.Equal(new[] { 132, 133, 134 }, changed);
        forest.RefreshHabitat(changed);
        ecosystem.Environment.RefreshRouting(changed);
        foreach (var entry in before)
            if (changed.Contains(entry.Key)) Assert.False(forest.IndividualTrees.TryGet(entry.Key, out _));
            else
            {
                Assert.True(forest.IndividualTrees.TryGet(entry.Key, out var patch));
                Assert.Equal(entry.Value, patch.Trees.Take(patch.Count));
            }
        Assert.Equal(year, forest.ForestYear);
        Assert.Equal(time, ecosystem.Environment.Time);
        ulong revision = forest.Revision, surface = map.SurfaceVersion;
        Assert.Empty(map.BuildRoadTilePath(132, 134));
        forest.RefreshHabitat(Array.Empty<int>());
        Assert.Equal(revision, forest.Revision);
        Assert.Equal(surface, map.SurfaceVersion);
        Assert.Equal(changed, map.RemoveRoadTilePath(132, 134));
        Assert.Empty(map.RemoveRoadTilePath(132, 134));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public void TerraformingOnlyDestroysVegetationOnActuallyChangedCells(int radius, int strength)
    {
        var map = Map();
        var ecosystem = EcosystemFor(map);
        ecosystem.Update(21.5); // Inside a growth interval, with a partially prepared next month.
        int nodeId = map.GetNode(5, 5).Id;
        var heights = map.Nodes.Select(n => n.W).ToArray();
        var forest = ecosystem.Forest;
        var snapshots = forest.IndividualTrees.Patches.ToDictionary(p => p.Key, p => p.Value.Trees.Take(p.Value.Count).ToArray());
        var cells = Enumerable.Range(0, ecosystem.Environment.CellCount).Select(ecosystem.Environment.Cell).ToArray();
        double year = forest.ForestYear, time = ecosystem.Environment.Time, water = ecosystem.Environment.StoredWater;
        ulong generation = forest.IndividualTrees.Generation, environmentRevision = ecosystem.Environment.Revision;
        int[] changed = map.EditElevationAtNode(nodeId, 1, radius, strength);
        var expected = map.Tiles.Where(t => new[] { t.W, t.S, t.E, t.N }.Any(n => heights[n.Id] != n.W)).Select(t => t.Id).ToArray();
        Assert.NotEmpty(changed);
        Assert.Contains(snapshots.Keys, changed.Contains);
        Assert.Equal(expected, changed);
        ecosystem.ApplyTerrainEdit(changed);
        foreach (var entry in snapshots)
        {
            if (changed.Contains(entry.Key)) Assert.False(forest.IndividualTrees.TryGet(entry.Key, out _));
            else
            {
                Assert.True(forest.IndividualTrees.TryGet(entry.Key, out var patch));
                Assert.Equal(entry.Value, patch.Trees.Take(patch.Count));
            }
        }
        Assert.Equal(year, forest.ForestYear);
        Assert.Equal(generation, forest.IndividualTrees.Generation);
        Assert.Equal(time, ecosystem.Environment.Time);
        Assert.Equal(environmentRevision, ecosystem.Environment.Revision);
        Assert.Equal(water, ecosystem.Environment.StoredWater);
        for (int id = 0; id < cells.Length; id++)
        {
            var after = ecosystem.Environment.Cell(id);
            Assert.Equal((cells[id].Canopy, cells[id].Surface, cells[id].Soil, cells[id].Deep),
                (after.Canopy, after.Surface, after.Soil, after.Deep));
            if (!changed.Contains(id)) Assert.Equal(cells[id], after);
        }
        Assert.InRange(Math.Abs(ecosystem.Environment.BalanceError), 0, 1e-6);
    }

    [Fact]
    public void TerrainEditDiscardsPlantationStumpsAndDepotWithoutHarvesting()
    {
        var ecosystem = new Ecosystem(Map());
        var forest = ecosystem.Forest;
        forest.Clear();
        Assert.Equal(ForestryActionResult.Planted, forest.Plant(4 * 32 + 4, ForestSpecies.Oak));
        Assert.Equal(ForestryActionResult.Harvested, forest.Harvest(4 * 32 + 4, out _));
        Assert.True(forest.IndividualTrees.TryGet(132, out var patch));
        Assert.NotEmpty(patch.Stumps);
        forest.ClearTerrainTiles(new[] { 132, 132 });
        Assert.False(forest.IndividualTrees.TryGet(132, out _));
        Assert.False(forest.TryGetPlantation(132, out _));
        Assert.Equal(0, forest.IndividualTreeCount);
        Assert.Equal(default, forest.Statistics);
    }

    [Fact]
    public void InvalidRemovalIsAtomicAndEmptyEditDoesNothing()
    {
        var map = Map();
        var ecosystem = EcosystemFor(map);
        int id = ecosystem.Forest.IndividualTrees.Patches.First().Key;
        ulong revision = ecosystem.Forest.Revision, surface = map.SurfaceVersion;
        Assert.Throws<ArgumentOutOfRangeException>(() => ecosystem.ApplyTerrainEdit(new[] { id, -1 }));
        Assert.True(ecosystem.Forest.IndividualTrees.TryGet(id, out _));
        Assert.Empty(map.EditElevationAtNode(-1, 1, 0, 1));
        Assert.Empty(map.EditElevationAtNode(map.GetNode(5, 5).Id, 0, 0, 1));
        ecosystem.ApplyTerrainEdit(Array.Empty<int>());
        Assert.Equal(revision, ecosystem.Forest.Revision);
        Assert.Equal(surface, map.SurfaceVersion);
    }

    [Fact]
    public void LocalRoutingProducesSameNextWaterStepAsFullRouting()
    {
        var firstMap = Map(); var secondMap = Map();
        var first = EcosystemFor(firstMap); var second = EcosystemFor(secondMap);
        first.Update(21.5); second.Update(21.5);
        first.ApplyTerrainEdit(firstMap.EditElevationAtNode(firstMap.GetNode(5, 5).Id, 1, 1, 3));
        second.ApplyTerrainEdit(secondMap.EditElevationAtNode(secondMap.GetNode(5, 5).Id, 1, 1, 3));
        second.Environment.RefreshRouting();
        first.Update(90); second.Update(90);
        Assert.Equal(first.Forest.ForestYear, second.Forest.ForestYear);
        Assert.Equal(first.Forest.Statistics, second.Forest.Statistics);
        for (int id = 0; id < first.Environment.CellCount; id++)
        {
            Assert.Equal(first.Environment.Cell(id), second.Environment.Cell(id));
            Assert.Equal(first.Forest.TryGetStand(id, out var a), second.Forest.TryGetStand(id, out var b));
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void SurfaceRevisionAndDirtyFlagsRemainLocalAcrossAnEdit()
    {
        var map = Map();
        var versions = map.Chunks.Chunks.Select(c => c.SurfaceVersion).ToArray();
        foreach (var chunk in map.Chunks.Chunks) chunk.ClearDirty(ChunkDirtyFlags.All);
        var changed = map.EditElevationAtNode(map.GetNode(5, 5).Id, 1, 0, 1);
        Assert.Equal(4, changed.Length);
        var owner = map.Chunks.GetByTile(changed[0]);
        for (int i = 0; i < versions.Length; i++)
        {
            var chunk = map.Chunks.Chunks[i];
            if (chunk == owner) Assert.True(chunk.SurfaceVersion > versions[i]);
            else
            {
                Assert.Equal(versions[i], chunk.SurfaceVersion);
                Assert.Equal(ChunkDirtyFlags.None, chunk.DirtyFlags);
            }
        }
    }

    private static TerrainMap Map() => new(TerrainSettings.Default.WithNodeSize(33, 42), (_, _) => 4);

    private static Ecosystem EcosystemFor(TerrainMap map)
    {
        var ecosystem = new Ecosystem(map);
        ecosystem.Forest.Clear();
        foreach (int id in new[] { 4 * 32 + 4, 5 * 32 + 5, 6 * 32 + 6, 15 * 32 + 15, 24 * 32 + 24 })
            Assert.Equal(ForestryActionResult.Planted, ecosystem.Forest.Plant(id, ForestSpecies.Oak));
        return ecosystem;
    }
}
