using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private sealed class StaticTerrainGeometry : IDisposable
        {
            internal readonly VertexBuffer Land = new VertexBuffer(PrimitiveType.Triangles);
            internal readonly VertexBuffer Grid = new VertexBuffer(PrimitiveType.Lines);
            internal ulong Version;
            public void Dispose() { Land.Dispose(); Grid.Dispose(); }
        }
        private readonly Dictionary<TerrainChunk, StaticTerrainGeometry> staticTerrain = new();
        private readonly List<Vertex> staticTerrainScratch = new();
        internal int StaticTerrainRebuilds { get; private set; }

        internal void WarmStaticGeometry() => DrawCachedTerrain(chunkIndex.Chunks, false);
        private void DrawCachedTerrain(IReadOnlyList<TerrainChunk> chunks = null, bool draw = true)
        {
            StaticTerrainRebuilds = 0;
            foreach (TerrainChunk chunk in visibleChunks)
            {
                if (!staticTerrain.TryGetValue(chunk, out var geometry))
                {
                    geometry = new StaticTerrainGeometry();
                    staticTerrain.Add(chunk, geometry);
                }
                if (geometry.Version != surfaceVisualVersion)
                {
                    staticTerrainScratch.Clear();
                    staticTerrainScratch.AddRange(DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads, () => {
                        foreach (int id in chunk.TileIds)
                        {
                            Tile tile = tiles[id];
                            if (!roads.Has(id) && tile.Shape.IsPlanar && GetTileRenderMaterial(tile) == TileRenderMaterial.Grass)
                                DrawTerrainTileQuad(tile);
                        }
                    }));
                    foreach (int id in chunk.TileIds)
                    {
                        Tile tile = tiles[id];
                        if (roads.Has(id) || tile.Shape.IsPlanar || GetTileRenderMaterial(tile) != TileRenderMaterial.Grass) continue;
                        bool flip = flippedDiagonalTiles.Contains(id);
                        var mesh = vbos[tile.Code + "_" + tile.Low + (flip ? "_f" : "")];
                        Vector3 offset = new Vector3(tile.W.xPos, tile.W.yPos, tile.LowPos);
                        foreach (Vertex vertex in mesh.CpuVertices)
                            staticTerrainScratch.Add(new Vertex(vertex.Position + offset, vertex.Normal, vertex.Color));
                    }
                    geometry.Land.SetData(staticTerrainScratch.ToArray());
                    geometry.Grid.SetData(DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Lines, () => {
                        foreach (int id in chunk.TileIds)
                            if (!roads.Has(id) && !ShouldDrawStandingWater(tiles[id]) && !CanRenderFallbackRiver(tiles[id])) DrawTileGrid(tiles[id], Color.FromArgb(82, 115, 38));
                    }));
                    geometry.Version = surfaceVisualVersion;
                    StaticTerrainRebuilds++;
                }
                if (draw) geometry.Land.DrawArray();
            }
        }
        private void DrawCachedGrid()
        {
            foreach (TerrainChunk chunk in visibleChunks) staticTerrain[chunk].Grid.DrawArray();
        }
        internal bool CachedGridHasAllTileBoundaries()
        {
            foreach (var pair in staticTerrain)
            {
                int expected = 0;
                foreach (int id in pair.Key.TileIds)
                    if (!roads.Has(id) && !ShouldDrawStandingWater(tiles[id]) && !CanRenderFallbackRiver(tiles[id])) expected += 8;
                if (pair.Value.Grid.CpuVertices.Length != expected) return false;
            }
            return true;
        }
        private void DisposeStaticTerrain()
        {
            foreach (var geometry in staticTerrain.Values) geometry.Dispose();
            staticTerrain.Clear();
        }
    }
}
