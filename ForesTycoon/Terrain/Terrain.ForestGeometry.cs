using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private sealed class ForestChunkGeometry : IDisposable
        {
            internal readonly ForestChunkVisualState State;
            internal readonly VertexBuffer Wood = new VertexBuffer(PrimitiveType.Triangles);
            internal readonly VertexBuffer Crowns = new VertexBuffer(PrimitiveType.Triangles);
            internal readonly VertexBuffer Floor = new VertexBuffer(PrimitiveType.Triangles);
            internal readonly VertexBuffer Understory = new VertexBuffer(PrimitiveType.Triangles);
            internal ForestLod? Lod;
            internal ulong TerrainVersion;
            internal ulong EditRevision;
            internal bool Dirty, Queued;
            internal ForestChunkGeometry(int count) => State = new ForestChunkVisualState(count);
            public void Dispose() { Wood.Dispose(); Crowns.Dispose(); Floor.Dispose(); Understory.Dispose(); }
        }

        private readonly Dictionary<TerrainChunk, ForestChunkGeometry> forestGeometry = new();
        private readonly Dictionary<(TerrainChunk, ForestLod), ForestChunkGeometry> forestLevels = new();
        private ForestLod? forestLod;

        internal void WarmForestGeometry(ForestSystem forest)
        {
            foreach (TerrainChunk chunk in chunkIndex.Chunks)
                foreach (ForestLod lod in Enum.GetValues<ForestLod>()) GetForestGeometry(chunk, forest, lod);
            ForestChunkRebuilds = 0;
        }

        private ForestChunkGeometry GetForestGeometry(TerrainChunk chunk, ForestSystem forest, ForestLod lod)
        {
            var key = (chunk, lod);
            if (!forestLevels.TryGetValue(key, out ForestChunkGeometry geometry))
            {
                geometry = new ForestChunkGeometry(chunk.TileIds.Length);
                forestLevels.Add(key, geometry);
            }
            bool changed = geometry.State.Refresh(forest, chunk.TileIds);
            geometry.Dirty |= changed;
            if (geometry.Lod != lod || geometry.TerrainVersion != forestTerrainVersion || (changed && geometry.EditRevision != forest.EditRevision))
            {
                BuildForestGeometry(chunk, geometry, lod);
                geometry.Lod = lod;
                geometry.TerrainVersion = forestTerrainVersion;
                geometry.EditRevision = forest.EditRevision;
                geometry.Dirty = false;
                ForestChunkRebuilds++;
            }
            geometry.EditRevision = forest.EditRevision;
            if (geometry.Dirty && !geometry.Queued)
            {
                geometry.Queued = true;
                forestBuildQueue.Enqueue((chunk, geometry, lod));
            }
            return geometry;
        }
        private readonly ForestMaterial forestMaterial = new ForestMaterial();
        private readonly List<Vertex> crownVertices = new List<Vertex>();
        internal int ForestChunkRebuilds { get; private set; }

        internal void DrawTrees(ForestSystem forest, RenderContext context)
        {
            ForestChunkRebuilds = 0;
            ForestLod lod = ForestLodPolicy.Select(context.PixelsPerWorldUnit, forestLod);
            forestLod = lod;
            foreach (TerrainChunk chunk in visibleChunks)
            {
                forestGeometry[chunk] = GetForestGeometry(chunk, forest, lod);
            }
            ProcessForestBuildQueue(forest);
            using (new RenderStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-1, -1))
                foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Floor.DrawArray();
            foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Wood.DrawArray();
            foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Understory.DrawArray();
            forestMaterial.Use();
            foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Crowns.DrawArray(false);
            if (lod != ForestLod.Far)
            {
                bool culling = GL.IsEnabled(EnableCap.CullFace);
                GL.GetInteger(GetPName.CullFaceMode, out int oldCull);
                try
                {
                    GL.Enable(EnableCap.CullFace);
                    GL.CullFace(TriangleFace.Front);
                    forestMaterial.Use((lod == ForestLod.Near ? 0.75f : 0.45f) / context.PixelsPerWorldUnit);
                    foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Crowns.DrawArray(false);
                }
                finally
                {
                    GL.CullFace((TriangleFace)oldCull);
                    if (!culling) GL.Disable(EnableCap.CullFace);
                    forestMaterial.Use();
                }
            }
        }

        private void PrepareForestStems(TerrainChunk chunk, ForestChunkGeometry geometry, ForestLod lod)
        {
            chunkStems.Clear();
            chunkStands.Clear();
            Span<TreeInstance> stems = stackalloc TreeInstance[MaximumStemsPerTile];
            for (int i = 0; i < chunk.TileIds.Length; i++)
            {
                Tile tile = tiles[chunk.TileIds[i]];
                ForestVisualState state = geometry.State.Tiles[i];
                int count = BuildStems(state.Stand, state.CanopyPressure, tile, stems, lod);
                if (count == 0) continue;
                chunkStands.Add((tile, chunkStems.Count, count));
                for (int stem = 0; stem < count; stem++) chunkStems.Add(stems[stem]);
            }
        }

        private void BuildForestGeometry(TerrainChunk chunk, ForestChunkGeometry geometry, ForestLod lod)
        {
            PrepareForestStems(chunk, geometry, lod);
            geometry.Wood.SetData(DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads, () =>
            {
                if (lod == ForestLod.Far) return;
                foreach (TreeInstance stem in chunkStems) DrawTreeWood(stem);
            }), false);
            crownVertices.Clear();
            foreach (TreeInstance tree in chunkStems)
            {
                TreeModel model = TreeModel.For(tree.Stand.Species);
                float height = model.CrownHeight * tree.Scale * tree.CrownRise;
                ForestCrownMesh.Append(crownVertices, tree.Stand.Species,
                    new Vector3(tree.X, tree.Y, tree.TrunkTop(model) - height * model.CrownDrop),
                    model.CrownRadius * tree.Scale * tree.CrownWidth, height, tree.Yaw, tree.Seed,
                    Weather(Tinted(model.CrownColor, tree.Tint), tree.Stand.Health), lod);
            }
            geometry.Crowns.SetData(crownVertices.ToArray(), false);
            geometry.Understory.SetData(DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Triangles, () =>
            {
                if (lod != ForestLod.Near) return;
                foreach (var stand in chunkStands)
                    DrawUndergrowth(stand.Tile, chunkStems[stand.Offset], stand.Count);
            }), false);
            geometry.Floor.SetData(DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Triangles, () =>
            {
                foreach (TreeInstance tree in chunkStems) DrawForestFloor(tree);
            }), false);
        }

        private void DrawForestFloor(TreeInstance tree)
        {
            float radius = TreeModel.For(tree.Stand.Species).CrownRadius * tree.Scale * 1.35f;
            const int sides = 12;
            for (int side = 0; side < sides; side++)
            {
                Point(tree.X, tree.Y, 65);
                float a = MathF.Tau * side / sides, b = MathF.Tau * (side + 1) / sides;
                Point(tree.X + MathF.Cos(a) * radius, tree.Y + MathF.Sin(a) * radius, 0);
                Point(tree.X + MathF.Cos(b) * radius, tree.Y + MathF.Sin(b) * radius, 0);
            }
            void Point(float x, float y, int alpha)
            {
                int u = Math.Clamp((int)MathF.Floor((x + offsetX) / tileSizeH), 0, settings.TileColumns - 1);
                int v = Math.Clamp((int)MathF.Floor((y + offsetY) / tileSizeV), 0, settings.TileRows - 1);
                Tile tile = getTileByCoords(u, v);
                SurfacePoint(tile, Math.Clamp((x - tile.W.xPos) / tileSizeH, 0, 1),
                    Math.Clamp((y - tile.W.yPos) / tileSizeV, 0, 1), out float px, out float py, out float pz);
                DynamicPrimitiveBatch.Color4(Color.FromArgb(alpha, 31, 48, 18));
                DynamicPrimitiveBatch.Vertex3(px, py, pz + 0.015f);
            }
        }

        private void DisposeForestGeometry()
        {
            foreach (ForestChunkGeometry geometry in forestLevels.Values) geometry.Dispose();
            forestLevels.Clear();
            forestBuildQueue.Clear();
            pendingForestBuild = null;
            forestGeometry.Clear();
            forestMaterial.Dispose();
            crownVertices.Clear();
            chunkStems.Clear();
            chunkStands.Clear();
        }
    }
}
