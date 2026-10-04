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
            foreach (TerrainChunk chunk in chunks ?? visibleChunks)
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
                            if (!roads.Has(id) && !ShouldDrawStandingWater(tiles[id]) && !CanRenderFallbackRiver(tiles[id])) DrawTileGrid(tiles[id], GridLineColor);
                    }));
                    geometry.Version = surfaceVisualVersion;
                    StaticTerrainRebuilds++;
                }
                if (draw) geometry.Land.DrawArray();
            }
        }
        // Dark moss line at partial opacity: it reads on both flat colour and textured grass,
        // and the overlapping passes below make the centre of the line the darkest part.
        // Dark moss line at partial opacity: it reads on both flat colour and textured grass.
        private static readonly Color GridLineColor = Color.FromArgb(115, 34, 52, 18);

        /// <summary>
        /// Core-profile OpenGL has no wide lines, so the cached 1-pixel line set is drawn
        /// twice, shifted a quarter pixel either way. Multisampling turns the overlap into a
        /// soft line of about one and a half pixels, without touching the line geometry.
        /// </summary>
        private void DrawCachedGrid(float pixelsPerWorldUnit = 0f)
        {
            int[] viewport = new int[4];
            GL.GetInteger(GetPName.Viewport, viewport);
            Matrix4 camera = RenderDevice.ViewProjection;
            float pixelX = 2f / Math.Max(1, viewport[2]), pixelY = 2f / Math.Max(1, viewport[3]);
            // Very distant views keep a single pass so the grid never swallows the terrain.
            bool distant = pixelsPerWorldUnit > 0f && pixelsPerWorldUnit < 4f;
            try
            {
                using var state = new RenderStateScope().AlphaBlend();
                for (int pass = 0; pass < (distant ? 1 : 2); pass++)
                {
                    float shift = distant ? 0f : (pass == 0 ? -0.25f : 0.25f);
                    RenderDevice.SetViewProjection(camera * Matrix4.CreateTranslation(shift * pixelX, shift * pixelY, 0));
                    foreach (TerrainChunk chunk in visibleChunks) staticTerrain[chunk].Grid.DrawArray();
                }
            }
            finally { RenderDevice.SetViewProjection(camera); }
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
