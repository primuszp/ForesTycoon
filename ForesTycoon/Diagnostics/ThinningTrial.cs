using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForesTycoon;

/// <summary>Paired field experiment on a generated world. One intervention at year zero, followed by natural development.</summary>
internal static class ThinningTrial
{
    private sealed class Plot
    {
        internal readonly ForestTree[] Opening;
        internal readonly HashSet<ulong> Retained;
        internal readonly double BasalArea;
        internal double RemovedBasalArea;
        internal int Marked, Cut;
        internal float RemovedVolume;

        internal Plot(ForestTree[] opening)
        {
            Opening = opening;
            Retained = opening.Select(t => t.Id).ToHashSet();
            BasalArea = opening.Sum(t => StemArea(t, 0));
        }
    }

    private static double StemArea(ForestTree tree, double year)
    {
        double d = tree.At(year).Diameter;
        return Math.PI / 4 * d * d;
    }

    internal static void Run(int years, int seed, TextWriter output)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        var opening = new Dictionary<int, ForestTree[]>();
        var controls = new Dictionary<(int Year, int Tile), Dictionary<ulong, ForestTree>>();
        output.WriteLine("seed,target_pct,year,tile,species,opening,standing_m2,marked,cut,cut_m2,cut_m3,retained,retained_alive,control_same_ids_alive,alive_only_treated,alive_only_control,paired_alive,sum_paired_dbh_delta_mm,sum_paired_space_delta,new_living,new_living_same_species,living_basal_m2");
        foreach (int percent in new[] { 0, 10, 30, 50 }) {
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, seed),
                forestYearSeconds: 120, enableRendering: false);
            var plots = new SortedDictionary<int, Plot>();
            foreach (var (tile, patch) in world.Forest.IndividualTrees.Patches) {
                if (patch.Count == 0 || patch.Trees[0].Species is not (ForestSpecies.Birch or ForestSpecies.Pine)) continue;
                var trees = patch.Trees.AsSpan(0, patch.Count).ToArray();
                if (percent == 0) opening.Add(tile, trees);
                else if (!opening.TryGetValue(tile, out var original) || !original.SequenceEqual(trees))
                    throw new InvalidOperationException($"Opening trees differ on tile {tile}.");
                plots.Add(tile, new(trees));
            }
            if (plots.Count != opening.Count) throw new InvalidOperationException("Opening plots differ from the control.");
            if (percent != 0) {
                foreach (int tile in plots.Keys) world.QueueThinForestArea(tile, tile, percent);
                world.ExecutePendingCommands();
                foreach (var (tile, plot) in plots) ApplyMarks(world, tile, plot, percent);
            }
            for (int year = 0; year <= years; year++) {
                if (year != 0)
                    for (int tick = 0; tick < 3600; tick++) world.Update(1d / 30);
                foreach (var (tile, plot) in plots) {
                    world.Forest.IndividualTrees.TryGet(tile, out var patch);
                    var living = patch == null ? Array.Empty<ForestTree>() : patch.Trees.AsSpan(0, patch.Count).ToArray();
                    if (percent == 0) controls.Add((year, tile), living.ToDictionary(t => t.Id));
                    var control = controls[(year, tile)];
                    int comparison = plot.Retained.Count(control.ContainsKey);
                    int survivors = 0, paired = 0, newSameSpecies = 0;
                    double diameterDelta = 0, spaceDelta = 0;
                    foreach (var tree in living) {
                        if (!plot.Retained.Contains(tree.Id)) {
                            if (tree.Species == plot.Opening[0].Species) newSameSpecies++;
                            continue;
                        }
                        survivors++;
                        if (!control.TryGetValue(tree.Id, out var untreated)) continue;
                        paired++;
                        diameterDelta += 1000d * (tree.At(year).Diameter - untreated.At(year).Diameter);
                        spaceDelta += tree.Resources.Space - untreated.Resources.Space;
                    }
                    double area = living.Sum(t => StemArea(t, world.Forest.ForestYear));
                    output.WriteLine(FormattableString.Invariant($"{seed},{percent},{year},{tile},{plot.Opening[0].Species},{plot.Opening.Length},{plot.BasalArea:F8},{plot.Marked},{plot.Cut},{plot.RemovedBasalArea:F8},{plot.RemovedVolume:F5},{plot.Retained.Count},{survivors},{comparison},{survivors-paired},{comparison-paired},{paired},{diameterDelta:F6},{spaceDelta:F6},{living.Length-survivors},{newSameSpecies},{area:F8}"));
                }
            }
        }
    }

    private static void ApplyMarks(GameWorld world, int tile, Plot plot, int percent)
    {
        var site = world.Logistics.SiteAt(tile);
        if (site == null) return;
        var marks = site.MarkedTrees ?? throw new InvalidOperationException("Expected a thinning prescription.");
        plot.Marked = marks.Count;
        // Synchronize felling across treatments. The processor's normal marked-stem extraction runs at year zero.
        var load = new WorkResult<float>();
        SimulationWork.Complete(world.Forest.ExtractTimberWork(tile, float.MaxValue, load, marks));
        plot.RemovedVolume = load.Value;
        world.Forest.IndividualTrees.TryGet(tile, out var patch);
        var remaining = patch.Trees.Take(patch.Count).Select(t => t.Id).ToHashSet();
        var cut = plot.Opening.Where(t => !remaining.Contains(t.Id)).ToArray();
        plot.Cut = cut.Length;
        plot.RemovedBasalArea = cut.Sum(t => StemArea(t, 0));
        if (!marks.SetEquals(cut.Select(t => t.Id)) || plot.RemovedBasalArea > plot.BasalArea * percent / 100 + 1e-10 ||
            Math.Abs(site.InitialVolume - load.Value) > 1e-5 * Math.Max(1, site.InitialVolume))
            throw new InvalidOperationException($"Measured harvest does not match the prescription on tile {tile}.");
        plot.Retained.ExceptWith(marks);
    }
}
