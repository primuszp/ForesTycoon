using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        // A physical metre is intentionally compressed for the existing diorama proportions.
        internal const float TreeMetresToWorld = TreeScale.MetresToWorld;
        private readonly record struct ForestGpuTree(int TileId, int Index, ulong Id, ForestTreeDimensions Size, long ShapeKey, TreeSite Site,
            Vector4 SeasonTint, Vector4 SeasonBounds);
        private readonly record struct ForestTileRange(int TileId, int WoodStart, int WoodCount, int CrownStart, int CrownCount,
            int FloorStart, int FloorCount, int InstanceStart, int InstanceCount, int WhorledStart, int WhorledCount);
        private readonly record struct ForestDraw(IndividualForestChunk Geometry, Vector2 LodRange);
        private sealed class IndividualForestChunk : IDisposable
        {
            internal readonly VertexBuffer Wood = new(PrimitiveTopology.Triangles);
            internal readonly VertexBuffer Crowns = new(PrimitiveTopology.Triangles);
            internal readonly VertexBuffer Floor = new(PrimitiveTopology.Triangles);
            internal readonly ulong[] TileRevisions;
            internal readonly List<ForestGpuTree> Trees = new();
            internal IForestCoverageBuffer Coverage;
            internal int WhorledInstanceStart;
            internal Vector4[] State = Array.Empty<Vector4>();
            internal IForestStateBuffer StateResource;
            internal double GrowthYear;
            internal ulong TerrainVersion, Generation, ForestRevision, EditRevision;
            internal double AnchorYear;
            internal double NextStageYear;
            internal bool Initialized;
            internal bool UploadSuppressed;
            internal readonly HashSet<int> HiddenTiles = new();
            internal readonly List<ForestTileRange> TileRanges = new();
            internal ulong VisibilityVersion = ulong.MaxValue;
            internal bool LightShapeDirty;
            internal long LastUsed;
            internal int VertexCount => Wood.VertexCount + Crowns.VertexCount + Floor.VertexCount
                + (Coverage?.Count ?? 0) * ForestCoverageInstance.IndicesPerPart * 2;
            internal long GpuBytes => Wood.GpuPayloadBytes + Crowns.GpuPayloadBytes + Floor.GpuPayloadBytes + State.LongLength * 16
                + (long)(Coverage?.Count ?? 0) * ForestCoverageInstance.Stride;
            internal long CpuBytes => TileRevisions.LongLength * sizeof(ulong) + (long)Trees.Capacity * Unsafe.SizeOf<ForestGpuTree>()
                + (long)TileRanges.Capacity * Unsafe.SizeOf<ForestTileRange>() + HiddenTiles.Count * sizeof(int)
                + State.LongLength * 16 + Wood.CpuPayloadBytes + Crowns.CpuPayloadBytes + Floor.CpuPayloadBytes;
            internal Dictionary<int, ForestTreeStore.Patch> Snapshot;
            internal IndividualForestChunk(int count) => TileRevisions = new ulong[count];
            public void Dispose() {
                Wood.Dispose(); Crowns.Dispose(); Floor.Dispose();
                StateResource?.Dispose();
                Coverage?.Dispose();
            }
        }

        private readonly Dictionary<(TerrainChunk, ForestLod), IndividualForestChunk> individualForestChunks = new();
        private readonly Dictionary<TerrainChunk, Dictionary<int, ForestTreeStore.Patch>> forestLodSnapshots = new();
        private readonly Dictionary<TerrainChunk, ForestLod> displayedForestLods = new();
        private readonly Dictionary<TerrainChunk, (long Bytes, bool Rejected)> measuredNearMeshes = new();
        private readonly HashSet<TerrainChunk> previouslyVisibleForest = new();
        private readonly Dictionary<TerrainChunk, ForestLodFade> forestLodFades = new();
        private (TerrainChunk, ForestLod)? waitingForestLod;
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
        // Diagnostics can pin every prepared LOD for controlled rendering comparisons.
        internal bool RetainPreparedGeometry { get; private set; }
        private bool residentForestCoverage;
        internal long ForestCacheBudgetBytes { get; set; } = 128L * 1024 * 1024;
        internal long ForestGpuPayloadBytes { get; private set; }
        internal long ForestCpuPayloadBytes { get; private set; }
        internal long ForestBudgetExcessBytes { get; private set; }
        internal int ForestResidentLods => individualForestChunks.Count;
        internal ForestLod CurrentForestLod => generatedForestLod ?? ForestLod.Medium;
        internal bool ForestNearMemoryLimited { get; private set; }
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
        private float previousForestScale, forestZoomOutHold;
        private static readonly ForestLod[] forestLods = Enum.GetValues<ForestLod>();
        private static readonly Vector2 fullForestCoverage = new(0, 1);
        private readonly List<ForestDraw> drawnForestChunks = new();
        // Additional submitted vertices over drawing the more detailed of each active pair.
        // An unusually dense chunk can use the whole transition budget on its own.
        internal const int ForestLodOverlapBudget = 1_500_000;
        internal int ForestLodOverlapVertices { get; private set; }
        internal int ForestLodFadeCount => forestLodFades.Count;
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

        private void TrimForestCache(bool keepShadowMeshes, GraphicsSettings graphics)
        {
            if (ForestCacheBudgetBytes <= 0) throw new InvalidOperationException("Forest cache budget must be positive.");
            long budget = ForestCacheBudgetBytes;
            // Keep recent detailed geometry behind the camera when memory allows.
            // Returning over the same forest should reuse the existing model.
            if (graphics.ForestQuality.Mode != ForestDetailMode.Camera && CurrentForestLod > ForestLod.Far)
                budget = Math.Max(budget, Math.Min(768L * 1024 * 1024, graphics.ForestQuality.NearBudgetBytes / 2));
            // Build scratch is borrowed by one suspended iterator. Release it only after completion/cancellation.
            if (pendingForestBuild == null)
            {
                ReleaseLarge(individualWood); ReleaseLarge(individualCrowns); ReleaseLarge(individualFloor);
                ReleaseLarge(individualWoodGrowth); ReleaseLarge(individualCrownGrowth); ReleaseLarge(individualFloorGrowth);
            }
            MeasureForestPayload();
            if (!StreamGeometry || RetainPreparedGeometry)
            {
                ForestBudgetExcessBytes = Math.Max(0, ForestCpuPayloadBytes + ForestGpuPayloadBytes - budget);
                return;
            }
            long coarseBytes = 0;
            foreach (var (key, geometry) in individualForestChunks)
                if (key.Item2 == ForestLod.Far) coarseBytes += geometry.CpuBytes + geometry.GpuBytes;
            while (ForestCpuPayloadBytes + ForestGpuPayloadBytes > budget)
            {
                (TerrainChunk Chunk, ForestLod Lod) victim = default;
                long oldest = long.MaxValue;
                foreach (var entry in individualForestChunks)
                {
                    if (pendingForestBuild != null && entry.Key == (pendingForestBuild.Chunk, pendingForestBuild.Lod)) continue;
                    if (entry.Value.Coverage != null) continue; // Whole-map base layer, independent of detail residency.
                    // Never release resources while they are the frame's visible continuity fallback.
                    var (chunk, lod) = entry.Key;
                    if (visibleChunkSet.Contains(chunk) &&
                        ((displayedForestLods.TryGetValue(chunk, out var displayed) && displayed == lod)
                            || (forestLodFades.TryGetValue(chunk, out var fade) && (fade.Low == lod || fade.High == lod))
                            || waitingForestLod == entry.Key)) continue;
                    // Small maps can keep a cheap coarse fallback under its own share of
                    // the budget. The depth pass can also reuse it without a new mesh.
                    if (entry.Key.Item2 == ForestLod.Far && (coarseBytes <= budget / 2
                        || keepShadowMeshes && visibleChunkSet.Contains(entry.Key.Item1))) continue;
                    if (entry.Value.LastUsed < oldest) { victim = entry.Key; oldest = entry.Value.LastUsed; }
                }
                if (victim.Chunk == null) break;
                var geometry = individualForestChunks[victim];
                if (victim.Lod == ForestLod.Far) coarseBytes -= geometry.CpuBytes + geometry.GpuBytes;
                geometry.Dispose(); individualForestChunks.Remove(victim);
                ForestCacheEvictions++;
                if (displayedForestLods.TryGetValue(victim.Chunk, out var previous) && previous == victim.Lod)
                    displayedForestLods.Remove(victim.Chunk);
                bool retained = pendingForestBuild?.Chunk == victim.Chunk && !UsesForestCoverage(pendingForestBuild.Lod);
                foreach (var lod in forestLods)
                    retained |= individualForestChunks.TryGetValue((victim.Chunk, lod), out var cached) && cached.Coverage == null;
                if (!retained) forestLodSnapshots.Remove(victim.Chunk);
                MeasureForestPayload();
            }
            // The visible camera-selected meshes and one replacement are the irreducible working set.
            // Expose its excess instead of silently hiding trees or claiming a hard whole-process memory limit.
            ForestBudgetExcessBytes = Math.Max(0, ForestCpuPayloadBytes + ForestGpuPayloadBytes - budget);
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
            if (lod != ForestLod.Far && graphics.Enhanced && graphics.Lighting && graphics.Shadows) {
                foreach (var chunk in visibleChunks) {
                    if (individualForestChunks.TryGetValue((chunk, ForestLod.Far), out var cached) && IsFresh(cached, chunk, forest, graphics)) continue;
                    GetIndividualForestChunk(chunk, forest, graphics, ForestLod.Far, true, out _, preload: true);
                    return;
                }
            }
            if (StreamGeometry && ForestCpuPayloadBytes + ForestGpuPayloadBytes >= ForestCacheBudgetBytes * 3 / 4) return;
            foreach (var other in forestLods)
            {
                if (Math.Abs((int)other - (int)lod) != 1) continue;
                if (other > lod && graphics.ForestQuality.Mode != ForestDetailMode.Camera) continue;
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
                    foreach (var lod in forestLods) GetIndividualForestChunk(chunk, forest, graphics, lod, false, out _);
            }
            finally { SynchronousForestBuilds = synchronous; }
        }

        internal IEnumerable<float> PrepareMapGeometry(ForestSystem forest, GraphicsSettings graphics, bool completeForestCache = false)
        {
            RetainPreparedGeometry = completeForestCache;
            residentForestCoverage = !completeForestCache;
            CancelForestBuild();
            forestLodFades.Clear();
            previouslyVisibleForest.Clear();
            previousForestScale = forestZoomOutHold = 0;
            waitingForestLod = null;
            int completed = 0, total = chunkIndex.Chunks.Count * (completeForestCache ? 4 : 2);
            foreach (var chunk in chunkIndex.Chunks)
            {
                DrawCachedTerrain(new[] { chunk }, false);
                yield return (float)++completed / total;
                foreach (var lod in forestLods) {
                    if (!completeForestCache && lod != ForestLod.Far) break;
                    foreach (var _ in PrepareForestLevel(chunk, forest, graphics, lod)) yield return (float)completed / total;
                    displayedForestLods[chunk] = lod;
                    yield return (float)++completed / total;
                }
            }
            TrimForestCache(graphics.Enhanced && graphics.Shadows, graphics);
            TrimStaticCache();
        }

        internal IEnumerable<float> PrepareForestView(ForestSystem forest, GraphicsSettings graphics, RenderContext view)
        {
            CancelForestBuild();
            UpdateVisibleTiles(view);
            var target = SelectForestDetail(graphics, view);
            int completed = 0, total = Math.Max(1, visibleChunks.Count);
            foreach (var chunk in visibleChunks) {
                foreach (var _ in PrepareForestLevel(chunk, forest, graphics, ForestLod.Far)) yield return (float)completed / total;
                foreach (var _ in PrepareForestLevel(chunk, forest, graphics, target)) yield return (float)completed / total;
                displayedForestLods[chunk] = target;
                yield return (float)++completed / total;
            }
            TrimForestCache(graphics.Enhanced && graphics.Shadows, graphics);
        }

        private ForestLod SelectForestDetail(GraphicsSettings graphics, RenderContext view)
        {
            var quality = graphics.ForestQuality;
            var target = quality.Select(view.PixelsPerWorldUnit, generatedForestLod);
            ForestNearMemoryLimited = target == ForestLod.Near && quality.Mode != ForestDetailMode.Camera &&
                !NearFitsBudget(quality.NearBudgetBytes);
            if (!ForestNearMemoryLimited) return target;
            quality.LimitNearForMemory(view.TotalTimeSeconds);
            return ForestLod.Medium;
        }

        internal ForestFrameWorkload GetForestFrameWorkload(GraphicsSettings graphics)
        {
            var lod = CurrentForestLod;
            bool ready = visibleChunks.Count != 0 && forestLodFades.Count == 0 &&
                ReadyVisibleForestChunks(lod) == visibleChunks.Count;
            return new(lod, ready, lod == ForestLod.Medium && ready && NearFitsBudget(graphics.ForestQuality.NearBudgetBytes));
        }

        private bool NearFitsBudget(long budget)
        {
            long projected = ForestGpuPayloadBytes -
                (pendingForestBuild?.Lod == ForestLod.Near ? pendingForestBuild.Geometry.GpuBytes : 0);
            foreach (var chunk in visibleChunks) {
                bool measured = measuredNearMeshes.TryGetValue(chunk, out var prior);
                if (individualForestChunks.TryGetValue((chunk, ForestLod.Near), out var near) && near.Initialized && !prior.Rejected)
                    continue; // Already counted in the measured payload.
                if (measured) projected += prior.Bytes;
                else if (individualForestChunks.TryGetValue((chunk, ForestLod.Medium), out var medium) && medium.Initialized)
                    projected += 6 * medium.GpuBytes;
                else if (individualForestChunks.TryGetValue((chunk, ForestLod.Far), out var far))
                    projected += far.Coverage != null ? far.Coverage.Count * 192L * 1024 : 16 * far.GpuBytes;
                if (projected > budget) return false;
            }
            return projected <= budget;
        }

        private IEnumerable<bool> PrepareForestLevel(TerrainChunk chunk, ForestSystem forest, GraphicsSettings graphics, ForestLod lod)
        {
            var key = (chunk, lod);
            if (individualForestChunks.TryGetValue(key, out var geometry) && geometry.Initialized &&
                (geometry.Coverage != null) != UsesForestCoverage(lod)) {
                geometry.Dispose();
                individualForestChunks.Remove(key);
                geometry = null;
            }
            if (geometry == null) individualForestChunks.Add(key, geometry = new(chunk.TileIds.Length));
            if (IsFresh(geometry, chunk, forest, graphics)) yield break;
            foreach (bool step in BuildIndividualForestChunk(chunk, geometry, forest, graphics, lod)) yield return step;
            UpdateIndividualForestState(geometry, forest);
            geometry.ForestRevision = forest.Revision;
            SetIndividualForestElapsed(geometry, forest.ForestYear);
            geometry.Initialized = true;
            TotalForestChunkRebuilds++;
        }

        private bool UsesForestCoverage(ForestLod lod) => residentForestCoverage && lod == ForestLod.Far;



        // Native regression diagnostic: all current LODs must share a morphology epoch and GPU clock.
        internal int CheckForestLodConsistency(ForestSystem forest, GraphicsSettings graphics)
        {
            int compared = 0;
            foreach (var chunk in chunkIndex.Chunks)
            {
                IndividualForestChunk basis = null;
                foreach (var lod in forestLods)
                {
                    if (!individualForestChunks.TryGetValue((chunk, lod), out var geometry) || geometry.Coverage != null
                        || !IsFresh(geometry, chunk, forest, graphics)) continue;
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
                || (!UsesForestCoverage(lod) && forestLodSnapshots.TryGetValue(chunk, out var snapshot) && !ReferenceEquals(snapshot, geometry.Snapshot))
                || forest.ForestYear >= geometry.NextStageYear
                || geometry.Generation != forest.IndividualTrees.Generation;
            bool topologyChanged = changed;
            if (changed || geometry.ForestRevision != forest.Revision)
            {
                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    ulong revision = forest.IndividualTrees.TryGet(chunk.TileIds[i], out var patch) ? patch.TopologyRevision : 0;
                    changed |= geometry.TileRevisions[i] != revision;
                    if (geometry.TileRevisions[i] != 0 && roads.Has(chunk.TileIds[i])) geometry.HiddenTiles.Add(chunk.TileIds[i]);
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
                // Keep neighbouring trees visible while rebuilding. Only the road tiles' cached
                // ranges are hidden immediately, including their floor decals, dead wood and stumps.
                bool edited = geometry.Initialized && geometry.Generation == forest.IndividualTrees.Generation
                    && (geometry.TerrainVersion != chunk.PropVersion || (geometry.EditRevision != forest.EditRevision && topologyChanged));
                bool immediate = SynchronousForestBuilds;
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
                || (geometry.Coverage == null &&
                    (!forestLodSnapshots.TryGetValue(chunk, out var snapshot) || !ReferenceEquals(snapshot, geometry.Snapshot)))
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
                return target;
            }
            if (wanted?.Coverage != null && wanted.Generation == forest.IndividualTrees.Generation) {
                GetIndividualForestChunk(chunk, forest, graphics, target, true, out _);
                return target;
            }
            ForestLod? fallback = null;
            int bestDistance = int.MaxValue;
            // Preserve the actual image in either zoom direction until the destination is ready.
            if (displayedForestLods.TryGetValue(chunk, out var retained)
                && DisplayableForestChunk(chunk, retained, forest) != null)
            {
                fallback = retained;
            }
            else foreach (var lod in forestLods)
            {
                if (lod == target || !individualForestChunks.TryGetValue((chunk, lod), out var other)) continue;
                int distance = Math.Abs((int)lod - (int)target);
                if (distance < bestDistance && IsFresh(other, chunk, forest, graphics)) { fallback = lod; bestDistance = distance; }
            }
            // An older resident mesh is still a better image than a missing patch while
            // its topology refresh waits for the single streaming worker.
            if (fallback == null && DisplayableForestChunk(chunk, ForestLod.Far, forest)?.Coverage != null)
                fallback = ForestLod.Far;
            GetIndividualForestChunk(chunk, forest, graphics, target, fallback.HasValue, out bool current);
            return current || fallback is null ? target : fallback.Value;
        }

        private IndividualForestChunk DisplayableForestChunk(TerrainChunk chunk, ForestLod lod, ForestSystem forest) =>
            individualForestChunks.TryGetValue((chunk, lod), out var geometry) && geometry.Initialized
                && geometry.Generation == forest.IndividualTrees.Generation ? geometry : null;

        private static object springTintBook = new();

        /// <summary>The spring colour of every species as a multiplier of its summer colour, for the crown shader.</summary>
        private static void UpdateSpringTints()
        {
            object book = ForestSpeciesBook.CaptureContext();
            if (ReferenceEquals(book, springTintBook)) return;
            springTintBook = book;
            var tints = new float[18 * 3]; Array.Fill(tints, 1f);
            foreach (ForestSpecies species in Enum.GetValues<ForestSpecies>()) {
                int code = TreeScale.CrownSpeciesCode(species) - 224;
                if (species == ForestSpecies.None || code < 0 || code >= 18) continue;
                var (spring, summer, _, _, _) = TreeForm.Colours(species);
                tints[code * 3] = spring.R / (float)Math.Max(1, (int)summer.R);
                tints[code * 3 + 1] = spring.G / (float)Math.Max(1, (int)summer.G);
                tints[code * 3 + 2] = spring.B / (float)Math.Max(1, (int)summer.B);
            }
            ForesTycoon.Rendering.OpenGl.OpenGlVertexBuffer.ForestSpringTints = tints;
        }

        private static void SetIndividualForestElapsed(IndividualForestChunk geometry, double year)
        {
            UpdateSpringTints();
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
            bool obsolete = !visibleChunkSet.Contains(build.Chunk) || geometry.Generation != forest.IndividualTrees.Generation
                || (!UsesForestCoverage(build.Lod) &&
                    (!forestLodSnapshots.TryGetValue(build.Chunk, out var snapshot) || !ReferenceEquals(snapshot, geometry.Snapshot)))
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
                        if (geometry.UploadSuppressed) {
                            CancelForestBuild();
                            return;
                        }
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
                    if (!build.Work.Current) return; // The CPU mesh worker has no result to consume yet.
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
            if (geometry.Coverage != null) return; // Absolute dimensions and annual rates are instance attributes.
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
            float crownFraction = TreeLifeStages.CrownFraction(tree.Species,
                TreeLifeStages.Of(tree.Species, tree.Age(year)));
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
            bool coverage = UsesForestCoverage(lod);
            var instances = coverage ? new List<ForestCoverageInstance>() : null;
            var whorledInstances = coverage ? new List<ForestCoverageInstance>() : null;
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
            // Every LOD is a tessellation of the same frozen tree/site snapshot. Building a
            // missing LOD from today's dimensions would change morphology when zooming.
            IndividualForestChunk basis = null;
            if (!coverage)
                foreach (var level in forestLods)
                    if (individualForestChunks.TryGetValue((chunk, level), out var cached) && cached.Coverage == null &&
                        !ReferenceEquals(cached, geometry) && IsFresh(cached, chunk, forest, graphics))
                    { basis = cached; break; }
            geometry.AnchorYear = basis?.AnchorYear ?? forest.ForestYear;
            geometry.UploadSuppressed = false;
            // Re-anchor resident dimensions, colour and growth rates periodically. The previous
            // instances remain usable during the refresh and animate between publications.
            geometry.NextStageYear = coverage ? geometry.AnchorYear + .25 : double.PositiveInfinity;
            geometry.Trees.Clear();
            geometry.TileRanges.Clear(); geometry.HiddenTiles.Clear();
            geometry.VisibilityVersion = ulong.MaxValue;
            geometry.TerrainVersion = chunk.PropVersion;
            geometry.Generation = forest.IndividualTrees.Generation;
            geometry.EditRevision = forest.EditRevision;
            var snapshots = coverage ? null : basis?.Snapshot ?? new Dictionary<int, ForestTreeStore.Patch>();
            geometry.Snapshot = snapshots;
            if (!coverage) forestLodSnapshots[chunk] = snapshots;
            for (int i = 0; i < chunk.TileIds.Length; i++)
            {
                int id = chunk.TileIds[i];
                geometry.TileRevisions[i] = 0;
                if (!forest.IndividualTrees.TryGet(id, out var source)) continue;
                geometry.TileRevisions[i] = source.TopologyRevision;
                if (coverage || basis != null) continue;
                var snapshot = new ForestTreeStore.Patch(source.Count) { Count = source.Count };
                Array.Copy(source.Trees, snapshot.Trees, source.Count);
                if (source.DeadTrees != null) snapshot.DeadTrees = new(source.DeadTrees);
                if (source.Stumps != null) snapshot.Stumps = new(source.Stumps);
                snapshots.Add(id, snapshot);
            }
            yield return true; // Snapshot publication is a separate work unit from any tree generation.
            const int tilesPerMeshBatch = 16;
            const int meshesPerBatch = 64;
            for (int begin = 0; begin < chunk.TileIds.Length;)
            {
                int limit = Math.Min(chunk.TileIds.Length, begin + tilesPerMeshBatch);
                int end = begin;
                var requests = new List<ForestMeshInput>();
                while (end < limit && requests.Count < meshesPerBatch) {
                    int id = chunk.TileIds[end++];
                    if (!TryGetPatch(id, out var patch)) continue;
                    Tile tile = tiles[id];
                    if (!coverage) {
                        for (int i = 0; i < patch.Count; i++) {
                            ForestTree tree = patch.Trees[i];
                            var stem = IndividualStem(tile, tree with { Health = 1 }, geometry.AnchorYear);
                            var site = ForestTreeSites.Gap(patch.Trees, patch.Count, i, tileSizeH / TreeMetresToWorld,
                                tileSizeV / TreeMetresToWorld, geometry.AnchorYear, ((IForestHabitat)map).GetNormalizedElevation(id))
                                with { Space = CrownSpaceOf(tile, tree, forest, snapshots, geometry.AnchorYear) };
                            var spec = TreeShapeSpec.From(tree, geometry.AnchorYear, site, stem.Yaw) with { Leaves = LeafState.Full };
                            requests.Add(new(spec, site, stem, lod));
                        }
                    }
                    if (patch.DeadTrees != null)
                        foreach (var dead in patch.DeadTrees) {
                            var stem = IndividualStem(tile, dead.Tree, dead.DeathYear);
                            var spec = TreeShapeSpec.From(dead.Tree, dead.DeathYear, TreeSite.Open, stem.Yaw, dead: true);
                            requests.Add(new(spec, TreeSite.Open, stem, ForestLod.Near));
                        }
                    yield return true;
                }
                var meshes = new DendroTreeGenerator.Mesh[requests.Count];
                foreach (bool unit in GenerateTreeMeshBatch(requests, meshes)) yield return unit;

                int nextMesh = 0;
                for (int tileIndex = begin; tileIndex < end; tileIndex++) {
                    int id = chunk.TileIds[tileIndex];
                    if (!TryGetPatch(id, out var patch)) continue;
                    int woodStart = individualWood.Count, crownStart = individualCrowns.Count, floorStart = individualFloor.Count;
                    int instanceStart = instances?.Count ?? 0;
                    int whorledStart = whorledInstances?.Count ?? 0;
                    Tile tile = tiles[id];
                    for (int i = 0; i < patch.Count; i++) {
                        ForestTree tree = patch.Trees[i];
                        var stage = TreeLifeStages.Of(tree.Species, tree.Age(geometry.AnchorYear));
                        var phase = TreeLifePhases.Of(tree.Species, tree.Age(geometry.AnchorYear));
                        geometry.NextStageYear = Math.Min(geometry.NextStageYear,
                            tree.BirthYear + Math.Min(TreeLifePhases.NextAge(tree.Species, phase), TreeLifeStages.NextAge(tree.Species, stage)));
                        if (coverage) {
                            var instance = CoverageOf(tile, tree, geometry.AnchorYear, phase, out bool whorled);
                            (whorled ? whorledInstances : instances).Add(instance);
                            continue;
                        }
                        var input = requests[nextMesh];
                        var mesh = meshes[nextMesh++];
                        var stem = input.Stem;
                        var size = input.Spec.Size;
                        float radial = tree.AnnualGrowth.Diameter / size.Diameter;
                        float vertical = tree.AnnualGrowth.Height / size.Height;
                        float crown = tree.AnnualGrowth.CrownRadius / size.CrownRadius;
                        var origin = new Vector3(stem.X, stem.Y, stem.BaseZ);
                        int slot = geometry.Trees.Count;
                        var season = ForestSeasonRenderState.Create(input.Spec);
                        geometry.Trees.Add(new(id, i, tree.Id, size, input.Spec.ShapeKey, input.Site, season.Tint, season.Bounds));
                        AppendAt(individualWood, mesh.Trunk, origin);
                        Repeat(individualWoodGrowth, mesh.Trunk.Length, new(origin, new Vector3(radial, radial, vertical), slot));
                        AppendAt(individualWood, mesh.Branches, origin);
                        Repeat(individualWoodGrowth, mesh.Branches.Length, new(origin, new Vector3(-1, crown, vertical), slot));
                        AppendAt(individualCrowns, mesh.Crown, origin);
                        // A negative rate component selects crown scaling instead of bole scaling.
                        Repeat(individualCrownGrowth, mesh.Crown.Length, new(origin, new Vector3(-1, crown, vertical), slot));
                        if (lod != ForestLod.Far) {
                            var floor = DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Triangles, () => DrawForestFloor(stem));
                            individualFloor.AddRange(floor);
                            Repeat(individualFloorGrowth, floor.Length, default);
                        }
                        yield return true;
                    }
                    if (patch.DeadTrees != null)
                        foreach (var dead in patch.DeadTrees) {
                            var stem = requests[nextMesh].Stem;
                            var mesh = meshes[nextMesh++];
                            var root = new Vector3(stem.X, stem.Y, stem.BaseZ);
                            AppendAt(individualWood, mesh.Trunk, root);
                            AppendAt(individualWood, mesh.Branches, root);
                            Repeat(individualWoodGrowth, mesh.Trunk.Length + mesh.Branches.Length, ForestVertexGrowth.DeadWood(
                                root, dead.DeathYear, dead.Tree.Dimensions.Diameter * TreeMetresToWorld, stem.Yaw));
                            yield return true;
                        }
                    if (patch.Stumps != null) foreach (var stump in patch.Stumps) {
                        geometry.NextStageYear = Math.Min(geometry.NextStageYear, geometry.AnchorYear + 1.0 / 12);
                        TreeInstance stem = IndividualStem(tile, stump.Felled, stump.FelledYear);
                        var wood = DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Quads, () => DrawStump(stem, (float)stump.DecayProgress));
                        individualWood.AddRange(wood);
                        Repeat(individualWoodGrowth, wood.Length, new(Vector3.Zero, Vector3.Zero));
                        yield return true;
                    }
                    geometry.TileRanges.Add(new(id, woodStart, individualWood.Count - woodStart,
                        crownStart, individualCrowns.Count - crownStart, floorStart, individualFloor.Count - floorStart,
                        instanceStart, (instances?.Count ?? 0) - instanceStart,
                        whorledStart, (whorledInstances?.Count ?? 0) - whorledStart));
                    if (coverage) yield return true;
                }
                begin = end;
            }
            if (coverage) {
                geometry.Coverage ??= SceneRenderBackends.Current.CreateForestCoverageBuffer();
                var data = new ForestCoverageInstance[instances.Count + whorledInstances.Count];
                instances.CopyTo(data);
                whorledInstances.CopyTo(data, instances.Count);
                geometry.Coverage.SetData(data);
                geometry.WhorledInstanceStart = instances.Count;
                geometry.GrowthYear = geometry.AnchorYear;
                yield return true;
            }
            if (lod == ForestLod.Near && graphics.ForestQuality.Mode != ForestDetailMode.Camera &&
                ReferenceEquals(pendingForestBuild?.Geometry, geometry)) {
                long actual = ((long)individualWood.Count + individualCrowns.Count + individualFloor.Count) *
                    (Vertex.Stride + ForestVertexGrowth.Stride) + geometry.Trees.Count * 64L;
                measuredNearMeshes[chunk] = (actual, true);
                if (!NearFitsBudget(graphics.ForestQuality.NearBudgetBytes)) {
                    geometry.UploadSuppressed = true;
                    yield break;
                }
                measuredNearMeshes[chunk] = (actual, false);
            }
            foreach (var page in geometry.Wood.UploadForestPages(individualWood, individualWoodGrowth)) yield return page;
            foreach (var page in geometry.Crowns.UploadForestPages(individualCrowns, individualCrownGrowth)) yield return page;
            foreach (var page in geometry.Floor.UploadForestPages(individualFloor, individualFloorGrowth)) yield return page;

            bool TryGetPatch(int id, out ForestTreeStore.Patch patch)
            {
                patch = null;
                return !roads.Has(id) && (coverage ? forest.IndividualTrees.TryGet(id, out patch) : snapshots.TryGetValue(id, out patch));
            }

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

        private readonly record struct ForestMeshInput(TreeShapeSpec Spec, TreeSite Site, TreeInstance Stem, ForestLod Lod);

        private IEnumerable<bool> GenerateTreeMeshBatch(List<ForestMeshInput> requests, DendroTreeGenerator.Mesh[] meshes)
        {
            if (requests.Count == 0) yield break;
            if (SynchronousForestBuilds) {
                for (int i = 0; i < requests.Count; i++) meshes[i] = Generate(requests[i]);
                yield break;
            }
            object species = ForestSpeciesBook.CaptureContext();
            var cancellation = new System.Threading.CancellationTokenSource();
            var options = new ParallelOptions {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 2, 1, 4),
                CancellationToken = cancellation.Token
            };
            var worker = Task.Run(() => Parallel.For(0, requests.Count, options, i =>
                meshes[i] = ForestSpeciesBook.WithContext(species, () => Generate(requests[i]))), cancellation.Token);
            try {
                while (SimulationWork.TaskPending(worker)) yield return false;
                worker.GetAwaiter().GetResult();
            }
            finally {
                if (worker.IsCompleted) cancellation.Dispose();
                else {
                    cancellation.Cancel();
                    _ = worker.ContinueWith(completed => {
                        _ = completed.Exception;
                        cancellation.Dispose();
                    }, System.Threading.CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }

            static DendroTreeGenerator.Mesh Generate(ForestMeshInput input) => input.Spec.Dead
                ? DendroTreeGenerator.Generate(input.Spec, input.Lod)
                : DendroTreeGenerator.GenerateSeasonal(input.Spec, input.Lod);
        }

        private void DrawIndividualTrees(ForestSystem forest, RenderContext context, GraphicsSettings graphics)
        {
            graphics ??= defaultPineGraphics;
            bool shadow = RenderDevice.Visuals?.ShadowPass == true;
            drawnForestChunks.Clear();
            if (shadow) {
                var target = ForestLodPolicy.SelectShadow(context.PixelsPerWorldUnit);
                foreach (var chunk in visibleChunks) {
                    var geometry = ShadowForestChunk(chunk, target, forest, graphics);
                    if (geometry != null) drawnForestChunks.Add(new(geometry, fullForestCoverage));
                }
            } else {
                var lod = SelectForestDetail(graphics, context);
                if (lod != generatedForestLod && pendingForestBuild is { Priority: false, Preload: false } build && build.Lod != lod)
                    CancelForestBuild();
                generatedForestLod = lod;
                AdvanceForestBuild(forest, graphics);
                bool holdRefinements = HoldForestRefinements(context);
                CollectForestDraws(forest, graphics, lod, context, holdRefinements);
                if (!holdRefinements && waitingForestLod == null) PrepareNeighbouringLods(forest, graphics, lod);
            }
            var previousCoverage = RenderDevice.LodRange;
            try {
                if (!shadow) {
                    if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.ForestFloor;
                    using (RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-1, -1))
                        DrawForestPass(2);
                }
                if (!graphics.HideTrees) {
                    if (RenderDevice.Visuals != null) RenderDevice.Visuals.Kind = SurfaceKind.Wood;
                    DrawForestPass(0);
                    // Front foliage shells keep the texture's gaps open in both the color and depth passes.
                    using (RenderDevice.CreateStateScope().Cull(RenderCullFace.Back))
                        DrawForestPass(1, shadow);
                }
            } finally {
                RenderDevice.LodRange = previousCoverage;
            }
            if (!shadow) TrimForestCache(!graphics.HideTrees && graphics.Enhanced && graphics.Lighting && graphics.Shadows, graphics);
        }

        private bool HoldForestRefinements(RenderContext context)
        {
            if (!residentForestCoverage || context.DeltaTimeSeconds <= 0) return false;
            float scale = context.PixelsPerWorldUnit;
            // While the camera is pulling back, publish coverage for the new area together.
            // Detail workers resume after the view has settled to avoid a moving tile front.
            if (scale < previousForestScale * .995f)
                forestZoomOutHold = .18f;
            else
                forestZoomOutHold = Math.Max(0, forestZoomOutHold - Math.Min(.05f, context.DeltaTimeSeconds));
            previousForestScale = scale;
            return forestZoomOutHold > 0;
        }

        private void CollectForestDraws(ForestSystem forest, GraphicsSettings graphics, ForestLod target, RenderContext context,
            bool holdRefinements)
        {
            waitingForestLod = null;
            ForestLodOverlapVertices = 0;
            // Stills and tools may issue an untimed draw at any zoom to capture an exact endpoint.
            bool timed = context.DeltaTimeSeconds > 0;
            if (!timed) forestLodFades.Clear();

            // Existing fades get first claim on the overlap budget. A camera reversal can
            // turn a fade around in place, with exactly the same pixel mask at that instant.
            foreach (var (chunk, fade) in forestLodFades) {
                var low = DisplayableForestChunk(chunk, fade.Low, forest);
                var high = DisplayableForestChunk(chunk, fade.High, forest);
                if (!visibleChunkSet.Contains(chunk) || low == null || high == null) {
                    forestLodFades.Remove(chunk);
                    continue;
                }
                var next = fade.Advance(target, context.DeltaTimeSeconds);
                if (next.Endpoint is ForestLod endpoint) {
                    displayedForestLods[chunk] = endpoint;
                    forestLodFades.Remove(chunk);
                    continue;
                }
                forestLodFades[chunk] = next;
                ForestLodOverlapVertices += Math.Min(low.VertexCount, high.VertexCount);
                KeepCurrent(chunk, low);
                KeepCurrent(chunk, high);
                drawnForestChunks.Add(new(low, new(next.HighCoverage, 1)));
                drawnForestChunks.Add(new(high, new(0, next.HighCoverage)));
            }

            foreach (var chunk in visibleChunks) {
                if (forestLodFades.ContainsKey(chunk)) continue;
                if (residentForestCoverage && !previouslyVisibleForest.Contains(chunk)) {
                    if (graphics.ForestQuality.Mode != ForestDetailMode.Camera &&
                        individualForestChunks.TryGetValue((chunk, target), out var cached) && IsFresh(cached, chunk, forest, graphics))
                        displayedForestLods[chunk] = target;
                    else if (holdRefinements && DisplayableForestChunk(chunk, ForestLod.Far, forest)?.Coverage != null)
                        displayedForestLods[chunk] = ForestLod.Far;
                }
                bool hadDisplay = displayedForestLods.TryGetValue(chunk, out var retained);
                var previous = hadDisplay ? DisplayableForestChunk(chunk, retained, forest) : null;
                // Keep just one ready destination waiting for admission. Neighbouring chunks
                // keep their current meshes while it waits, without building an unbounded queue.
                bool keepCoverage = holdRefinements && previous?.Coverage != null && target != ForestLod.Far;
                var lod = keepCoverage || (waitingForestLod != null && previous != null && retained != target)
                    ? retained : ResolveForestLod(chunk, forest, graphics, target);
                var selected = DisplayableForestChunk(chunk, lod, forest);
                if (selected != null && previous != null && lod != retained && timed &&
                    (previous.VertexCount != 0 || selected.VertexCount != 0)) {
                    int overlap = Math.Min(previous.VertexCount, selected.VertexCount);
                    if (ForestLodOverlapVertices == 0 || overlap <= ForestLodOverlapBudget - ForestLodOverlapVertices) {
                        forestLodFades.Add(chunk, ForestLodFade.Start(retained, lod));
                        ForestLodOverlapVertices += overlap;
                    } else {
                        waitingForestLod = (chunk, lod);
                    }
                    // This frame is exactly the old endpoint. Start mixing on the next frame.
                    lod = retained;
                    selected = previous;
                }
                displayedForestLods[chunk] = lod;
                if (selected == null) continue;
                if (lod != target) KeepCurrent(chunk, selected);
                drawnForestChunks.Add(new(selected, fullForestCoverage));
            }
            if (residentForestCoverage) {
                previouslyVisibleForest.Clear();
                foreach (var chunk in visibleChunks) previouslyVisibleForest.Add(chunk);
            }

            void KeepCurrent(TerrainChunk chunk, IndividualForestChunk geometry)
            {
                if (!IsFresh(geometry, chunk, forest, graphics)) SetIndividualForestElapsed(geometry, forest.ForestYear);
                geometry.LastUsed = ++forestUseSequence;
            }
        }

        private void DrawForestPass(int part, bool shadow = false)
        {
            RenderDevice.LodRange = fullForestCoverage;
            if (part == 1) forestMaterial.Use();
            foreach (var draw in drawnForestChunks) {
                if (RenderDevice.LodRange != draw.LodRange) {
                    RenderDevice.LodRange = draw.LodRange;
                    if (part == 1) forestMaterial.Use();
                }
                DrawForestPart(draw.Geometry, part, shadow);
            }
            if (part == 1 && RenderDevice.LodRange != fullForestCoverage) {
                RenderDevice.LodRange = fullForestCoverage;
                forestMaterial.Use();
            }
        }

        private IndividualForestChunk ShadowForestChunk(TerrainChunk chunk, ForestLod requested, ForestSystem forest, GraphicsSettings graphics)
        {
            // Prefer an existing coarse mesh in the shadow pass. It never drives camera LOD hysteresis or builds geometry.
            var wanted = Find(requested);
            if (wanted != null) return wanted;
            foreach (var lod in forestLods) {
                if (lod == requested) continue;
                var fallback = Find(lod);
                if (fallback != null) return fallback;
            }
            return null;

            IndividualForestChunk Find(ForestLod lod) {
                if (!individualForestChunks.TryGetValue((chunk, lod), out var geometry) || !geometry.Initialized
                    || geometry.Generation != forest.IndividualTrees.Generation) return null;
                if (!IsFresh(geometry, chunk, forest, graphics)) SetIndividualForestElapsed(geometry, forest.ForestYear);
                return geometry;
            }
        }

        private void DrawForestPart(IndividualForestChunk geometry, int part, bool shadow)
        {
            // A displayed fallback LOD may differ from the LOD currently being rebuilt.
            // Apply road clearing to whichever cached level is actually drawn.
            if (geometry.VisibilityVersion != map.SurfaceVersion) {
                foreach (var range in geometry.TileRanges) if (roads.Has(range.TileId)) geometry.HiddenTiles.Add(range.TileId);
                geometry.VisibilityVersion = map.SurfaceVersion;
            }
            var buffer = part == 0 ? geometry.Wood : part == 1 ? geometry.Crowns : geometry.Floor;
            if (geometry.Coverage is { Count: > 0 } coverage && part != 2) {
                var standard = part == 0 ? ForestCoveragePart.Bole : ForestCoveragePart.Crown;
                bool tiered = part == 1 && !shadow;
                int whorledStart = geometry.WhorledInstanceStart;
                if (!tiered && geometry.HiddenTiles.Count == 0)
                    DrawCoverage(0, coverage.Count, standard);
                else {
                    DrawVisibleRanges(0, whorledStart, standard);
                    DrawVisibleRanges(whorledStart, coverage.Count - whorledStart,
                        tiered ? ForestCoveragePart.WhorledCrown : standard, whorledRange: true);
                }
            }
            DrawVisibleRanges(0, buffer.VertexCount);

            void DrawCoverage(int first, int count, ForestCoveragePart coveragePart) =>
                geometry.Coverage.Draw(first, count, coveragePart, geometry.Wood.ForestElapsedYears, geometry.Wood.ForestCurrentYear);

            void DrawVisibleRanges(int offset, int length, ForestCoveragePart? coveragePart = null, bool whorledRange = false)
            {
                if (length == 0) return;
                if (geometry.HiddenTiles.Count == 0) {
                    Draw(offset, length);
                    return;
                }
                int start = 0, count = 0;
                foreach (var range in geometry.TileRanges) {
                    if (geometry.HiddenTiles.Contains(range.TileId)) {
                        if (count > 0) Draw(start, count);
                        count = 0;
                        continue;
                    }
                    var (tileStart, tileCount) = coveragePart.HasValue
                        ? whorledRange ? (range.WhorledStart, range.WhorledCount) : (range.InstanceStart, range.InstanceCount)
                        : part switch {
                            0 => (range.WoodStart, range.WoodCount),
                            1 => (range.CrownStart, range.CrownCount),
                            _ => (range.FloorStart, range.FloorCount)
                        };
                    if (count == 0) start = offset + tileStart;
                    count += tileCount;
                }
                if (count > 0) Draw(start, count);

                void Draw(int first, int countToDraw)
                {
                    if (coveragePart is { } kind) DrawCoverage(first, countToDraw, kind);
                    else buffer.DrawRange(first, countToDraw, part != 1);
                }
            }
        }



        private IEnumerable<int> DisposeIndividualForestWork()
        {
            CancelForestBuild();
            foreach (var chunk in individualForestChunks.Values) { chunk.Dispose(); yield return 1; }
            individualForestChunks.Clear();
            forestLodSnapshots.Clear();
            displayedForestLods.Clear();
            measuredNearMeshes.Clear();
            previouslyVisibleForest.Clear();
            forestLodFades.Clear();
            waitingForestLod = null;
            individualWood.Clear(); individualCrowns.Clear(); individualFloor.Clear();
            individualWoodGrowth.Clear(); individualCrownGrowth.Clear(); individualFloorGrowth.Clear();
            measuredForestSnapshots.Clear();
            ForestGpuPayloadBytes = ForestCpuPayloadBytes = ForestBudgetExcessBytes = 0;
        }
    }
}
