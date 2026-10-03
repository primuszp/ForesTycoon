namespace ForesTycoon.Tests;

public class TerrainChunkIndexTests
{
    [Fact]
    public void Index_PartitionsTilesWithoutDuplicates()
    {
        TerrainData data = new TerrainData(CreateSettings(33));
        TerrainChunkIndex index = new TerrainChunkIndex(data, chunkSize: 16);

        int[] ids = index.Chunks.SelectMany(chunk => chunk.TileIds).ToArray();

        Assert.Equal(2, index.ChunkColumns);
        Assert.Equal(2, index.ChunkRows);
        Assert.Equal(data.Tiles.Length, ids.Length);
        Assert.Equal(data.Tiles.Length, ids.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, data.Tiles.Length), ids.OrderBy(id => id));
    }

    [Fact]
    public void DirtyFlag_IsLocalToOwningChunk()
    {
        TerrainData data = new TerrainData(CreateSettings(33));
        TerrainChunkIndex index = new TerrainChunkIndex(data, 16);
        foreach (TerrainChunk chunk in index.Chunks) chunk.ClearDirty(ChunkDirtyFlags.All);
        int tileId = data.GetTile(17, 3).Id;

        index.MarkTileDirty(tileId, ChunkDirtyFlags.Roads);

        TerrainChunk owner = index.GetByTile(tileId);
        Assert.Equal(1, index.Chunks.Count(chunk => chunk.DirtyFlags != ChunkDirtyFlags.None));
        Assert.Equal(ChunkDirtyFlags.Roads, owner.DirtyFlags);
        Assert.Equal(1, owner.ChunkX);
        Assert.Equal(0, owner.ChunkY);
    }

    [Fact]
    public void Index_Supports512By512TileWorld()
    {
        TerrainData data = new TerrainData(CreateSettings(513));
        TerrainChunkIndex index = new TerrainChunkIndex(data, 16);

        Assert.Equal(32, index.ChunkColumns);
        Assert.Equal(32, index.ChunkRows);
        Assert.Equal(1024, index.Chunks.Count);
        Assert.Equal(512 * 512, index.Chunks.Sum(chunk => chunk.TileIds.Length));
    }

    [Theory]
    [InlineData(ChunkDirtyFlags.Terrain)]
    [InlineData(ChunkDirtyFlags.Props)]
    [InlineData(ChunkDirtyFlags.Roads)]
    [InlineData(ChunkDirtyFlags.Foundations)]
    internal void PropRevisionSurvivesConsumptionByOtherRenderCaches(ChunkDirtyFlags change)
    {
        var data = new TerrainData(CreateSettings(33));
        var index = new TerrainChunkIndex(data);
        int id = data.GetTile(4, 4).Id;
        var owner = index.GetByTile(id);
        ulong original = owner.PropVersion;
        index.MarkTileDirty(id, change);
        owner.ClearDirty(ChunkDirtyFlags.All);
        Assert.True(owner.PropVersion > original);
        Assert.All(index.Chunks.Where(c => c != owner), c => Assert.Equal(0UL, c.PropVersion));
        ulong edited = owner.PropVersion;
        index.MarkTileDirty(id, ChunkDirtyFlags.Water);
        Assert.Equal(edited, owner.PropVersion);
    }

    [Fact]
    public void ContactShadowInvalidationCrossesOnlyAdjacentChunkBoundaries()
    {
        var data = new TerrainData(CreateSettings(65));
        var index = new TerrainChunkIndex(data);
        index.MarkTileAndNeighboursDirty(data.GetTile(15, 15).Id, ChunkDirtyFlags.Props);
        var changed = index.Chunks.Where(c => c.PropVersion > 0).ToArray();
        Assert.Equal(4, changed.Length);
        Assert.All(changed, c => { Assert.InRange(c.ChunkX, 0, 1); Assert.InRange(c.ChunkY, 0, 1); });
    }

    private static TerrainSettings CreateSettings(int nodes) => new TerrainSettings(
        nodes, nodes, 5, 5, 2, 0.04f, 0.55f, 3f, seed: 42, maxHeight: 6);
}
