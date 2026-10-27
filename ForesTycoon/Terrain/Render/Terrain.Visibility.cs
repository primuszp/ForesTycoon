using System.Collections.Generic;

namespace ForesTycoon
{
    partial class Terrain
    {
        private readonly record struct VisibilityView(float Tilt, float Yaw, double MinX, double MinY, double MaxX, double MaxY);
        private sealed class VisibleRegion
        {
            internal VisibilityView View;
            internal bool Ready;
            internal readonly List<Tile> Tiles = new();
            internal readonly List<TerrainChunk> Chunks = new();
            internal readonly HashSet<TerrainChunk> ChunkSet = new();
        }

        // The main view and its expanded shadow footprint alternate in every lit frame.
        // Keep both selections; their tile references stay live through terrain edits.
        // Chunk bounds cover the map's full allowed height range and never change.
        private VisibleRegion visibleRegion = new(), previousRegion = new();
        private List<Tile> visibleTiles => visibleRegion.Tiles;
        private List<TerrainChunk> visibleChunks => visibleRegion.Chunks;
        private HashSet<TerrainChunk> visibleChunkSet => visibleRegion.ChunkSet;
        public int VisibleChunkCount => visibleChunks.Count;
        internal long VisibilitySelectionsBuilt { get; private set; }

        internal void UpdateVisibleTiles(RenderContext context)
        {
            var key = new VisibilityView(context.CameraTilt, context.CameraYaw,
                context.ViewMinX, context.ViewMinY, context.ViewMaxX, context.ViewMaxY);
            if (visibleRegion.Ready && visibleRegion.View == key) return;
            (visibleRegion, previousRegion) = (previousRegion, visibleRegion);
            if (visibleRegion.Ready && visibleRegion.View == key) return;

            visibleTiles.Clear(); visibleChunks.Clear(); visibleChunkSet.Clear();
            var view = new IsometricView(key.Tilt, key.Yaw);
            const double margin = 12;
            foreach (TerrainChunk chunk in chunkIndex.Chunks)
            {
                view.ProjectBounds(chunk.Min, chunk.Max, out var min, out var max);
                if (max.X < key.MinX - margin || min.X > key.MaxX + margin
                    || max.Y < key.MinY - margin || min.Y > key.MaxY + margin) continue;
                visibleChunks.Add(chunk); visibleChunkSet.Add(chunk);
                foreach (int id in chunk.TileIds) visibleTiles.Add(tiles[id]);
            }
            visibleRegion.View = key; visibleRegion.Ready = true; VisibilitySelectionsBuilt++;
        }
    }
}
