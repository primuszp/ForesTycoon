namespace ForesTycoon.Tests;

public class ForestCompetitionPruningTests
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PruningMatchesUnculledReferenceForSmallAndOversizedCrowns(bool allocatedWater)
    {
        var habitat = new Habitat();
        var store = Create(habitat);
        var competition = new ForestCompetition();
        competition.Snapshot(habitat, store, .8);
        foreach (var entry in store.Patches)
            foreach (var tree in entry.Value.Trees.Take(entry.Value.Count))
                Assert.Equal(Reference(habitat, store, tree, .8, allocatedWater),
                    competition.Evaluate(habitat, tree, .8, .7f, .6f, allocatedWater));
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

    // Original pair traversal and arithmetic, without pair or cell pruning.
    private static ForestResources Reference(Habitat habitat, ForestTreeStore store, ForestTree tree, double year, bool allocated)
    {
        var geometry = habitat.GetForestTileGeometry(tree.TileId);
        float x = geometry.X + tree.U * geometry.Width, y = geometry.Y + tree.V * geometry.Height;
        var size = tree.At(year);
        float shade = 0, crowns = 0, roots = 0;
        foreach (int id in Neighbours(habitat, tree.TileId))
        {
            if (!store.TryGet(id, out var patch)) continue;
            var cell = habitat.GetForestTileGeometry(id);
            for (int i = 0; i < patch.Count; i++)
            {
                var other = patch.Trees[i];
                if (other.Id == tree.Id) continue;
                var otherSize = other.At(year);
                float dx = cell.X + other.U * cell.Width - x, dy = cell.Y + other.V * cell.Height - y;
                float distance = dx * dx + dy * dy, reach = size.CrownRadius + otherSize.CrownRadius;
                float overlap = Overlap(distance, reach);
                float ratio = Math.Clamp(otherSize.CrownRadius * otherSize.CrownRadius
                    / Math.Max(.0225f, size.CrownRadius * size.CrownRadius), .001f, 4);
                crowns += overlap * ratio;
                float dominance = Math.Clamp(.5f + (otherSize.Height - size.Height) / Math.Max(.8f, size.Height) * 2, 0, 1);
                shade += overlap * 2 * dominance * other.Health;
                if (!allocated) roots += Overlap(distance, Math.Max(1, reach * 1.25f)) * MathF.Sqrt(ratio);
            }
        }
        return new(.6f * MathF.Exp(-.65f * shade), .7f / (1 + roots * .22f), 1 / (1 + crowns * .55f));
    }

    private static float Overlap(float distance, float reach)
    {
        float t = Math.Max(0, 1 - distance / Math.Max(.01f, reach * reach));
        return t * t;
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
