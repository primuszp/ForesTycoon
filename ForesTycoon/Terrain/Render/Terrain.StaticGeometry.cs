using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private sealed class StaticTerrainGeometry : IDisposable
        {
            internal readonly VertexBuffer Land = new VertexBuffer(PrimitiveTopology.Triangles);
            internal readonly VertexBuffer Grid = new VertexBuffer(PrimitiveTopology.Lines);
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
            foreach (TerrainChunk chunk in chunks ?? visibleChunks)
            {
                if (!staticTerrain.TryGetValue(chunk, out var geometry))
                {
                    geometry = new StaticTerrainGeometry();
                    staticTerrain.Add(chunk, geometry);
                }
                if (geometry.Version != chunk.SurfaceVersion)
                {
                    staticTerrainScratch.Clear();
                    staticTerrainScratch.AddRange(DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Quads, () => {
                        foreach (int id in chunk.TileIds)
                        {
                            Tile tile = tiles[id];
                            if (!roads.Has(id) && tile.Shape.IsPlanar && GetTileSurfaceMaterial(tile) == TileSurfaceMaterial.Grass)
                                DrawTerrainTileQuad(tile);
                        }
                    }));
                    foreach (int id in chunk.TileIds)
                    {
                        Tile tile = tiles[id];
                        if (roads.Has(id) || tile.Shape.IsPlanar || GetTileSurfaceMaterial(tile) != TileSurfaceMaterial.Grass) continue;
                        bool flip = flippedDiagonalTiles.Contains(id);
                        var mesh = vbos[tile.Code + "_" + tile.Low + (flip ? "_f" : "")];
                        Vector3 offset = new Vector3(tile.W.xPos, tile.W.yPos, tile.LowPos);
                        foreach (Vertex vertex in mesh.CpuVertices)
                            staticTerrainScratch.Add(new Vertex(vertex.Position + offset, vertex.Normal, vertex.Color));
                    }
                    geometry.Land.SetData(staticTerrainScratch.ToArray(), retainCpuCopy: false);
                    geometry.Grid.SetData(DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Lines, () => {
                        foreach (int id in chunk.TileIds)
                            if (!roads.Has(id) && !ShouldDrawStandingWater(tiles[id]) && !CanRenderFallbackRiver(tiles[id])) DrawTileGrid(tiles[id], GridLineColor);
                    }));
                    geometry.Version = chunk.SurfaceVersion;
                    StaticTerrainRebuilds++;
                }
                if (draw) geometry.Land.DrawArray();
            }
        }
        // Dark moss line at partial opacity: it reads on both flat colour and textured grass.
        private static readonly Color GridLineColor = Color.FromArgb(115, 34, 52, 18);

        /// <summary>
        /// Screen-space ribbons keep a 1.6-pixel core at every zoom and camera angle.
        /// The shader supplies a soft coverage fringe and a small depth bias.
        /// </summary>
        private void DrawCachedGrid()
        {
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false);
            RenderDevice.UseScreenLineShader(1.6f);
            foreach (TerrainChunk chunk in visibleChunks) staticTerrain[chunk].Grid.DrawArray(useGeometryShader: false);
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
