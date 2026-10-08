using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    [Flags]
    enum ChunkDirtyFlags
    {
        None = 0,
        Terrain = 1 << 0,
        Water = 1 << 1,
        Roads = 1 << 2,
        Foundations = 1 << 3,
        Props = 1 << 4,
        All = Terrain | Water | Roads | Foundations | Props
    }

    sealed class TerrainChunk
    {
        public TerrainChunk(int chunkX, int chunkY, int[] tileIds, Vector3 min, Vector3 max)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            TileIds = tileIds;
            Min = min;
            Max = max;
        }

        public int ChunkX { get; }
        public int ChunkY { get; }
        public int[] TileIds { get; }
        public Vector3 Min { get; }
        public Vector3 Max { get; }
        public ChunkDirtyFlags DirtyFlags { get; private set; } = ChunkDirtyFlags.All;
        // Monotonic revisions survive another renderer clearing the dirty flags.
        public ulong PropVersion { get; private set; }
        public ulong SurfaceVersion { get; private set; } = 1;
        public void MarkDirty(ChunkDirtyFlags flags)
        {
            DirtyFlags |= flags;
            if ((flags & (ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Water | ChunkDirtyFlags.Roads | ChunkDirtyFlags.Foundations)) != 0)
                SurfaceVersion++;
            if ((flags & (ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Props | ChunkDirtyFlags.Roads | ChunkDirtyFlags.Foundations)) != 0)
                PropVersion++;
        }
        public void ClearDirty(ChunkDirtyFlags flags) => DirtyFlags &= ~flags;
    }

    sealed class TerrainChunkIndex
    {
        public const int DefaultChunkSize = 16;
        private readonly TerrainData data;
        private readonly TerrainChunk[] chunks;

        public TerrainChunkIndex(TerrainData data, int chunkSize = DefaultChunkSize)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
            ChunkSize = chunkSize;
            ChunkColumns = (data.Settings.TileColumns + chunkSize - 1) / chunkSize;
            ChunkRows = (data.Settings.TileRows + chunkSize - 1) / chunkSize;
            chunks = new TerrainChunk[ChunkColumns * ChunkRows];
            Build();
        }

        public int ChunkSize { get; }
        public int ChunkColumns { get; }
        public int ChunkRows { get; }
        public IReadOnlyList<TerrainChunk> Chunks => chunks;

        public TerrainChunk GetByTile(int tileId)
        {
            int tilesPerColumn = data.NodeRows - 1;
            int u = tileId / tilesPerColumn;
            int v = tileId % tilesPerColumn;
            return chunks[(u / ChunkSize) * ChunkRows + v / ChunkSize];
        }

        public void MarkTileDirty(int tileId, ChunkDirtyFlags flags) => GetByTile(tileId).MarkDirty(flags);

        public void MarkTileAndNeighboursDirty(int tileId, ChunkDirtyFlags flags)
        {
            int tileRows = data.NodeRows - 1;
            int u = tileId / tileRows, v = tileId % tileRows;
            for (int du = -1; du <= 1; du++)
                for (int dv = -1; dv <= 1; dv++)
                    if (data.CheckTile(u + du, v + dv))
                        GetByTile(data.GetTile(u + du, v + dv).Id).MarkDirty(flags);
        }

        private void Build()
        {
            int tileRows = data.Settings.TileRows;
            for (int cx = 0; cx < ChunkColumns; cx++)
                for (int cy = 0; cy < ChunkRows; cy++)
                {
                    int startU = cx * ChunkSize, startV = cy * ChunkSize;
                    int endU = Math.Min(startU + ChunkSize, data.Settings.TileColumns);
                    int endV = Math.Min(startV + ChunkSize, data.Settings.TileRows);
                    List<int> ids = new List<int>((endU - startU) * (endV - startV));
                    for (int u = startU; u < endU; u++)
                        for (int v = startV; v < endV; v++) ids.Add(u * tileRows + v);

                    Node minNode = data.GetNode(startU, startV);
                    Node maxNode = data.GetNode(endU, endV);
                    Vector3 min = new Vector3(minNode.xPos, minNode.yPos, 0f);
                    Vector3 max = new Vector3(maxNode.xPos, maxNode.yPos, data.Settings.MaxHeight * data.TileSizeM);
                    chunks[cx * ChunkRows + cy] = new TerrainChunk(cx, cy, ids.ToArray(), min, max);
                }
        }
    }
}
