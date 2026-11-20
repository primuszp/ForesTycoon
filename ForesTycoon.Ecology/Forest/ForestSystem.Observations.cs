using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology;

internal readonly record struct TreeMonthSample(ulong Month, ulong Id, int Tile, ForestSpecies Species, bool Dead,
    float RootWaterMm, float Supply, ForestResources Resources,
    float DiameterMetres, float Health, float StressYears);

sealed partial class ForestSystem
{
    internal event Action<ulong> ObservationMonthClosed;
    internal event Action<TreeMonthSample> MonthlyTreeObserved;
    internal event Action<ulong, ForestTree, bool> SeedlingEstablished;
    internal event Action ObservationReset;
    private bool observingMonthlyRules;

    // Sample the resources used by the monthly rules before a death or a new seedling
    // triggers the next competition snapshot. Observers add no work to ordinary games.
    private IEnumerable<int> ObserveTreesWork(double year)
    {
        var water = Environment as EnvironmentSystem;
        foreach (var (tile, patch) in IndividualTrees.Patches) {
            float root = (float)(water?.Water(tile, EnvironmentSystem.WaterStore.Soil) ?? double.NaN);
            float supply = (float)(water?.PeriodSupply(tile) ?? double.NaN);
            for (int i = 0; i < patch.Count; i++) {
                PublishObservation(patch.Trees[i], false, year, root, supply);
                yield return 1;
            }
        }
    }

    private void ObserveDeath(ForestTree tree, double year)
    {
        var water = Environment as EnvironmentSystem;
        PublishObservation(tree, true, year, (float)(water?.Water(tree.TileId, EnvironmentSystem.WaterStore.Soil) ?? double.NaN),
            (float)(water?.PeriodSupply(tree.TileId) ?? double.NaN));
    }

    private void PublishObservation(ForestTree tree, bool dead, double year, float root, float supply) =>
        MonthlyTreeObserved?.Invoke(new(month, tree.Id, tree.TileId, tree.Species, dead, root, supply, tree.Resources,
            tree.At(year).Diameter, tree.Health, tree.StressYears));
}
