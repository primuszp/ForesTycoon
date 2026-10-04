using System;
using System.Diagnostics;

namespace ForesTycoon
{
    // No window, GPU, rendering or water ticks: isolates monthly competition and local extraction.
    internal static class ForestSimulationBenchmark
    {
        internal static void Run()
        {
            foreach (int side in new[] { 32, 64, 128 })
                RunCase(side, false);
            foreach (int side in new[] { 32, 64 })
                RunCase(side, true);
            RunPreparedCase(64, false);
            RunPreparedCase(128, false);
            RunPreparedCase(64, true);
            RunLateEditCase();
        }

        private static void RunLateEditCase()
        {
            var habitat = new GridHabitat(128);
            var forest = new ForestSystem(habitat, secondsPerYear: 1200);
            _ = new ForestEnvironmentCoordinator(forest, new EnvironmentSystem(habitat, forest));
            for (int step = 0; step < 199; step++)
            {
                forest.PrepareNextMonthStep();
                forest.Update(.5);
            }
            int id = FindForest(forest, habitat.TileCount, 0);
            forest.Harvest(id, out _);
            forest.Plant(id, ForestSpecies.Spruce);
            long start = Stopwatch.GetTimestamp();
            forest.PrepareNextMonthStep();
            forest.Update(.5);
            Console.WriteLine($"Prepared natural 128x128, edit in final half-second: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.00} ms boundary including restart (single sample, not steady-state).");
        }

        private static void RunPreparedCase(int side, bool dense)
        {
            var habitat = new GridHabitat(side);
            ForestSystem Create()
            {
                var forest = new ForestSystem(habitat, secondsPerYear: 1200);
                if (dense)
                {
                    var stands = new ForestStand[habitat.TileCount];
                    Array.Fill(stands, new ForestStand(ForestSpecies.Oak, 80, ForestSpeciesProfile.For(ForestSpecies.Oak).MaximumBiomass, 1));
                    forest = new ForestSystem(habitat, stands);
                    forest.UseEnvironmentTempo();
                }
                _ = new ForestEnvironmentCoordinator(forest, new EnvironmentSystem(habitat, forest));
                return forest;
            }
            var prepared = Create();
            var reference = Create();
            var boundaries = new double[7];
            var slices = new double[7 * 200];
            for (int month = 0; month < 9; month++)
            {
                reference.Update(100);
                for (int step = 0; step < 200; step++)
                {
                    long start = Stopwatch.GetTimestamp();
                    prepared.PrepareNextMonthStep();
                    prepared.Update(.5);
                    double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (month >= 2)
                    {
                        slices[(month - 2) * 200 + step] = elapsed;
                        if (step == 199) boundaries[month - 2] = elapsed;
                    }
                }
            }
            ulong fingerprint = Fingerprint(prepared);
            if (fingerprint != Fingerprint(reference)) throw new InvalidOperationException("Prepared monthly growth differs from reference.");
            Array.Sort(boundaries); Array.Sort(slices);
            Console.WriteLine($"Prepared {(dense ? "dense" : "natural")} {side}x{side}: boundary median {boundaries[3]:0.00} ms, max {boundaries[^1]:0.00} ms; all slices p95 {slices[(int)(slices.Length * .95)]:0.00} ms, max {slices[^1]:0.00} ms; {prepared.LastMonthlyPreparedTrees} prepared trees; exact fingerprint {fingerprint:X16}.");
        }

        private static void RunCase(int side, bool dense)
        {
            var habitat = new GridHabitat(side);
            ForestSystem forest;
            if (dense)
            {
                var stands = new ForestStand[habitat.TileCount];
                Array.Fill(stands, new ForestStand(ForestSpecies.Oak, 80, ForestSpeciesProfile.For(ForestSpecies.Oak).MaximumBiomass, 1));
                forest = new ForestSystem(habitat, stands);
                forest.UseEnvironmentTempo();
            }
            else forest = new ForestSystem(habitat, secondsPerYear: 1200);
            _ = new ForestEnvironmentCoordinator(forest, new EnvironmentSystem(habitat, forest));
            forest.Update(200); // JIT, caches and two complete months.
            var months = new double[7];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < months.Length; i++)
            {
                long start = Stopwatch.GetTimestamp();
                forest.Update(100);
                months[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            ulong fingerprint = Fingerprint(forest);
            var edits = new double[7];
            for (int i = 0; i < edits.Length; i++)
            {
                int tile = FindForest(forest, side * side, i * side * side / edits.Length);
                long start = Stopwatch.GetTimestamp();
                forest.ExtractTimber(tile, .01f);
                edits[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            Array.Sort(months); Array.Sort(edits);
            Console.WriteLine($"Forest {(dense ? "dense" : "natural")} {side}x{side}: {forest.IndividualTreeCount} trees; monthly median {months[3]:0.00} ms, max {months[^1]:0.00} ms, {allocated / months.Length} B/month; extraction median {edits[3]:0.00} ms; fingerprint {fingerprint:X16}.");
        }

        private static int FindForest(ForestSystem forest, int count, int start)
        {
            for (int i = 0; i < count; i++)
            {
                int id = (start + i) % count;
                if (forest.IndividualTrees.TryGet(id, out var patch) && patch.Count > 0) return id;
            }
            throw new InvalidOperationException("Benchmark has no living trees.");
        }

        private static ulong Fingerprint(ForestSystem forest)
        {
            ulong hash = 14695981039346656037;
            foreach (var entry in forest.IndividualTrees.Patches)
                for (int i = 0; i < entry.Value.Count; i++)
                {
                    var tree = entry.Value.Trees[i];
                    Add(tree.Id); Add(BitConverter.SingleToUInt32Bits(tree.Health));
                    Add(BitConverter.SingleToUInt32Bits(tree.Dimensions.Diameter));
                    Add(BitConverter.SingleToUInt32Bits(tree.Dimensions.Height));
                    Add(BitConverter.SingleToUInt32Bits(tree.Dimensions.CrownRadius));
                    Add(BitConverter.SingleToUInt32Bits(tree.AnnualGrowth.Diameter));
                    Add(BitConverter.SingleToUInt32Bits(tree.AnnualGrowth.Height));
                    Add(BitConverter.SingleToUInt32Bits(tree.AnnualGrowth.CrownRadius));
                    Add(BitConverter.SingleToUInt32Bits(tree.Resources.Light));
                    Add(BitConverter.SingleToUInt32Bits(tree.Resources.Water));
                    Add(BitConverter.SingleToUInt32Bits(tree.Resources.Space));
                }
            return hash;
            void Add(ulong value) { hash = unchecked((hash ^ value) * 1099511628211); }
        }

        private sealed class GridHabitat(int side) : IForestHabitat
        {
            public int TileCount => side * side;
            public int Seed => 42;
            public bool CanSupportForest(int id) => true;
            public float GetMoisture(int id) => .65f;
            public float GetNormalizedElevation(int id) => .45f;
            public ForestTileGeometry GetForestTileGeometry(int id) => new(id % side * 16, id / side * 16, 16, 16);
            public int GetAdjacentTileIds(int id, Span<int> target)
            {
                int n = 0, x = id % side, y = id / side;
                if (x > 0) target[n++] = id - 1;
                if (x < side - 1) target[n++] = id + 1;
                if (y > 0) target[n++] = id - side;
                if (y < side - 1) target[n++] = id + side;
                return n;
            }
        }
    }
}
