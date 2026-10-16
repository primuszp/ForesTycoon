namespace ForesTycoon.Tests;

public class ForestCompetitionTraversalTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 25;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .65f;
        public float GetNormalizedElevation(int id) => .45f;
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id % 5 * 16, id / 5 * 16, 16, 16);
        public int GetAdjacentTileIds(int id, Span<int> target)
        {
            int n = 0, x = id % 5, y = id / 5;
            if (x > 0) target[n++] = id - 1;
            if (x < 4) target[n++] = id + 1;
            if (y > 0) target[n++] = id - 5;
            if (y < 4) target[n++] = id + 5;
            return n;
        }
    }

    private static ForestTreeStore Create(Habitat habitat)
    {
        var store = new ForestTreeStore();
        var random = new Random(762);
        for (int id = 0; id < habitat.TileCount; id++)
        {
            store.Create(id, new ForestStand(ForestSpecies.Oak, 80, 1, 1), 0, habitat.Seed);
            store.TryGet(id, out var patch);
            for (int i = 0; i < patch.Count; i++)
                patch.Trees[i] = patch.Trees[i] with {
                    U = (float)random.NextDouble(), V = (float)random.NextDouble(),
                    Dimensions = new(.3f, 1 + (float)random.NextDouble() * 20,
                        id % 3 == 0 ? .001f : (float)random.NextDouble() * 20),
                    AnnualGrowth = new(.01f, .1f, .1f), Health = (float)random.NextDouble()
                };
        }
        return store;
    }

    [Fact]
    public void PairTraversalCanSuspendAndMatchesTheSynchronousGraphPath()
    {
        var habitat = new Habitat(); var store = Create(habitat);
        var competition = new ForestCompetition(); competition.Snapshot(habitat, store, .8);
        foreach (var entry in store.Patches)
        foreach (var tree in entry.Value.Trees.Take(entry.Value.Count)) {
            var result = new WorkResult<ForestCompetitionLoads>(); int slices = 0;
            foreach (int unit in competition.EvaluateLoadsWork(habitat, tree, .8, result)) { Assert.Equal(1, unit); slices++; }
            Assert.True(slices >= Neighbours(habitat, tree.TileId).Count); // Even proven-zero cells yield a bounded work unit.
            Assert.Equal(competition.Evaluate(habitat, tree, .8, .7f, .6f), ForestCompetition.Resolve(result.Value, .7f, .6f));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void FloatDistancesAtAndBesideTheAuthoredBoundaryKeepTheExactContribution(int side)
    {
        float distance = side < 0 ? MathF.BitDecrement(2) : side > 0 ? MathF.BitIncrement(2) : 2;
        var dimensions = new ForestTreeDimensions(.2f, 10, 1);
        var tree = new ForestTree(1, 0, ForestSpecies.Oak, 0, 0, 42, -20, 0, dimensions, default, 1);
        var sample = new ForestCompetition.Sample(2, distance, 0, dimensions, 1, ForestSpecies.Oak);
        var snapshot = new CompetitionCheckpoint(new[] { new CompetitionCellCheckpoint(1, new[] { sample }) },
            new[] { new NeighboursCheckpoint(0, new[] { 0, 1 }) });
        var competition = new ForestCompetition(); competition.Restore(snapshot, 25);
        var rules = EcologyResponseProgram.Default;
        double tolerance = ForestSpeciesProfile.For(ForestSpecies.Oak).ShadeTolerance;
        var expected = rules.Pair(10, 1, .2f, 1, tolerance, 10, 1, .2f, 1, tolerance, distance * distance);
        var actual = new WorkResult<ForestCompetitionLoads>();
        SimulationWork.Complete(competition.EvaluateLoadsWork(new Habitat(), tree, 0, actual, rules));
        Assert.Equal(new ForestCompetitionLoads(expected.Shade, expected.CrownLoad), actual.Value);
        if (side < 0) Assert.True(actual.Value.Shade > 0);
    }

    [Fact]
    public void GraphBoundsAndRestoredBoundsMatchAnUnprunedPairReductionExactly()
    {
        var habitat = new Habitat(); var store = Create(habitat);
        var competition = new ForestCompetition(); competition.Snapshot(habitat, store, .8);
        Check(competition);
        var restored = new ForestCompetition(); restored.Restore(competition.Capture(), habitat.TileCount); Check(restored);
        store.RemoveTile(12); store.TryGet(11, out var cut); store.RemoveLiving(cut, 0);
        competition.SnapshotAffected(habitat, store, .8, Neighbours(habitat, 12).ToHashSet()); Check(competition);

        void Check(ForestCompetition actual)
        {
            var captured = actual.Capture();
            var cells = captured.Cells.ToDictionary(c => c.Tile, c => c.Trees);
            var neighbors = captured.Neighbours.ToDictionary(n => n.Tile, n => n.Neighbours);
            foreach (var entry in store.Patches)
            foreach (var tree in entry.Value.Trees.Take(entry.Value.Count)) {
                var geometry = habitat.GetForestTileGeometry(tree.TileId);
                float x = geometry.X + tree.U * geometry.Width, y = geometry.Y + tree.V * geometry.Height;
                var size = tree.At(.8); double shade = 0, load = 0;
                var rules = EcologyResponseProgram.Default;
                foreach (int tile in neighbors[tree.TileId]) {
                    if (!cells.TryGetValue(tile, out var samples)) continue;
                    foreach (var other in samples) {
                        if (other.Id == tree.Id) continue;
                        float dx = other.X - x, dy = other.Y - y;
                        var effect = rules.Pair(size.Height, size.CrownRadius, size.Diameter, tree.Health,
                            ForestSpeciesProfile.For(tree.Species).ShadeTolerance, other.Size.Height, other.Size.CrownRadius,
                            other.Size.Diameter, other.Health, ForestSpeciesProfile.For(other.Species).ShadeTolerance, dx * dx + dy * dy);
                        shade += effect.Shade; load += effect.CrownLoad;
                    }
                }
                var result = new WorkResult<ForestCompetitionLoads>();
                SimulationWork.Complete(actual.EvaluateLoadsWork(habitat, tree, .8, result, rules));
                Assert.Equal(new ForestCompetitionLoads(shade, load), result.Value);
            }
        }
    }

    [Fact]
    public void LocalSnapshotMatchesFullSnapshotAfterRemovalAndGrowth()
    {
        var habitat = new Habitat();
        var store = Create(habitat);
        var local = new ForestCompetition();
        local.Snapshot(habitat, store, 0);
        store.RemoveTile(12);
        store.TryGet(11, out var cut);
        store.RemoveLiving(cut, 0);
        var affected = Neighbours(habitat, 12).ToHashSet();
        local.SnapshotAffected(habitat, store, .8, affected);
        var full = new ForestCompetition();
        full.Snapshot(habitat, store, .8);
        foreach (int id in affected)
            if (store.TryGet(id, out var patch))
                foreach (var tree in patch.Trees.Take(patch.Count))
                    Assert.Equal(full.Evaluate(habitat, tree, .8, .7f), local.Evaluate(habitat, tree, .8, .7f));
    }

    private static List<int> Neighbours(Habitat habitat, int id)
    {
        var ids = new List<int> { id };
        Span<int> target = stackalloc int[4];
        int n = habitat.GetAdjacentTileIds(id, target);
        for (int i = 0; i < n; i++) if (!ids.Contains(target[i])) ids.Add(target[i]);
        int end = ids.Count;
        for (int j = 1; j < end; j++)
        {
            n = habitat.GetAdjacentTileIds(ids[j], target);
            for (int i = 0; i < n; i++) if (!ids.Contains(target[i])) ids.Add(target[i]);
        }
        return ids;
    }
}
