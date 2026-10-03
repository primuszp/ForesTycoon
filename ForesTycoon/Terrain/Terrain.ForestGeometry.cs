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
            foreach (TerrainChunk chunk in chunkIndex.Chunks) GetForestGeometry(chunk,forest,ForestLod.Near);
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

        internal void DrawTrees(ForestSystem forest, RenderContext context, bool processBuildQueue = true)
        {
            ForestChunkRebuilds = 0;
            forestLod=ForestLod.Near;
            DrawTreeLevel(forest,context,ForestLod.Near,processBuildQueue);
        }

        private void DrawTreeLevel(ForestSystem forest,RenderContext context,ForestLod lod,bool processBuildQueue)
        {
            foreach (TerrainChunk chunk in visibleChunks)
            {
                forestGeometry[chunk] = GetForestGeometry(chunk, forest, lod);
            }
            if (processBuildQueue) ProcessForestBuildQueue(forest);
            bool shadow = RenderDevice.Visuals?.ShadowPass == true;
            if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.ForestFloor;
            if (!shadow)
            using (new RenderStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-1, -1))
                foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Floor.DrawArray();
            if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.Wood;
            foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Wood.DrawArray();
            if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.Foliage;
            foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Understory.DrawArray();
            forestMaterial.Use();
            foreach (TerrainChunk chunk in visibleChunks) forestGeometry[chunk].Crowns.DrawArray(false);
            if (lod != ForestLod.Far && !shadow)
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

        private readonly List<(TreeInstance Stem, float Decay)> chunkStumps = new();

        private void PrepareForestStems(TerrainChunk chunk, ForestChunkGeometry geometry, ForestLod lod)
        {
            chunkStems.Clear();
            chunkStands.Clear();
            chunkStumps.Clear();
            Span<TreeInstance> stems = stackalloc TreeInstance[MaximumStemsPerTile];
            for (int i = 0; i < chunk.TileIds.Length; i++)
            {
                Tile tile = tiles[chunk.TileIds[i]];
                ForestVisualState state = geometry.State.Tiles[i];
                ForestStumpVisualState stump = geometry.State.Stumps[i];
                if (!stump.IsEmpty && lod != ForestLod.Far && !roads.Has(tile.Id))
                {
                    int stumpCount = BuildStumps(stump, state, tile, stems);
                    for (int s = 0; s < stumpCount; s++) chunkStumps.Add((stems[s], stump.DecayFraction));
                }
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
                foreach (var stump in chunkStumps) DrawStump(stump.Stem, stump.Decay);
            }), false);
            crownVertices.Clear();
            foreach (TreeInstance tree in chunkStems)
            {
                AppendTreeCrown(crownVertices, tree, lod);
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

        private static void AppendTreeCrown(List<Vertex> vertices, in TreeInstance tree, ForestLod lod)
        {
            TreeModel model = TreeModel.For(tree.Stand.Species);
            float height = model.CrownHeight * tree.Scale * tree.CrownRise;
            ForestCrownMesh.Append(vertices, tree.Stand.Species,
                new Vector3(tree.X, tree.Y, tree.TrunkTop(model) - height * model.CrownDrop),
                model.CrownRadius * tree.Scale * tree.CrownWidth, height, tree.Yaw, tree.Seed,
                Color.FromArgb(SurfaceSpeciesCode(tree.Stand.Species), Weather(Tinted(model.CrownColor, tree.Tint), tree.Stand.Health)), lod);
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
            chunkStumps.Clear();
        }
    }
}
