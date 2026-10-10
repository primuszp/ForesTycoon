using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        // A physical metre is intentionally compressed for the existing diorama proportions.
        internal const float TreeMetresToWorld = TreeScale.MetresToWorld;
        private readonly record struct ForestGpuTree(int TileId, int Index, ulong Id, ForestTreeDimensions Size, long ShapeKey, TreeSite Site,
            Vector4 SeasonTint, Vector4 SeasonBounds);
        private sealed class IndividualForestChunk : IDisposable
        {
            internal readonly VertexBuffer Wood = new(PrimitiveTopology.Triangles);
            internal readonly VertexBuffer Crowns = new(PrimitiveTopology.Triangles);
            internal readonly VertexBuffer Floor = new(PrimitiveTopology.Triangles);
            internal readonly ulong[] TileRevisions;
            internal readonly List<ForestGpuTree> Trees = new();
            internal Vector4[] State = Array.Empty<Vector4>();
            internal IForestStateBuffer StateResource;
            internal double GrowthYear;
            internal ulong TerrainVersion, Generation, ForestRevision, EditRevision;
            internal double AnchorYear;
            internal double NextStageYear;
            internal bool Initialized;
            internal bool LightShapeDirty;
            internal long LastUsed;
            internal long GpuBytes => Wood.GpuPayloadBytes + Crowns.GpuPayloadBytes + Floor.GpuPayloadBytes + State.LongLength * 16;
            internal long CpuBytes => TileRevisions.LongLength * sizeof(ulong) + (long)Trees.Capacity * Unsafe.SizeOf<ForestGpuTree>()
                + State.LongLength * 16 + Wood.CpuPayloadBytes + Crowns.CpuPayloadBytes + Floor.CpuPayloadBytes;
            internal Dictionary<int, ForestTreeStore.Patch> Snapshot;
            internal IndividualForestChunk(int count) => TileRevisions = new ulong[count];
            public void Dispose() {
                Wood.Dispose(); Crowns.Dispose(); Floor.Dispose();
                StateResource?.Dispose();
            }
        }

        private readonly Dictionary<(TerrainChunk, ForestLod), IndividualForestChunk> individualForestChunks = new();
        private readonly Dictionary<TerrainChunk, Dictionary<int, ForestTreeStore.Patch>> forestLodSnapshots = new();
        private readonly Dictionary<TerrainChunk, ForestLod> displayedForestLods = new();
        private readonly List<Vertex> individualWood = new(), individualCrowns = new(), individualFloor = new();
        private readonly List<ForestVertexGrowth> individualWoodGrowth = new(), individualCrownGrowth = new(), individualFloorGrowth = new();
        private sealed class ForestBuild : IDisposable
        {
            internal TerrainChunk Chunk;
            internal ForestLod Lod;
            internal IndividualForestChunk Geometry;
            internal IEnumerator<bool> Work;
            /// <summary>Rebuild caused by a player edit (road, building, terrain, forestry): jumps the queue and gets a larger budget.</summary>
            internal bool Priority;
            internal bool Preload;
            public void Dispose() { Work.Dispose(); Geometry.Dispose(); }
        }
        private ForestBuild pendingForestBuild;
        internal bool HasPendingForestBuild => pendingForestBuild != null;
        // Explicit diagnostic mode for pixel assertions at an exact life-stage boundary.
        internal bool SynchronousForestBuilds { get; set; }
        // Direct geometry tools can still request synchronous first builds; the game host streams them.
        internal bool StreamGeometry { get; set; }
        internal long ForestCacheBudgetBytes { get; set; } = 128L * 1024 * 1024;
        internal long ForestGpuPayloadBytes { get; private set; }
        internal long ForestCpuPayloadBytes { get; private set; }
        internal long ForestBudgetExcessBytes { get; private set; }
        internal int ForestResidentLods => individualForestChunks.Count;
        internal int ReadyVisibleForestChunks(ForestLod lod)
        {
            int count = 0;
            foreach (var chunk in visibleChunks)
                if (displayedForestLods.TryGetValue(chunk, out var displayed) && displayed == lod
                    && individualForestChunks.TryGetValue((chunk, lod), out var geometry) && geometry.Initialized)
                    count++;
            return count;
        }
        internal int ForestCacheEvictions { get; private set; }
        private long forestUseSequence;
        private ForestLod? generatedForestLod;
        private readonly List<ForestLod> chunkLods = new();
        private readonly HashSet<Dictionary<int, ForestTreeStore.Patch>> measuredForestSnapshots = new();

        private void MeasureForestPayload()
        {
            long gpu = 0, cpu = 0;
            measuredForestSnapshots.Clear();
            foreach (var geometry in individualForestChunks.Values) Include(geometry);
            if (pendingForestBuild != null) Include(pendingForestBuild.Geometry);
            foreach (var snapshot in forestLodSnapshots.Values) measuredForestSnapshots.Add(snapshot);
            foreach (var snapshot in measuredForestSnapshots)
                foreach (var patch in snapshot.Values)
                    cpu += (long)patch.Trees.Length * Unsafe.SizeOf<ForestTree>()
                        + (long)(patch.Stumps?.Capacity ?? 0) * Unsafe.SizeOf<ForestTreeStump>()
                        + (long)(patch.DeadTrees?.Capacity ?? 0) * Unsafe.SizeOf<ForestDeadTree>();
            cpu += (long)(individualWood.Capacity + individualCrowns.Capacity + individualFloor.Capacity) * Vertex.Stride
                + (long)(individualWoodGrowth.Capacity + individualCrownGrowth.Capacity + individualFloorGrowth.Capacity) * ForestVertexGrowth.Stride;
            ForestGpuPayloadBytes = gpu; ForestCpuPayloadBytes = cpu;
            measuredForestSnapshots.Clear(); // Measurement must not retain evicted snapshots.
            void Include(IndividualForestChunk geometry)
            {
                gpu += geometry.GpuBytes; cpu += geometry.CpuBytes;
                if (geometry.Snapshot != null) measuredForestSnapshots.Add(geometry.Snapshot);
            }
        }

        private void TrimForestCache()
        {
            if (ForestCacheBudgetBytes <= 0) throw new InvalidOperationException("Forest cache budget must be positive.");
            // Build scratch is borrowed by one suspended iterator. Release it only after completion/cancellation.
            if (pendingForestBuild == null)
            {
                ReleaseLarge(individualWood); ReleaseLarge(individualCrowns); ReleaseLarge(individualFloor);
                ReleaseLarge(individualWoodGrowth); ReleaseLarge(individualCrownGrowth); ReleaseLarge(individualFloorGrowth);
            }
            MeasureForestPayload();
            if (!StreamGeometry) return; // Explicit warm-up tools preserve all requested levels.
            while (ForestCpuPayloadBytes + ForestGpuPayloadBytes > ForestCacheBudgetBytes)
            {
                (TerrainChunk Chunk, ForestLod Lod) victim = default;
                long oldest = long.MaxValue;
                foreach (var entry in individualForestChunks)
                {
                    if (pendingForestBuild != null && entry.Key == (pendingForestBuild.Chunk, pendingForestBuild.Lod)) continue;
                    // Never release resources while they are the frame's visible continuity fallback.
                    if (visibleChunks.Contains(entry.Key.Item1) && displayedForestLods.TryGetValue(entry.Key.Item1, out var displayed)
                        && displayed == entry.Key.Item2) continue;
                    if (entry.Value.LastUsed < oldest) { victim = entry.Key; oldest = entry.Value.LastUsed; }
                }
                if (victim.Chunk == null) break;
                individualForestChunks[victim].Dispose(); individualForestChunks.Remove(victim);
                ForestCacheEvictions++;
                if (displayedForestLods.TryGetValue(victim.Chunk, out var previous) && previous == victim.Lod)
                    displayedForestLods.Remove(victim.Chunk);
                bool retained = pendingForestBuild?.Chunk == victim.Chunk;
                foreach (var lod in Enum.GetValues<ForestLod>()) retained |= individualForestChunks.ContainsKey((victim.Chunk, lod));
                if (!retained) forestLodSnapshots.Remove(victim.Chunk);
                MeasureForestPayload();
            }
            // The visible camera-selected meshes and one replacement are the irreducible working set.
            // Expose its excess instead of silently hiding trees or claiming a hard whole-process memory limit.
            ForestBudgetExcessBytes = Math.Max(0, ForestCpuPayloadBytes + ForestGpuPayloadBytes - ForestCacheBudgetBytes);
            static void ReleaseLarge<T>(List<T> list)
            {
                list.Clear();
                if ((long)list.Capacity * Unsafe.SizeOf<T>() > 1024 * 1024) list.TrimExcess();
            }
        }

        /// <summary>
        /// While the wanted level is current everywhere, builds the neighbouring levels one chunk at a
        /// time in the background so a zoom change swaps to meshes that are already up to date.
        /// </summary>
        private void PrepareNeighbouringLods(ForestSystem forest, GraphicsSettings graphics, ForestLod lod)
        {
            if (pendingForestBuild != null) return;
            if (StreamGeometry && ForestCpuPayloadBytes + ForestGpuPayloadBytes >= ForestCacheBudgetBytes * 3 / 4) return;
            foreach (var other in Enum.GetValues<ForestLod>())
            {
                if (Math.Abs((int)other - (int)lod) != 1) continue;
                foreach (var chunk in visibleChunks)
                {
                    if (individualForestChunks.TryGetValue((chunk, other), out var cached) && IsFresh(cached, chunk, forest, graphics)) continue;
                    GetIndividualForestChunk(chunk, forest, graphics, other, true, out _, preload: true);
                    if (pendingForestBuild != null) return;
                }
            }
        }
        private readonly GraphicsSettings defaultPineGraphics = new() { Enhanced = false };

        internal void WarmIndividualForest(ForestSystem forest, GraphicsSettings graphics)
        {
            bool synchronous = SynchronousForestBuilds;
            try
            {
                SynchronousForestBuilds = true;
                foreach (var chunk in chunkIndex.Chunks)
                    foreach (var lod in Enum.GetValues<ForestLod>()) GetIndividualForestChunk(chunk, forest, graphics, lod, false, out _);
            }
            finally { SynchronousForestBuilds = synchronous; }
        }

        // Restore the complete opening diorama at the camera's actual detail level.
        // Only its visible chunks are prepared; the rest of the map still streams.
        internal void PrepareOpeningForest(ForestSystem forest, GraphicsSettings graphics, RenderContext context)
        {
            bool synchronous = SynchronousForestBuilds;
            try
            {
                SynchronousForestBuilds = true;
                generatedForestLod = ForestLodPolicy.Select(context.PixelsPerWorldUnit, null);
                foreach (var chunk in visibleChunks)
                {
                    GetIndividualForestChunk(chunk, forest, graphics, generatedForestLod.Value, false, out _);
                    displayedForestLods[chunk] = generatedForestLod.Value;
                }
                TrimForestCache();
            }
            finally { SynchronousForestBuilds = synchronous; }
        }

        // Native regression diagnostic: all current LODs must share a morphology epoch and GPU clock.
        internal int CheckForestLodConsistency(ForestSystem forest, GraphicsSettings graphics)
        {
            int compared = 0;
            foreach (var chunk in chunkIndex.Chunks)
            {
                IndividualForestChunk basis = null;
                foreach (var lod in Enum.GetValues<ForestLod>())
                {
                    if (!individualForestChunks.TryGetValue((chunk, lod), out var geometry) || !IsFresh(geometry, chunk, forest, graphics)) continue;
                    if (geometry.Wood.ForestCurrentYear != (float)forest.ForestYear ||
                        Math.Abs(geometry.GrowthYear + geometry.Wood.ForestElapsedYears - forest.ForestYear) > 1e-6)
                        throw new InvalidOperationException("LOD uses an outdated growth/phenology clock.");
                    if (basis == null) { basis = geometry; continue; }
                    if (!ReferenceEquals(basis.Snapshot, geometry.Snapshot) || basis.AnchorYear != geometry.AnchorYear ||
                        basis.Trees.Count != geometry.Trees.Count)
                        throw new InvalidOperationException("LOD changed its tree morphology snapshot.");
                    for (int i = 0; i < basis.Trees.Count; i++)
                        if (basis.Trees[i] != geometry.Trees[i]) throw new InvalidOperationException("LOD changed tree identity, size or site.");
                    compared++;
                }
            }
            return compared;
        }

        /// <summary>
        /// Brings the cached geometry of <paramref name="lod"/> up to date. <paramref name="current"/> is false
        /// when only a stale copy is available yet (its replacement is being built in the background);
        /// with <paramref name="deferIfStale"/> a stale or missing copy never blocks the frame, because
        /// the caller keeps drawing another level of detail that is current.
        /// </summary>
        private IndividualForestChunk GetIndividualForestChunk(TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics,
            ForestLod lod, bool deferIfStale, out bool current, bool preload = false)
        {
            current = true;
            if (!individualForestChunks.TryGetValue((chunk, lod), out var geometry))
                individualForestChunks.Add((chunk, lod), geometry = new(chunk.TileIds.Length));
            geometry.LastUsed = ++forestUseSequence;
            bool changed = !geometry.Initialized || geometry.TerrainVersion != chunk.PropVersion
                || (forestLodSnapshots.TryGetValue(chunk, out var snapshot) && !ReferenceEquals(snapshot, geometry.Snapshot))
                || forest.ForestYear >= geometry.NextStageYear
                || geometry.Generation != forest.IndividualTrees.Generation;
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
                // A player edit must never stall the frame: the old copy stays on screen for the few frames its
                // replacement takes, and that replacement goes ahead of any background work.
                bool edited = geometry.Initialized && geometry.Generation == forest.IndividualTrees.Generation
                    && (geometry.TerrainVersion != chunk.PropVersion || (geometry.EditRevision != forest.EditRevision && topologyChanged));
                bool immediate = SynchronousForestBuilds || (!StreamGeometry && !deferIfStale && (!geometry.Initialized
                    || geometry.Generation != forest.IndividualTrees.Generation));
                if (!immediate)
                {
                    if (!preload && pendingForestBuild?.Preload == true) CancelForestBuild();
                    if (!preload && pendingForestBuild != null && !pendingForestBuild.Priority && pendingForestBuild.Lod < lod)
                        CancelForestBuild(); // A zoom-in must not wait for an obsolete coarse request.
                    if (edited && pendingForestBuild != null && !pendingForestBuild.Priority) CancelForestBuild();
                    if (pendingForestBuild == null && (!StreamGeometry || RenderDevice.Visuals?.ShadowPass != true))
                    {
                        var replacement = new IndividualForestChunk(chunk.TileIds.Length);
                        pendingForestBuild = new ForestBuild { Chunk = chunk, Lod = lod, Geometry = replacement, Priority = edited, Preload = preload,
                            Work = BuildIndividualForestChunk(chunk, replacement, forest, graphics, lod).GetEnumerator() };
                        try { pendingForestBuild.Work.MoveNext(); } // Only capture the consistent snapshot here.
                        catch { CancelForestBuild(); throw; }
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
                || !forestLodSnapshots.TryGetValue(chunk, out var snapshot) || !ReferenceEquals(snapshot, geometry.Snapshot)
                || forest.ForestYear >= geometry.NextStageYear || geometry.Generation != forest.IndividualTrees.Generation) return false;
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
            SetIndividualForestElapsed(geometry, forest.ForestYear);
            return !geometry.LightShapeDirty;
        }

        /// <summary>
        /// Keep the displayed detail while its replacement builds. A freshly built coarse mesh must
        /// not replace a detailed crown merely because fast-forward invalidated its morphology.
        /// </summary>
        private ForestLod ResolveForestLod(TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics, ForestLod target)
        {
            if (individualForestChunks.TryGetValue((chunk, target), out var wanted) && IsFresh(wanted, chunk, forest, graphics))
            {
                GetIndividualForestChunk(chunk, forest, graphics, target, false, out _);
                displayedForestLods[chunk] = target;
                return target;
            }
            ForestLod? fallback = null;
            int bestDistance = int.MaxValue;
            if (displayedForestLods.TryGetValue(chunk, out var retained) && retained <= target
                && individualForestChunks.TryGetValue((chunk, retained), out var retainedGeometry) && retainedGeometry.Initialized)
            {
                fallback = retained;
                bestDistance = (int)target - (int)retained;
            }
            foreach (var lod in Enum.GetValues<ForestLod>())
            {
                if (lod == target || !individualForestChunks.TryGetValue((chunk, lod), out var other)) continue;
                int distance = Math.Abs((int)lod - (int)target);
                if (distance < bestDistance && IsFresh(other, chunk, forest, graphics)) { fallback = lod; bestDistance = distance; }
            }
            // While every mesh is waiting for a new morphology epoch, keep the last displayed
            // level. Zoom must not select a different stale epoch merely because no LOD is fresh.
            if (!fallback.HasValue && displayedForestLods.TryGetValue(chunk, out var previous)
                && individualForestChunks.TryGetValue((chunk, previous), out var previousGeometry) && previousGeometry.Initialized)
            {
                GetIndividualForestChunk(chunk, forest, graphics, previous, false, out _);
                fallback = previous;
            }
            GetIndividualForestChunk(chunk, forest, graphics, target, fallback.HasValue, out bool current);
            var displayed = current || fallback is null ? target : fallback.Value;
            displayedForestLods[chunk] = displayed;
            return displayed;
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
            bool obsolete = !visibleChunks.Contains(build.Chunk) || geometry.Generation != forest.IndividualTrees.Generation
                || !forestLodSnapshots.TryGetValue(build.Chunk, out var snapshot) || !ReferenceEquals(snapshot, geometry.Snapshot)
                || geometry.TerrainVersion != build.Chunk.PropVersion || geometry.EditRevision != forest.EditRevision;
            for (int i = 0; i < build.Chunk.TileIds.Length && !obsolete; i++)
            {
                ulong revision = forest.IndividualTrees.TryGet(build.Chunk.TileIds[i], out var patch) ? patch.TopologyRevision : 0;
                obsolete |= geometry.TileRevisions[i] != revision;
            }
            if (obsolete) { CancelForestBuild(); return; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            double budget = build.Priority ? 8 : build.Preload ? 2 : build.Lod == ForestLod.Near ? 8 : 4;
            try
            {
                do
                {
                    if (!build.Work.MoveNext())
                    {
                        UpdateIndividualForestState(geometry, forest);
                        geometry.ForestRevision = forest.Revision;
                        SetIndividualForestElapsed(geometry, forest.ForestYear);
                        geometry.Initialized = true;
                        geometry.LastUsed = ++forestUseSequence;
                        build.Work.Dispose();
                        var key = (build.Chunk, build.Lod);
                        individualForestChunks[key].Dispose();
                        individualForestChunks[key] = geometry;
                        pendingForestBuild = null;
                        ForestChunkRebuilds++; TotalForestChunkRebuilds++;
                        return;
                    }
                } while (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < budget);
            }
            catch
            {
                CancelForestBuild();
                throw;
            }
        }

        private static void UpdateIndividualForestState(IndividualForestChunk geometry, ForestSystem forest)
        {
            int count = geometry.Trees.Count * 4;
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
                geometry.State[i * 4] = state.Scale;
                geometry.State[i * 4 + 1] = state.Rate;
                geometry.State[i * 4 + 2] = slot.SeasonTint;
                geometry.State[i * 4 + 3] = slot.SeasonBounds;
            }
            geometry.GrowthYear = forest.ForestYear;
            if (count == 0) return;
            geometry.StateResource ??= RenderDevice.CreateForestStateBuffer();
            geometry.StateResource.SetData(geometry.State, count);
            geometry.Wood.ForestState = geometry.Crowns.ForestState = geometry.StateResource;
        }

        private static long ShapeKeyOf(in ForestTree tree, double year, in TreeSite site) =>
            (TreeShapeSpec.From(tree, year, site, 0) with { Leaves = LeafState.Full }).ShapeKey;

        private static int CollectIndividualStems(ForestSystem forest, Tile tile, Span<TreeInstance> output)
        {
            if (!forest.IndividualTrees.TryGet(tile.Id, out var patch)) return 0;
            int count = Math.Min(patch.Count, output.Length);
            for (int i = 0; i < count; i++) output[i] = IndividualStem(tile, patch.Trees[i], forest.ForestYear);
            return count;
        }

        private readonly List<(OpenTK.Mathematics.Vector2, float, float)> crownNeighbours = new();

        /// <summary>
        /// Room for one crown among the crowns on its own and the eight surrounding tiles (frozen chunk
        /// snapshot where available, so every LOD shapes the same way). World units.
        /// </summary>
        private CrownSpace CrownSpaceOf(Tile tile, in ForestTree tree, ForestSystem forest,
            Dictionary<int, ForestTreeStore.Patch> snapshots, double year)
        {
            crownNeighbours.Clear();
            var size = tree.At(year);
            SurfacePoint(tile, tree.U, tree.V, out float x, out float y, out _);
            int column = Math.Clamp((int)MathF.Floor((x + offsetX) / tileSizeH), 0, settings.TileColumns - 1);
            int row = Math.Clamp((int)MathF.Floor((y + offsetY) / tileSizeV), 0, settings.TileRows - 1);
            for (int du = -1; du <= 1; du++)
                for (int dv = -1; dv <= 1; dv++)
                {
                    int u = column + du, v = row + dv;
                    if (u < 0 || v < 0 || u >= settings.TileColumns || v >= settings.TileRows) continue;
                    Tile other = getTileByCoords(u, v);
                    if (!snapshots.TryGetValue(other.Id, out var patch) && !forest.IndividualTrees.TryGet(other.Id, out patch)) continue;
                    for (int j = 0; j < patch.Count; j++)
                    {
                        var neighbour = patch.Trees[j];
                        if (neighbour.Id == tree.Id) continue;
                        var dimensions = neighbour.At(year);
                        SurfacePoint(other, neighbour.U, neighbour.V, out float nx, out float ny, out _);
                        crownNeighbours.Add((new(nx, ny), dimensions.CrownRadius * TreeMetresToWorld, dimensions.Height));
                    }
                }
            return CrownSpace.Measure(new(x, y), size.CrownRadius * TreeMetresToWorld, size.Height,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(crownNeighbours));
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
            // Every LOD is a tessellation of the same frozen tree/site snapshot. Building a
            // missing LOD from today's dimensions would change morphology when zooming.
            IndividualForestChunk basis = null;
            foreach (var level in Enum.GetValues<ForestLod>())
                if (individualForestChunks.TryGetValue((chunk, level), out var cached) &&
                    !ReferenceEquals(cached, geometry) && IsFresh(cached, chunk, forest, graphics))
                { basis = cached; break; }
            geometry.AnchorYear = basis?.AnchorYear ?? forest.ForestYear;
            geometry.NextStageYear = double.PositiveInfinity;
            geometry.Trees.Clear();
            geometry.TerrainVersion = chunk.PropVersion;
            geometry.Generation = forest.IndividualTrees.Generation;
            geometry.EditRevision = forest.EditRevision;
            var snapshots = basis?.Snapshot ?? new Dictionary<int, ForestTreeStore.Patch>();
            geometry.Snapshot = snapshots;
            forestLodSnapshots[chunk] = snapshots;
            for (int i = 0; i < chunk.TileIds.Length; i++)
            {
                int id = chunk.TileIds[i];
                geometry.TileRevisions[i] = 0;
                if (!forest.IndividualTrees.TryGet(id, out var source)) continue;
                geometry.TileRevisions[i] = source.TopologyRevision;
                if (basis != null) continue;
                var snapshot = new ForestTreeStore.Patch(source.Count) { Count = source.Count };
                Array.Copy(source.Trees, snapshot.Trees, source.Count);
                if (source.DeadTrees != null) snapshot.DeadTrees = new(source.DeadTrees);
                if (source.Stumps != null) snapshot.Stumps = new(source.Stumps);
                snapshots.Add(id, snapshot);
            }
            yield return true; // Snapshot publication is a separate work unit from any tree generation.
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
                    {
                        int slot = geometry.Trees.Count;
                        var site = ForestTreeSites.Gap(patch.Trees, patch.Count, i, tileSizeH / TreeMetresToWorld,
                            tileSizeV / TreeMetresToWorld, geometry.AnchorYear, ((IForestHabitat)map).GetNormalizedElevation(id))
                            with { Space = CrownSpaceOf(tile, tree, forest, snapshots, geometry.AnchorYear) };
                        var spec = TreeShapeSpec.From(tree, geometry.AnchorYear, site, stem.Yaw) with { Leaves = LeafState.Full };
                        var season = ForestSeasonRenderState.Create(spec);
                        geometry.Trees.Add(new(id, i, tree.Id, size, spec.ShapeKey, site,
                            season.Tint, season.Bounds));
                        var mesh = DendroTreeGenerator.GenerateSeasonal(spec, lod);
                        AppendAt(individualWood, mesh.Trunk, origin);
                        Repeat(individualWoodGrowth, mesh.Trunk.Length, new(origin, new Vector3(radial, radial, vertical), slot));
                        AppendAt(individualWood, mesh.Branches, origin);
                        Repeat(individualWoodGrowth, mesh.Branches.Length, new(origin, new Vector3(-1, crown, vertical), slot));
                        AppendAt(individualCrowns, mesh.Crown, origin);
                        // A negative rate component selects crown scaling instead of bole scaling.
                        Repeat(individualCrownGrowth, mesh.Crown.Length, new(origin, new Vector3(-1, crown, vertical), slot));
                    }
                    Vertex[] floor = lod == ForestLod.Far ? Array.Empty<Vertex>()
                        : DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Triangles, () => DrawForestFloor(stem));
                    individualFloor.AddRange(floor);
                    Repeat(individualFloorGrowth, floor.Length, default); // Decals stay on the sampled terrain.
                    yield return true;
                }
                if (patch.DeadTrees != null)
                    foreach (var dead in patch.DeadTrees)
                    {
                        var stem = IndividualStem(tile, dead.Tree, dead.DeathYear);
                        var deadSpec = TreeShapeSpec.From(dead.Tree, dead.DeathYear, TreeSite.Open, stem.Yaw, dead: true);
                        var mesh = DendroTreeGenerator.Generate(deadSpec, ForestLod.Near);
                        var deadWood = new List<Vertex>(mesh.Trunk.Length + mesh.Branches.Length);
                        AppendAt(deadWood, mesh.Trunk, new(stem.X, stem.Y, stem.BaseZ));
                        AppendAt(deadWood, mesh.Branches, new(stem.X, stem.Y, stem.BaseZ));
                        var wood = deadWood.ToArray();
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
                    Vertex[] wood = DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Quads,
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
            foreach (var chunk in visibleChunks)
            {
                // A cold region builds the camera's requested detail directly. Never put a distant
                // proxy on screen at close zoom just to fill coverage before the detailed mesh.
                chunkLods.Add(ResolveForestLod(chunk, forest, graphics, lod));
            }
            if (!shadow0) PrepareNeighbouringLods(forest, graphics, lod);
            bool shadow = RenderDevice.Visuals?.ShadowPass == true;
            if (!shadow)
            {
                if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.ForestFloor;
                using (RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-1, -1))
                    for (int c = 0; c < visibleChunks.Count; c++)
                    {
                        var geometry = individualForestChunks[(visibleChunks[c], chunkLods[c])];
                        if (geometry.Initialized && geometry.Generation == forest.IndividualTrees.Generation) geometry.Floor.DrawArray();
                    }
            }
            if (graphics.HideTrees) { if (!shadow) TrimForestCache(); return; }
            if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.Wood;
            for (int c = 0; c < visibleChunks.Count; c++)
            {
                var geometry = individualForestChunks[(visibleChunks[c], chunkLods[c])];
                if (geometry.Initialized && geometry.Generation == forest.IndividualTrees.Generation) geometry.Wood.DrawArray();
            }
            forestMaterial.Use();
            // The texture represents the whole foliage mass. Rendering only the front
            // shell lets its gaps expose branches instead of filling them with the back
            // shell or a solid outline. Use identical coverage for shadow casters.
            using (RenderDevice.CreateStateScope().Cull(RenderCullFace.Back))
            {
                for (int c = 0; c < visibleChunks.Count; c++)
                {
                    var geometry = individualForestChunks[(visibleChunks[c], chunkLods[c])];
                    if (geometry.Initialized && geometry.Generation == forest.IndividualTrees.Generation) geometry.Crowns.DrawArray(false);
                }
            }
            forestMaterial.Use();
            if (!shadow) TrimForestCache();
        }

        private void DisposeIndividualForest()
        {
            CancelForestBuild();
            foreach (var chunk in individualForestChunks.Values) chunk.Dispose();
            individualForestChunks.Clear();
            forestLodSnapshots.Clear();
            displayedForestLods.Clear();
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
            measuredForestSnapshots.Clear();
            ForestGpuPayloadBytes = ForestCpuPayloadBytes = ForestBudgetExcessBytes = 0;
        }
    }
}
