using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal readonly record struct ForestTileGeometry(float X, float Y, float Width, float Height);
    internal readonly record struct ForestResources(float Light, float Water, float Space)
    {
        internal float LightResponse(ForestSpecies species, ForesTycoon.Rules.EcologyResponseProgram rules = null) =>
            (float)(rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default).LightResponse(Light, Water, Space, ForestSpeciesProfile.For(species).ShadeTolerance);
        internal float Limitation(ForestSpecies species, ForesTycoon.Rules.EcologyResponseProgram rules = null) =>
            (float)(rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default).ResourceLimit(Light, Water, Space, ForestSpeciesProfile.For(species).ShadeTolerance);
    }
    internal readonly record struct ForestCompetitionLoads(double Shade, double CrownLoad);

    // Snapshot all trees before updating any of them. Geometry and ordered reduction are
    // mechanisms; the project graphs own every pair contribution and resource response.
    internal sealed partial class ForestCompetition
    {
        internal readonly record struct Sample(ulong Id, float X, float Y, ForestTreeDimensions Size, float Health, ForestSpecies Species = ForestSpecies.None);
        private sealed class Cell
        {
            internal Sample[] Trees = Array.Empty<Sample>();
            internal int Count;
            internal ulong GeometryVersion;
            internal float MinX, MaxX, MinY, MaxY;
            internal readonly ForesTycoon.Rules.GraphInputRange[] Bounds = new ForesTycoon.Rules.GraphInputRange[5];
            internal void ResetBounds()
            {
                MinX = MinY = float.PositiveInfinity; MaxX = MaxY = float.NegativeInfinity;
                for (int i = 0; i < 4; i++) Bounds[i] = new(double.PositiveInfinity, double.NegativeInfinity);
                // Species profiles can change independently of a geometry snapshot; unknown is always safe.
                Bounds[4] = ForesTycoon.Rules.GraphInputRange.Unknown;
            }
            internal void Include(in Sample tree)
            {
                MinX = Math.Min(MinX, tree.X); MaxX = Math.Max(MaxX, tree.X);
                MinY = Math.Min(MinY, tree.Y); MaxY = Math.Max(MaxY, tree.Y);
                Add(0, tree.Size.Height); Add(1, tree.Size.CrownRadius); Add(2, tree.Size.Diameter); Add(3, tree.Health);
            }
            private void Add(int i, double value) => Bounds[i] = new(Math.Min(Bounds[i].Min, value), Math.Max(Bounds[i].Max, value));
            internal (double Min, double Max) DistanceSquared(float x, float y)
            {
                // Keep the same float subtraction, multiplication and addition as the actual pair query.
                float dx = x < MinX ? MinX - x : x > MaxX ? MaxX - x : 0;
                float dy = y < MinY ? MinY - y : y > MaxY ? MaxY - y : 0;
                float farX = Math.Max(Math.Abs(MinX - x), Math.Abs(MaxX - x));
                float farY = Math.Max(Math.Abs(MinY - y), Math.Abs(MaxY - y));
                return (Math.Max(0, MathF.BitDecrement(dx * dx + dy * dy)), MathF.BitIncrement(farX * farX + farY * farY));
            }
        }
        private struct PreparedCell
        {
            internal int Tile, Count;
            internal Cell Cell;
            internal ulong Version;
            internal double[] Weights;
        }
        private sealed class HealthRow
        {
            internal Sample Geometry;
            internal object SpeciesContext;
            internal string ProgramRevision;
            internal PreparedCell[] Cells = Array.Empty<PreparedCell>();
            internal double Shade, Crowding;
            internal bool Ready, Used = true;
            internal int ActiveCells;
        }
        private readonly Dictionary<ulong, HealthRow> healthRows = new();
        private readonly double[] healthInputs = new double[12];
        private readonly double[] shadeTolerances = new double[256];
        private object toleranceContext;
        private bool tolerancesKnown;
        internal bool CacheHealthQueries { get; set; } = true;
        private readonly Dictionary<int, Cell> cells = new();
        private readonly Dictionary<int, int[]> neighbours = new();
        private readonly HashSet<int> snapshotTiles = new();
        internal void Clear() { cells.Clear(); neighbours.Clear(); snapshotTiles.Clear(); healthRows.Clear(); }
        // Reserve the second snapshot buffer while loading, rather than allocating all pair rows during play.
        internal IEnumerable<int> ReserveLikeWork(ForestCompetition source)
        {
            foreach (var pair in source.healthRows) {
                if (!healthRows.TryGetValue(pair.Key, out var row)) healthRows.Add(pair.Key, row = new());
                if (row.Cells.Length < pair.Value.Cells.Length) row.Cells = new PreparedCell[pair.Value.Cells.Length];
                for (int i = 0; i < pair.Value.Cells.Length; i++) {
                    int capacity = pair.Value.Cells[i].Weights?.Length ?? 0;
                    if ((row.Cells[i].Weights?.Length ?? 0) < capacity) row.Cells[i].Weights = new double[capacity];
                    yield return 1;
                }
            }
        }
        internal void BeginSnapshot() => SimulationWork.Complete(BeginSnapshotWork());
        internal IEnumerable<int> BeginSnapshotWork()
        {
            foreach (var pair in healthRows) {
                if (!pair.Value.Used) healthRows.Remove(pair.Key);
                else { pair.Value.Ready = false; pair.Value.Used = false; }
                yield return 1;
            }
            foreach (var cell in cells.Values) { cell.Count = 0; yield return 1; }
        }
        internal void SnapshotPatch(IForestHabitat habitat, ForestTreeStore store, int id, double year)
            => SimulationWork.Complete(SnapshotPatchWork(habitat, store, id, year));
        internal IEnumerable<int> SnapshotPatchWork(IForestHabitat habitat, ForestTreeStore store, int id, double year)
        {
            foreach (int unit in SnapshotTileWork(habitat, store, id, year)) yield return unit;
            Neighbours(habitat, id);
            yield return 1;
        }
        internal void Snapshot(IForestHabitat habitat, ForestTreeStore store, double year)
            => SimulationWork.Complete(SnapshotWork(habitat, store, year));
        internal IEnumerable<int> SnapshotWork(IForestHabitat habitat, ForestTreeStore store, double year)
        {
            foreach (int unit in BeginSnapshotWork()) yield return unit;
            foreach (var entry in store.Patches)
            {
                foreach (int unit in SnapshotPatchWork(habitat, store, entry.Key, year)) yield return unit;
            }
        }

        // Local edits only need the samples read by the affected trees. Capture all of those
        // dependencies before changing rates, including removed patches and second-ring neighbours.
        internal void SnapshotAffected(IForestHabitat habitat, ForestTreeStore store, double year, HashSet<int> affected)
            => SimulationWork.Complete(SnapshotAffectedWork(habitat, store, year, affected));
        internal IEnumerable<int> SnapshotAffectedWork(IForestHabitat habitat, ForestTreeStore store, double year, HashSet<int> affected)
        {
            snapshotTiles.Clear();
            foreach (int id in affected)
            {
                yield return 1;
                if (!store.TryGet(id, out var patch) || patch.Count == 0) continue;
                foreach (int dependency in Neighbours(habitat, id)) snapshotTiles.Add(dependency);
            }
            foreach (int id in snapshotTiles)
                foreach (int unit in SnapshotTileWork(habitat, store, id, year)) yield return unit;
        }

        private IEnumerable<int> SnapshotTileWork(IForestHabitat habitat, ForestTreeStore store, int id, double year)
        {
            if (!store.TryGet(id, out var patch) || patch.Count == 0)
            {
                yield return 1; // Removed/empty dependencies must also consume a bounded work unit.
                if (cells.TryGetValue(id, out var old)) { if (old.Count != 0) old.GeometryVersion++; old.Count = 0; }
                yield break;
            }
            if (!cells.TryGetValue(id, out var cell)) cells.Add(id, cell = new());
            if (cell.Trees.Length < patch.Trees.Length) cell.Trees = new Sample[patch.Trees.Length];
            var geometry = habitat.GetForestTileGeometry(id);
            bool changedGeometry = cell.Count != patch.Count;
            cell.Count = patch.Count;
            cell.ResetBounds();
            for (int i = 0; i < cell.Count; i++)
            {
                yield return 1;
                var tree = patch.Trees[i];
                var sample = new Sample(tree.Id, geometry.X + tree.U * geometry.Width,
                    geometry.Y + tree.V * geometry.Height, tree.At(year), tree.Health, tree.Species);
                changedGeometry |= (cell.Trees[i] with { Health = 0 }) != (sample with { Health = 0 });
                cell.Trees[i] = sample;
                cell.Include(sample);

            }
            if (changedGeometry) cell.GeometryVersion++;
        }

        private int[] Neighbours(IForestHabitat habitat, int tileId)
        {
            if (neighbours.TryGetValue(tileId, out var cached)) return cached;
            Span<int> adjacent = stackalloc int[16];
            var ids = new List<int> { tileId };
            int firstRing = habitat.GetAdjacentTileIds(tileId, adjacent);
            for (int i = 0; i < firstRing; i++) if (!ids.Contains(adjacent[i])) ids.Add(adjacent[i]);
            int end = ids.Count;
            for (int n = 1; n < end; n++)
            {
                int count = habitat.GetAdjacentTileIds(ids[n], adjacent);
                for (int i = 0; i < count; i++) if (!ids.Contains(adjacent[i])) ids.Add(adjacent[i]);
            }
            var result = ids.ToArray();
            neighbours.Add(tileId, result);
            return result;
        }
        internal sealed record Query(ForestTree Tree, double Year, WorkResult<ForestCompetitionLoads> Result)
        {
            internal string Error;
        }
        private sealed class QueryWorker
        {
            internal ForestCompetition Competition;
            internal ForesTycoon.Rules.EcologyResponseProgram Rules;
        }
        private QueryWorker[] queryWorkers;
        private readonly SlicedQueryBatch queryBatch = new();
        internal bool BackgroundPending => queryBatch.Pending;
        private ForestCompetition(Dictionary<int, Cell> cells, Dictionary<int, int[]> neighbours)
        {
            this.cells = cells; this.neighbours = neighbours;
        }
        internal ForestCompetition() { }

        // Only pure graph queries run in workers. The owner keeps the immutable snapshot and tree store
        // unchanged until all results have joined; dictionary publication and error order stay on the owner.
        internal IEnumerable<int> EvaluateBatchWork(IForestHabitat habitat, IReadOnlyList<Query> queries,
            ForesTycoon.Rules.EcologyResponseProgram rules)
        {
            if (queryBatch.Pending) throw new InvalidOperationException("A competition batch already owns this snapshot.");
            if (queries.Count > 32 * 128) throw new ArgumentOutOfRangeException(nameof(queries), "At most 32 patches of 128 trees belong to one worker batch.");
            int count = SlicedQueryBatch.WorkerCount;
            if (count == 1 || queries.Count < 16) {
                foreach (var query in queries)
                    foreach (int unit in EvaluateLoadsWork(habitat, query.Tree, query.Year, query.Result, rules)) yield return unit;
                yield break;
            }
            foreach (var query in queries) { Neighbours(habitat, query.Tree.TileId); yield return 1; }
            queryWorkers ??= new QueryWorker[count];
            object species = ForestSpeciesBook.CaptureContext();
            for (int i = 0; i < count; i++) queryWorkers[i] ??= new() { Competition = new(cells, neighbours) };
            foreach (int unit in queryBatch.RunWork(count, partition => ForestSpeciesBook.WithContext(species, () => {
                var worker = queryWorkers[partition];
                    if (worker.Rules?.PairRevision != rules.PairRevision) worker.Rules = rules.CreateQueryWorker();
                    worker.Competition.healthRows.Clear();
                    worker.Competition.CacheHealthQueries = CacheHealthQueries;
                    foreach (var query in queries) {
                        // A tile always belongs to one worker, including a repeated local repair.
                        if ((uint)query.Tree.TileId % (uint)count != partition) continue;
                        if (healthRows.TryGetValue(query.Tree.Id, out var row)) worker.Competition.healthRows[query.Tree.Id] = row;
                        worker.Rules.ClearQueryError();
                        using (var calculation = worker.Competition.EvaluateLoadsWork(habitat, query.Tree, query.Year, query.Result, worker.Rules).GetEnumerator())
                            while (SimulationWork.MoveNext(calculation, 32)) { } // At most one bounded query in this worker job.

                        query.Error = worker.Rules.LastError;
                    }
                return 0;
            }))) yield return unit;
            foreach (var query in queries) {
                var worker = queryWorkers[(uint)query.Tree.TileId % (uint)count];
                if (worker.Competition.healthRows.TryGetValue(query.Tree.Id, out var row)) healthRows[query.Tree.Id] = row;
                rules.PublishQueryError(query.Error);
                yield return 1;
            }
        }
        // Retirement must join jobs before the habitat can be disposed, including an interrupted iterator.
        internal IEnumerable<int> FinishQueriesWork() => queryBatch.JoinWork();

        internal ReadOnlySpan<int> NeighbourTiles(IForestHabitat habitat, int tileId) => Neighbours(habitat, tileId);

        // Only snapshot geometry, candidate enumeration and ordered reduction remain in the host.
        // No crown-based pruning is allowed: an authored graph may intentionally influence a distant candidate.
        internal IEnumerable<int> EvaluateLoadsWork(IForestHabitat habitat, ForestTree tree, double year,
            WorkResult<ForestCompetitionLoads> result, ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            rules ??= ForesTycoon.Rules.EcologyResponseProgram.Default;
            var ids = Neighbours(habitat, tree.TileId);
            var geometry = habitat.GetForestTileGeometry(tree.TileId);
            float x = geometry.X + tree.U * geometry.Width, y = geometry.Y + tree.V * geometry.Height;
            var size = tree.At(year);
            double shade = 0, crownLoad = 0;
            object context = ForestSpeciesBook.CaptureContext();
            if (!tolerancesKnown || !ReferenceEquals(toleranceContext, context)) {
                foreach (var species in Enum.GetValues<ForestSpecies>()) shadeTolerances[(int)species] = ForestSpeciesProfile.For(species).ShadeTolerance;
                toleranceContext = context; tolerancesKnown = true;
            }
            double tolerance = shadeTolerances[(int)tree.Species];
            var ownGeometry = new Sample(tree.Id, x, y, size, 0, tree.Species);
            bool Valid(HealthRow candidate) {
                if (!candidate.Ready || candidate.Geometry != ownGeometry || candidate.ProgramRevision != rules.PairRevision || !ReferenceEquals(candidate.SpeciesContext, context)) return false;
                for (int d = 0; d < candidate.ActiveCells; d++) {
                    var dependency = candidate.Cells[d];
                    if (dependency.Cell != null) { if (dependency.Version != dependency.Cell.GeometryVersion) return false; }
                    else if (cells.TryGetValue(dependency.Tile, out var added) && added.Count > 0) return false;
                }
                return true;
            }
            HealthRow row = null;
            if (CacheHealthQueries && rules.CanPrepareHealthPairs && float.IsFinite(tree.Health)) {
                if (healthRows.TryGetValue(tree.Id, out row) && Valid(row)) {
                    row.Used = true;
                    if (!rules.PreparedShadeUsesHealth) { yield return 1; result.Value = new(row.Shade, row.Crowding); yield break; }
                    int work = 0;
                    for (int d = 0; d < row.ActiveCells; d++) {
                        var dependency = row.Cells[d];
                        for (int i = 0; i < dependency.Count; i++) {
                            if (++work == Math.Min(32, SimulationWork.MaximumUnits)) { yield return work; work = 0; }
                            shade += rules.PreparedShade(dependency.Weights[i], tree.Health, dependency.Cell.Trees[i].Health);
                        }
                    }
                    if (work > 0) yield return work;
                    result.Value = new(shade, row.Crowding); yield break;
                }
                if (row == null) healthRows[tree.Id] = row = new();
                row.Geometry = ownGeometry; row.SpeciesContext = context; row.ProgramRevision = rules.PairRevision;
                row.Ready = false; row.Used = true; row.ActiveCells = 0;
                if (row.Cells.Length != ids.Length) row.Cells = new PreparedCell[ids.Length];
            }
            bool cacheable = row != null;
            int pendingWork = 0;
            foreach (int id in ids) {
                if (++pendingWork == Math.Min(32, SimulationWork.MaximumUnits)) { yield return pendingWork; pendingWork = 0; }
                cells.TryGetValue(id, out var cell);
                PreparedCell dependency = default;
                int dependencyIndex = -1;
                if (row != null) {
                    dependencyIndex = row.ActiveCells++;
                    dependency = row.Cells[dependencyIndex];
                    dependency.Tile = id; dependency.Cell = cell; dependency.Version = cell?.GeometryVersion ?? 0; dependency.Count = 0;
                    row.Cells[dependencyIndex] = dependency;
                }
                if (cell == null) continue;
                double[] weights = null;
                if (cell.Count > 0 && rules.CanBoundPairs) {
                    var distance = cell.DistanceSquared(x, y);
                    if (!rules.PairMayContribute(size.Height, size.CrownRadius, size.Diameter, tree.Health, tolerance,
                        cell.Bounds, distance.Min, distance.Max, ignoreHealth: row != null)) continue;
                }
                if (row != null && rules.PreparedShadeUsesHealth) {
                    if ((dependency.Weights?.Length ?? 0) < cell.Count) dependency.Weights = new double[cell.Trees.Length];
                    weights = dependency.Weights ?? Array.Empty<double>(); dependency.Count = cell.Count;
                    row.Cells[dependencyIndex] = dependency;
                    Array.Fill(weights, double.NaN, 0, cell.Count);
                }
                for (int i = 0; i < cell.Count; i++) {
                    if (++pendingWork == Math.Min(32, SimulationWork.MaximumUnits)) { yield return pendingWork; pendingWork = 0; }
                    var other = cell.Trees[i];
                    if (other.Id == tree.Id) continue;
                    float dx = other.X - x, dy = other.Y - y;
                    double otherTolerance = shadeTolerances[(int)other.Species];
                    (double Shade, double CrownLoad) effects;
                    bool prepared = false;
                    if (row != null) {
                        var v = healthInputs;
                        v[0] = size.Height; v[1] = size.CrownRadius; v[2] = size.Diameter; v[3] = tree.Health; v[4] = tolerance;
                        v[5] = other.Size.Height; v[6] = other.Size.CrownRadius; v[7] = other.Size.Diameter; v[8] = other.Health; v[9] = otherTolerance;
                        v[11] = dx * dx + dy * dy; v[10] = Math.Sqrt(v[11]);
                        prepared = rules.TryPrepareHealthPair(v, out double coefficient, out double preparedShade, out double preparedCrowd);
                        effects = (preparedShade, preparedCrowd);
                        if (weights != null) weights[i] = coefficient;
                    } else effects = default;
                    if (!prepared) {
                        cacheable = false;
                        effects = rules.Pair(size.Height, size.CrownRadius, size.Diameter, tree.Health, tolerance,
                            other.Size.Height, other.Size.CrownRadius, other.Size.Diameter, other.Health, otherTolerance, dx * dx + dy * dy);
                    }
                    shade += effects.Shade; crownLoad += effects.CrownLoad;
                }
            }
            if (pendingWork > 0) yield return pendingWork;
            result.Value = new(shade, crownLoad);
            if (row != null) { row.Shade = shade; row.Crowding = crownLoad; row.Ready = cacheable; }
        }
        internal static ForestResources Resolve(ForestCompetitionLoads loads, float water, float radiation,
            ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            var response = (rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default).Competition(loads.Shade, loads.CrownLoad, radiation, water);
            return new((float)response.Light, Math.Clamp(water, 0, 1), (float)response.Space);
        }
        // Synchronous diagnostics use exactly the same sliced implementation.
        internal ForestResources Evaluate(IForestHabitat habitat, in ForestTree tree, double year, float siteWater, float radiation = 1,
            ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            var result = new WorkResult<ForestCompetitionLoads>();
            SimulationWork.Complete(EvaluateLoadsWork(habitat, tree, year, result, rules));
            return Resolve(result.Value, siteWater, radiation, rules);
        }
    }
}
