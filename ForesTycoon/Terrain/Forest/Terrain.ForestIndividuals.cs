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
        internal const float TreeMetresToWorld = TreeScale.MetresToWorld;
        private readonly record struct ForestGpuTree(int TileId, int Index, ulong Id, ForestTreeDimensions Size, int ShapeKey, TreeSite Site);
        private sealed class IndividualForestChunk : IDisposable
        {
            internal readonly VertexBuffer Wood = new(PrimitiveType.Triangles);
            internal readonly VertexBuffer Crowns = new(PrimitiveType.Triangles);
            internal readonly VertexBuffer Floor = new(PrimitiveType.Triangles);
            internal readonly ulong[] TileRevisions;
            internal readonly List<ForestGpuTree> Trees = new();
            internal Vector4[] State = Array.Empty<Vector4>();
            internal int StateBuffer, StateTexture;
            internal double GrowthYear;
            internal ulong TerrainVersion, Generation, ForestRevision, EditRevision;
            internal double AnchorYear;
            internal double NextStageYear;
            internal bool Initialized;
            internal bool LightShapeDirty;
            internal ForestModelStyle ModelStyle;
            internal bool ImportedBirch;
            internal IndividualForestChunk(int count) => TileRevisions = new ulong[count];
            public void Dispose() {
                Wood.Dispose(); Crowns.Dispose(); Floor.Dispose();
                if (StateTexture != 0) GL.DeleteTexture(StateTexture);
                if (StateBuffer != 0) GL.DeleteBuffer(StateBuffer);
            }
        }

        private readonly Dictionary<(TerrainChunk, ForestLod), IndividualForestChunk> individualForestChunks = new();
        private readonly List<Vertex> individualWood = new(), individualCrowns = new(), individualFloor = new();
        private readonly List<ForestVertexGrowth> individualWoodGrowth = new(), individualCrownGrowth = new(), individualFloorGrowth = new();
        private sealed class ForestBuild : IDisposable
        {
            internal TerrainChunk Chunk;
            internal ForestLod Lod;
            internal IndividualForestChunk Geometry;
            internal IEnumerator<bool> Work;
            public void Dispose() { Work.Dispose(); Geometry.Dispose(); }
        }
        private ForestBuild pendingForestBuild;
        // Explicit diagnostic mode for pixel assertions at an exact life-stage boundary.
        internal bool SynchronousForestBuilds { get; set; }
        private ImportedPineAsset importedPine;
        private readonly ImportedForestModels importedForestModels = new();
        private ForestLod? generatedForestLod;
        private readonly List<ForestLod> chunkLods = new();

        /// <summary>
        /// While the wanted level is current everywhere, builds the neighbouring levels one chunk at a
        /// time in the background so a zoom change swaps to meshes that are already up to date.
        /// </summary>
        private void PrepareNeighbouringLods(ForestSystem forest, GraphicsSettings graphics, ForestLod lod)
        {
            if (pendingForestBuild != null) return;
            foreach (var other in Enum.GetValues<ForestLod>())
            {
                if (Math.Abs((int)other - (int)lod) != 1) continue;
                foreach (var chunk in visibleChunks)
                {
                    if (individualForestChunks.TryGetValue((chunk, other), out var cached) && IsFresh(cached, chunk, forest, graphics)) continue;
                    GetIndividualForestChunk(chunk, forest, graphics, other, true, out _);
                    if (pendingForestBuild != null) return;
                }
            }
        }
        private readonly GraphicsSettings defaultPineGraphics = new() { Enhanced = false };

        internal void WarmIndividualForest(ForestSystem forest, GraphicsSettings graphics)
        {
            foreach (var chunk in chunkIndex.Chunks)
                foreach (var lod in Enum.GetValues<ForestLod>()) GetIndividualForestChunk(chunk, forest, graphics, lod, false, out _);
        }

        /// <summary>
        /// Brings the cached geometry of <paramref name="lod"/> up to date. <paramref name="current"/> is false
        /// when only a stale copy is available yet (its replacement is being built in the background);
        /// with <paramref name="deferIfStale"/> a stale or missing copy never blocks the frame, because
        /// the caller keeps drawing another level of detail that is current.
        /// </summary>
        private IndividualForestChunk GetIndividualForestChunk(TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics,
            ForestLod lod, bool deferIfStale, out bool current)
        {
            current = true;
            if (!individualForestChunks.TryGetValue((chunk, lod), out var geometry))
                individualForestChunks.Add((chunk, lod), geometry = new(chunk.TileIds.Length));
            bool changed = !geometry.Initialized || geometry.TerrainVersion != chunk.PropVersion
                || forest.ForestYear >= geometry.NextStageYear
                || geometry.Generation != forest.IndividualTrees.Generation
                || geometry.ModelStyle != graphics.ForestModels || geometry.ImportedBirch != graphics.ImportedBirch;
            bool topologyChanged = changed;
            if (changed || geometry.ForestRevision != forest.Revision)
            {
                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    ulong revision = forest.IndividualTrees.TryGet(chunk.TileIds[i], out var patch) ? patch.TopologyRevision : 0;
                    changed |= geometry.TileRevisions[i] != revision;
                }
                topologyChanged = changed;
                geometry.LightShapeDirty = false;
                foreach (var cached in geometry.Trees)
                    if (forest.IndividualTrees.TryGet(cached.TileId, out var patch) && cached.Index < patch.Count)
                        geometry.LightShapeDirty |= ShapeKeyOf(patch.Trees[cached.Index], forest.ForestYear, cached.Site) != cached.ShapeKey;
            }
            changed |= geometry.LightShapeDirty;
            if (changed)
            {
                bool immediate = SynchronousForestBuilds || (!deferIfStale && (!geometry.Initialized
                    || geometry.TerrainVersion != chunk.PropVersion || geometry.Generation != forest.IndividualTrees.Generation
                    || geometry.ModelStyle != graphics.ForestModels || geometry.ImportedBirch != graphics.ImportedBirch
                    || (geometry.EditRevision != forest.EditRevision && topologyChanged)));
                if (!immediate)
                {
                    if (pendingForestBuild == null)
                    {
                        var replacement = new IndividualForestChunk(chunk.TileIds.Length);
                        pendingForestBuild = new ForestBuild { Chunk = chunk, Lod = lod, Geometry = replacement,
                            Work = BuildIndividualForestChunk(chunk, replacement, forest, graphics, lod).GetEnumerator() };
                        pendingForestBuild.Work.MoveNext(); // Capture a consistent tree snapshot before yielding.
                    }
                    if (!topologyChanged && geometry.ForestRevision != forest.Revision)
                    {
                        // Light-only rebuilds may wait behind other chunks. Publish the
                        // continuous light/health/growth state immediately while keeping
                        // LightShapeDirty set until the replacement topology is ready.
                        UpdateIndividualForestState(geometry, forest);
                        geometry.ForestRevision = forest.Revision;
                    }
                    SetIndividualForestElapsed(geometry, forest.ForestYear);
                    current = false;
                    return geometry;
                }
                CancelForestBuild();
                foreach (var step in BuildIndividualForestChunk(chunk, geometry, forest, graphics, lod)) { }
                geometry.ModelStyle = graphics.ForestModels;
                geometry.ImportedBirch = graphics.ImportedBirch;
                geometry.Initialized = true;
                geometry.TerrainVersion = chunk.PropVersion;
                geometry.Generation = forest.IndividualTrees.Generation;
                geometry.EditRevision = forest.EditRevision;
                ForestChunkRebuilds++;
                TotalForestChunkRebuilds++;
            }
            if (changed || geometry.ForestRevision != forest.Revision)
            {
                UpdateIndividualForestState(geometry, forest);
                geometry.ForestRevision = forest.Revision;
            }
            geometry.EditRevision = forest.EditRevision;
            SetIndividualForestElapsed(geometry, forest.ForestYear);
            return geometry;
        }

        /// <summary>
        /// True when the cached geometry already shows the present forest (shape, season, light bands),
        /// refreshing its cheap continuous state on the way. Never starts a rebuild.
        /// </summary>
        private bool IsFresh(IndividualForestChunk geometry, TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics)
        {
            if (!geometry.Initialized || geometry.TerrainVersion != chunk.PropVersion
                || forest.ForestYear >= geometry.NextStageYear || geometry.Generation != forest.IndividualTrees.Generation
                || geometry.ModelStyle != graphics.ForestModels || geometry.ImportedBirch != graphics.ImportedBirch) return false;
            if (geometry.ForestRevision != forest.Revision)
            {
                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    ulong revision = forest.IndividualTrees.TryGet(chunk.TileIds[i], out var patch) ? patch.TopologyRevision : 0;
                    if (geometry.TileRevisions[i] != revision) return false;
                }
                UpdateIndividualForestState(geometry, forest);
                geometry.ForestRevision = forest.Revision;
                geometry.EditRevision = forest.EditRevision;
            }
            return !geometry.LightShapeDirty;
        }

        /// <summary>
        /// The level of detail to draw for one chunk. A level that is not current yet is never shown in
        /// place of a current one: trees would pop to an old season, size or stand while the new mesh
        /// is built, so the chunk keeps its current level until the wanted one is ready.
        /// </summary>
        private ForestLod ResolveForestLod(TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics, ForestLod target)
        {
            if (individualForestChunks.TryGetValue((chunk, target), out var wanted) && IsFresh(wanted, chunk, forest, graphics))
            {
                GetIndividualForestChunk(chunk, forest, graphics, target, false, out _);
                return target;
            }
            ForestLod? fallback = null;
            int bestDistance = int.MaxValue;
            foreach (var lod in Enum.GetValues<ForestLod>())
            {
                if (lod == target || !individualForestChunks.TryGetValue((chunk, lod), out var other)) continue;
                int distance = Math.Abs((int)lod - (int)target);
                if (distance < bestDistance && IsFresh(other, chunk, forest, graphics)) { fallback = lod; bestDistance = distance; }
            }
            GetIndividualForestChunk(chunk, forest, graphics, target, fallback.HasValue, out bool current);
            return current || fallback is null ? target : fallback.Value;
        }

        private static void SetIndividualForestElapsed(IndividualForestChunk geometry, double year)
        {
            float elapsed = (float)Math.Max(0, year - geometry.GrowthYear);
            geometry.Wood.ForestElapsedYears = geometry.Crowns.ForestElapsedYears = elapsed;
            geometry.Wood.ForestCurrentYear = geometry.Crowns.ForestCurrentYear = (float)year;
            geometry.Floor.ForestElapsedYears = 0;
        }

        private void CancelForestBuild()
        {
            pendingForestBuild?.Dispose();
            pendingForestBuild = null;
        }

        private void AdvanceForestBuild(ForestSystem forest, GraphicsSettings graphics)
        {
            var build = pendingForestBuild;
            if (build == null) return;
            var geometry = build.Geometry;
            bool obsolete = geometry.Generation != forest.IndividualTrees.Generation
                || geometry.TerrainVersion != build.Chunk.PropVersion || geometry.EditRevision != forest.EditRevision
                || geometry.ModelStyle != graphics.ForestModels || geometry.ImportedBirch != graphics.ImportedBirch;
            for (int i = 0; i < build.Chunk.TileIds.Length && !obsolete; i++)
            {
                ulong revision = forest.IndividualTrees.TryGet(build.Chunk.TileIds[i], out var patch) ? patch.TopologyRevision : 0;
                obsolete |= geometry.TileRevisions[i] != revision;
            }
            if (obsolete) { CancelForestBuild(); return; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            do
            {
                if (!build.Work.MoveNext())
                {
                    UpdateIndividualForestState(geometry, forest);
                    geometry.ForestRevision = forest.Revision;
                    geometry.Initialized = true;
                    var key = (build.Chunk, build.Lod);
                    individualForestChunks[key].Dispose();
                    individualForestChunks[key] = geometry;
                    build.Work.Dispose();
                    pendingForestBuild = null;
                    ForestChunkRebuilds++; TotalForestChunkRebuilds++;
                    return;
                }
            } while (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2);
        }

        private static void UpdateIndividualForestState(IndividualForestChunk geometry, ForestSystem forest)
        {
            int count = geometry.Trees.Count * 2;
            geometry.LightShapeDirty = false;
            if (geometry.State.Length != count) geometry.State = new Vector4[count];
            for (int i = 0; i < geometry.Trees.Count; i++)
            {
                var slot = geometry.Trees[i];
                if (!forest.IndividualTrees.TryGet(slot.TileId, out var patch) || patch.Trees[slot.Index].Id != slot.Id)
                    throw new InvalidOperationException("Forest topology changed without invalidating geometry.");
                var tree = patch.Trees[slot.Index];
                geometry.LightShapeDirty |= ShapeKeyOf(tree, forest.ForestYear, slot.Site) != slot.ShapeKey;
                var state = ForestTreeRenderState.Create(tree, slot.Size, forest.ForestYear);
                geometry.State[i * 2] = state.Scale;
                geometry.State[i * 2 + 1] = state.Rate;
            }
            geometry.GrowthYear = forest.ForestYear;
            if (count == 0) return;
            if (geometry.StateBuffer == 0) geometry.StateBuffer = GL.GenBuffer();
            if (geometry.StateTexture == 0) geometry.StateTexture = GL.GenTexture();
            GL.BindBuffer(BufferTarget.TextureBuffer, geometry.StateBuffer);
            GL.BufferData(BufferTarget.TextureBuffer, count * 4 * sizeof(float), geometry.State, BufferUsageHint.DynamicDraw);
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.TextureBuffer, geometry.StateTexture);
            GL.TexBuffer(TextureBufferTarget.TextureBuffer, SizedInternalFormat.Rgba32f, geometry.StateBuffer);
            GL.ActiveTexture(TextureUnit.Texture0);
            geometry.Wood.ForestStateTexture = geometry.Crowns.ForestStateTexture = geometry.StateTexture;
        }

        private static int ShapeKeyOf(in ForestTree tree, double year, in TreeSite site) =>
            TreeShapeSpec.From(tree, year, site, 0).ShapeKey;

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
            float crownFraction = ForestTreeAppearance.CrownFraction(tree.Species,
                ForestTreeAppearance.Stage(tree.Species, tree.Age(year)));
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

        private IEnumerable<bool> BuildIndividualForestChunk(TerrainChunk chunk, IndividualForestChunk geometry, ForestSystem forest, GraphicsSettings graphics, ForestLod lod)
        {
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
            geometry.AnchorYear = forest.ForestYear;
            geometry.NextStageYear = double.PositiveInfinity;
            geometry.Trees.Clear();
            geometry.TerrainVersion = chunk.PropVersion;
            geometry.Generation = forest.IndividualTrees.Generation;
            geometry.EditRevision = forest.EditRevision;
            geometry.ModelStyle = graphics.ForestModels;
            geometry.ImportedBirch = graphics.ImportedBirch;
            var snapshots = new Dictionary<int, ForestTreeStore.Patch>();
            for (int i = 0; i < chunk.TileIds.Length; i++)
            {
                int id = chunk.TileIds[i];
                geometry.TileRevisions[i] = 0;
                if (!forest.IndividualTrees.TryGet(id, out var source)) continue;
                geometry.TileRevisions[i] = source.TopologyRevision;
                var snapshot = new ForestTreeStore.Patch(source.Count) { Count = source.Count };
                Array.Copy(source.Trees, snapshot.Trees, source.Count);
                if (source.DeadTrees != null) snapshot.DeadTrees = new(source.DeadTrees);
                if (source.Stumps != null) snapshot.Stumps = new(source.Stumps);
                snapshots.Add(id, snapshot);
            }
            foreach (int id in chunk.TileIds)
            {
                if (roads.Has(id) || !snapshots.TryGetValue(id, out var patch)) continue;
                Tile tile = tiles[id];
                for (int i = 0; i < patch.Count; i++)
                {
                    ForestTree tree = patch.Trees[i];
                    var stage = ForestTreeAppearance.Stage(tree.Species, tree.Age(geometry.AnchorYear));
                    var phase = TreeLifePhases.Of(tree.Species, tree.Age(geometry.AnchorYear));
                    geometry.NextStageYear = Math.Min(geometry.NextStageYear,
                        tree.BirthYear + Math.Min(TreeLifePhases.NextAge(tree.Species, phase),
                            ForestTreeAppearance.NextStageAge(tree.Species, stage)));
                    // Health is applied from the small per-tree GPU state, not baked into the mesh.
                    TreeInstance stem = IndividualStem(tile, tree with { Health = 1 }, geometry.AnchorYear);
                    var size = tree.At(geometry.AnchorYear);
                    float radial = tree.AnnualGrowth.Diameter / size.Diameter;
                    float vertical = tree.AnnualGrowth.Height / size.Height;
                    float crown = tree.AnnualGrowth.CrownRadius / size.CrownRadius;
                    var origin = new Vector3(stem.X, stem.Y, stem.BaseZ);
                    if (!ImportedForestModels.UsesImported(tree.Species, graphics))
                    {
                        int slot = geometry.Trees.Count;
                        var site = ForestTreeSites.Gap(patch.Trees, patch.Count, i, tileSizeH / TreeMetresToWorld,
                            tileSizeV / TreeMetresToWorld, geometry.AnchorYear, ((IForestHabitat)map).GetNormalizedElevation(id));
                        var spec = TreeShapeSpec.From(tree, geometry.AnchorYear, site, stem.Yaw);
                        geometry.NextStageYear = Math.Min(geometry.NextStageYear,
                            TreePhenology.NextChange(tree.Species, tree.Seed, geometry.AnchorYear));
                        geometry.Trees.Add(new(id, i, tree.Id, size, spec.ShapeKey, site));
                        var mesh = DendroTreeGenerator.Generate(spec, lod);
                        AppendAt(individualWood, mesh.Trunk, origin);
                        Repeat(individualWoodGrowth, mesh.Trunk.Length, new(origin, new Vector3(radial, radial, vertical), slot));
                        AppendAt(individualWood, mesh.Branches, origin);
                        Repeat(individualWoodGrowth, mesh.Branches.Length, new(origin, new Vector3(-1, crown, vertical), slot));
                        AppendAt(individualCrowns, mesh.Crown, origin);
                        // A negative rate component selects crown scaling instead of bole scaling.
                        Repeat(individualCrownGrowth, mesh.Crown.Length, new(origin, new Vector3(-1, crown, vertical), slot));
                    }
                    Vertex[] floor = lod == ForestLod.Far ? Array.Empty<Vertex>()
                        : DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Triangles, () => DrawForestFloor(stem));
                    individualFloor.AddRange(floor);
                    Repeat(individualFloorGrowth, floor.Length, default); // Decals stay on the sampled terrain.
                    yield return true;
                }
                if (patch.DeadTrees != null)
                    foreach (var dead in patch.DeadTrees)
                    {
                        var stem = IndividualStem(tile, dead.Tree, dead.DeathYear);
                        Vertex[] wood;
                        if (!ImportedForestModels.UsesImported(dead.Tree.Species, graphics))
                        {
                            var deadSpec = TreeShapeSpec.From(dead.Tree, dead.DeathYear, TreeSite.Open, stem.Yaw, dead: true);
                            var mesh = DendroTreeGenerator.Generate(deadSpec, ForestLod.Near);
                            var deadWood = new List<Vertex>(mesh.Trunk.Length + mesh.Branches.Length);
                            AppendAt(deadWood, mesh.Trunk, new(stem.X, stem.Y, stem.BaseZ));
                            AppendAt(deadWood, mesh.Branches, new(stem.X, stem.Y, stem.BaseZ));
                            wood = deadWood.ToArray();
                        }
                        else wood = DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads, () => DrawTreeWood(stem));
                        individualWood.AddRange(wood);
                        // Physical snag/fallen state follows the current year in every cached LOD,
                        // rather than waiting for each LOD to replace a differently aged mesh.
                        Repeat(individualWoodGrowth, wood.Length, ForestVertexGrowth.DeadWood(
                            new Vector3(stem.X,stem.Y,stem.BaseZ),dead.DeathYear,
                            dead.Tree.Dimensions.Diameter*TreeMetresToWorld,stem.Yaw));
                        yield return true;
                    }
                if (patch.Stumps == null) continue;
                foreach (var stump in patch.Stumps)
                {
                    geometry.NextStageYear = Math.Min(geometry.NextStageYear, geometry.AnchorYear + 1.0 / 12);
                    TreeInstance stem = IndividualStem(tile, stump.Felled, stump.FelledYear);
                    Vertex[] wood = DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads,
                        () => DrawStump(stem, stump.Decay(geometry.AnchorYear)));
                    individualWood.AddRange(wood);
                    Repeat(individualWoodGrowth, wood.Length, new(Vector3.Zero, Vector3.Zero));
                        yield return true;
                }
            }
            foreach (var page in geometry.Wood.UploadForestPages(individualWood, individualWoodGrowth)) yield return page;
            foreach (var page in geometry.Crowns.UploadForestPages(individualCrowns, individualCrownGrowth)) yield return page;
            foreach (var page in geometry.Floor.UploadForestPages(individualFloor, individualFloorGrowth)) yield return page;

            static void Repeat(List<ForestVertexGrowth> target, int count, ForestVertexGrowth value)
            {
                for (int i = 0; i < count; i++) target.Add(value);
            }
            static void AppendAt(List<Vertex> target, Vertex[] source, Vector3 origin)
            {
                foreach (var vertex in source)
                    target.Add(new Vertex(vertex.Position + origin, vertex.Normal, vertex.Color));
            }
        }

        private void DrawIndividualTrees(ForestSystem forest, RenderContext context, GraphicsSettings graphics)
        {
            graphics ??= defaultPineGraphics;
            bool shadow0 = RenderDevice.Visuals?.ShadowPass == true;
            if (!shadow0) AdvanceForestBuild(forest, graphics);
            generatedForestLod = ForestLodPolicy.Select(context.PixelsPerWorldUnit, generatedForestLod);
            ForestLod lod = generatedForestLod.Value;
            chunkLods.Clear();
            foreach (var chunk in visibleChunks) chunkLods.Add(ResolveForestLod(chunk, forest, graphics, lod));
            if (!shadow0) PrepareNeighbouringLods(forest, graphics, lod);
            bool shadow = RenderDevice.Visuals?.ShadowPass == true;
            if (!shadow)
            {
                if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.ForestFloor;
                using (new RenderStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-1, -1))
                    for (int c = 0; c < visibleChunks.Count; c++) individualForestChunks[(visibleChunks[c], chunkLods[c])].Floor.DrawArray();
            }
            if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.Wood;
            for (int c = 0; c < visibleChunks.Count; c++) individualForestChunks[(visibleChunks[c], chunkLods[c])].Wood.DrawArray();
            forestMaterial.Use();
            // The texture represents the whole foliage mass. Rendering only the front
            // shell lets its gaps expose branches instead of filling them with the back
            // shell or a solid outline. Use identical coverage for shadow casters.
            using (new RenderStateScope().Enable(EnableCap.CullFace))
            {
                GL.GetInteger(GetPName.CullFaceMode, out int oldCull);
                try
                {
                    GL.CullFace(TriangleFace.Back);
                    for (int c = 0; c < visibleChunks.Count; c++) individualForestChunks[(visibleChunks[c], chunkLods[c])].Crowns.DrawArray(false);
                }
                finally { GL.CullFace((TriangleFace)oldCull); }
            }
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
        }

        private void DisposeIndividualForest()
        {
            CancelForestBuild();
            importedPine?.Dispose(); importedPine = null;
            importedForestModels.Dispose();
            foreach (var chunk in individualForestChunks.Values) chunk.Dispose();
            individualForestChunks.Clear();
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
        }
    }
}
