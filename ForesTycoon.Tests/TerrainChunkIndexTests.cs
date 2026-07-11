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

    private static TerrainSettings CreateSettings(int nodes) => new TerrainSettings(
        nodes, nodes, 5, 5, 2, 0.04f, 0.55f, 3f, seed: 42, maxHeight: 6);
}
