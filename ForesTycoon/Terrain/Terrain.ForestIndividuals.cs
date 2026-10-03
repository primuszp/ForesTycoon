using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        // A physical metre is intentionally compressed for the existing diorama proportions.
        internal const float TreeMetresToWorld = 0.32f;
        private sealed class IndividualForestChunk : IDisposable
        {
            internal readonly VertexBuffer Wood = new(PrimitiveType.Triangles);
            internal readonly VertexBuffer Crowns = new(PrimitiveType.Triangles);
            internal readonly VertexBuffer Floor = new(PrimitiveType.Triangles);
            internal readonly ulong[] TileRevisions;
            internal ulong TerrainVersion, Generation, ForestRevision;
            internal double AnchorYear;
            internal bool Initialized;
            internal ForestModelStyle ModelStyle;
            internal bool ImportedBirch;
            internal IndividualForestChunk(int count) => TileRevisions = new ulong[count];
            public void Dispose() { Wood.Dispose(); Crowns.Dispose(); Floor.Dispose(); }
        }

        private readonly Dictionary<TerrainChunk, IndividualForestChunk> individualForestChunks = new();
        private readonly List<Vertex> individualWood = new(), individualCrowns = new(), individualFloor = new();
        private readonly List<ForestVertexGrowth> individualWoodGrowth = new(), individualCrownGrowth = new(), individualFloorGrowth = new();
        private ImportedPineAsset importedPine;
        private readonly ImportedForestModels importedForestModels = new();
        private ForestLod? generatedForestLod;
        private readonly GraphicsSettings defaultPineGraphics = new() { Enhanced = false };

        private IndividualForestChunk GetIndividualForestChunk(TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics)
        {
            if (!individualForestChunks.TryGetValue(chunk, out var geometry))
                individualForestChunks.Add(chunk, geometry = new(chunk.TileIds.Length));
            bool changed = !geometry.Initialized || geometry.TerrainVersion != chunk.PropVersion
                || geometry.Generation != forest.IndividualTrees.Generation
                || geometry.ModelStyle != graphics.ForestModels || geometry.ImportedBirch != graphics.ImportedBirch;
            if (changed || geometry.ForestRevision != forest.Revision)
            {
                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    ulong revision = forest.IndividualTrees.TryGet(chunk.TileIds[i], out var patch) ? patch.Revision : 0;
                    changed |= geometry.TileRevisions[i] != revision;
                    geometry.TileRevisions[i] = revision;
                }
                geometry.ForestRevision = forest.Revision;
            }
            if (changed)
            {
                BuildIndividualForestChunk(chunk, geometry, forest, graphics);
                geometry.ModelStyle = graphics.ForestModels;
                geometry.ImportedBirch = graphics.ImportedBirch;
                geometry.Initialized = true;
                geometry.TerrainVersion = chunk.PropVersion;
                geometry.Generation = forest.IndividualTrees.Generation;
                ForestChunkRebuilds++;
            }
            float elapsed = (float)Math.Max(0, forest.ForestYear - geometry.AnchorYear);
            geometry.Wood.ForestElapsedYears = geometry.Crowns.ForestElapsedYears = geometry.Floor.ForestElapsedYears = elapsed;
            return geometry;
        }

        private static int CollectIndividualStems(ForestSystem forest, Tile tile, Span<TreeInstance> output)
        {
            if (!forest.IndividualTrees.TryGet(tile.Id, out var patch)) return 0;
            int count = Math.Min(patch.Count, output.Length);
            for (int i = 0; i < count; i++) output[i] = IndividualStem(tile, patch.Trees[i], forest.ForestYear);
            return count;
        }

        internal static TreeInstance IndividualStem(Tile tile, in ForestTree tree, double year)
        {
            ForestTreeDimensions size = tree.At(year);
            TreeModel model = TreeModel.For(tree.Species);
            float scale = size.Height * TreeMetresToWorld / model.TotalHeight;
            float width = size.CrownRadius * TreeMetresToWorld / (model.CrownRadius * scale);
            float crownFraction = tree.Species switch
            {
                ForestSpecies.Spruce => 0.86f, ForestSpecies.Oak => 0.70f,
                ForestSpecies.Birch => 0.65f, _ => 0.72f
            };
            float crownHeight = size.Height * crownFraction * TreeMetresToWorld;
            float rise = crownHeight / (model.CrownHeight * scale);
            float boleHeight = size.Height * TreeMetresToWorld - crownHeight * (1 - model.CrownDrop);
            // Diorama trunks remain legible; this mapping never changes physical timber volume.
            float trunkScale = size.Diameter * TreeMetresToWorld * 0.85f / model.TrunkRadius;
            SurfacePoint(tile, tree.U, tree.V, out float x, out float y, out float z);
            var stand = new ForestStand(tree.Species, tree.Age(year), ForestTree.Volume(size) / 100, tree.Health);
            return new(stand, unchecked((int)tree.Seed), x, y, z, scale,
                ForestTreeStore.Unit(ForestTreeStore.Random(tree.Seed + 11)) * MathF.Tau,
                ForestTreeStore.Unit(ForestTreeStore.Random(tree.Seed + 17)) - 0.5f,
                width, rise, DetailLevel(size.CrownRadius * TreeMetresToWorld), trunkScale, boleHeight / model.TrunkHeight);
        }

        private void BuildIndividualForestChunk(TerrainChunk chunk, IndividualForestChunk geometry, ForestSystem forest, GraphicsSettings graphics)
        {
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
            geometry.AnchorYear = forest.ForestYear;
            foreach (int id in chunk.TileIds)
            {
                if (roads.Has(id) || !forest.IndividualTrees.TryGet(id, out var patch)) continue;
                Tile tile = tiles[id];
                for (int i = 0; i < patch.Count; i++)
                {
                    ForestTree tree = patch.Trees[i];
                    TreeInstance stem = IndividualStem(tile, tree, geometry.AnchorYear);
                    var size = tree.At(geometry.AnchorYear);
                    float radial = tree.AnnualGrowth.Diameter / size.Diameter;
                    float vertical = tree.AnnualGrowth.Height / size.Height;
                    float crown = tree.AnnualGrowth.CrownRadius / size.CrownRadius;
                    var origin = new Vector3(stem.X, stem.Y, stem.BaseZ);
                    if (!ImportedForestModels.UsesImported(tree.Species, graphics))
                    {
                        Vertex[] wood = DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads, () => DrawTreeWood(stem));
                        individualWood.AddRange(wood);
                        Repeat(individualWoodGrowth, wood.Length, new(origin, new Vector3(radial, radial, vertical)));
                        int start = individualCrowns.Count;
                        AppendTreeCrown(individualCrowns, stem, ForestLod.Near);
                        Repeat(individualCrownGrowth, individualCrowns.Count - start, new(origin, new Vector3(crown, crown, vertical)));
                    }
                    Vertex[] floor = DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Triangles, () => DrawForestFloor(stem));
                    individualFloor.AddRange(floor);
                    Repeat(individualFloorGrowth, floor.Length, default); // Decals stay on the sampled terrain.
                }
                if (patch.Stumps == null) continue;
                foreach (var stump in patch.Stumps)
                {
                    TreeInstance stem = IndividualStem(tile, stump.Felled, stump.FelledYear);
                    Vertex[] wood = DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads,
                        () => DrawStump(stem, stump.Decay(geometry.AnchorYear)));
                    individualWood.AddRange(wood);
                    Repeat(individualWoodGrowth, wood.Length, default);
                }
            }
            Upload(geometry.Wood, individualWood, individualWoodGrowth);
            Upload(geometry.Crowns, individualCrowns, individualCrownGrowth);
            Upload(geometry.Floor, individualFloor, individualFloorGrowth);

            static void Repeat(List<ForestVertexGrowth> target, int count, ForestVertexGrowth value)
            {
                for (int i = 0; i < count; i++) target.Add(value);
            }
            static void Upload(VertexBuffer buffer, List<Vertex> vertices, List<ForestVertexGrowth> growth)
            {
                buffer.SetData(vertices.ToArray(), false);
                buffer.SetForestGrowth(growth.ToArray());
            }
        }

        private void DrawIndividualTrees(ForestSystem forest, RenderContext context, GraphicsSettings graphics)
        {
            graphics ??= defaultPineGraphics;
            generatedForestLod = ForestLodPolicy.Select(context.PixelsPerWorldUnit, generatedForestLod);
            foreach (var chunk in visibleChunks) GetIndividualForestChunk(chunk, forest, graphics);
            bool shadow = RenderDevice.Visuals?.ShadowPass == true;
            if (!shadow)
            {
                if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.ForestFloor;
                using (new RenderStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-1, -1))
                    foreach (var chunk in visibleChunks) individualForestChunks[chunk].Floor.DrawArray();
            }
            if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.Wood;
            foreach (var chunk in visibleChunks) individualForestChunks[chunk].Wood.DrawArray();
            forestMaterial.Use();
            foreach (var chunk in visibleChunks) individualForestChunks[chunk].Crowns.DrawArray(false);
            foreach (var chunk in visibleChunks)
                foreach (int id in chunk.TileIds)
                {
                    if (roads.Has(id) || !forest.IndividualTrees.TryGet(id, out var patch)) continue;
                    for (int i = 0; i < patch.Count; i++)
                    {
                        var tree = patch.Trees[i];
                        if (!ImportedForestModels.UsesImported(tree.Species, graphics)) continue;
                        var stem = IndividualStem(tiles[id], tree, forest.ForestYear);
                        if (tree.Species == ForestSpecies.Spruce && graphics.ForestModels == ForestModelStyle.OriginalPine)
                        {
                            importedPine ??= new ImportedPineAsset();
                            importedPine.Draw(tree, forest.ForestYear, stem, graphics);
                        }
                        else importedForestModels.Get(ImportedForestModels.Select(tree, graphics.ForestModels), generatedForestLod.Value)
                            .Draw(tree, forest.ForestYear, stem, graphics);
                    }
                }
            forestMaterial.Use();
            if (shadow) return;
            using (new RenderStateScope().Enable(EnableCap.CullFace))
            {
                GL.GetInteger(GetPName.CullFaceMode, out int oldCull);
                try
                {
                    GL.CullFace(TriangleFace.Front);
                    forestMaterial.Use(0.75f / context.PixelsPerWorldUnit);
                    foreach (var chunk in visibleChunks) individualForestChunks[chunk].Crowns.DrawArray(false);
                }
                finally { GL.CullFace((TriangleFace)oldCull); forestMaterial.Use(); }
            }
        }

        private void DisposeIndividualForest()
        {
            importedPine?.Dispose(); importedPine = null;
            importedForestModels.Dispose();
            foreach (var chunk in individualForestChunks.Values) chunk.Dispose();
            individualForestChunks.Clear();
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
        }
    }
}
