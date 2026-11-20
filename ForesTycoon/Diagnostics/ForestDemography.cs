using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon;

/// <summary>Annual demographic balance by species and soil, sampled after each completed forest month.</summary>
internal static class ForestDemography
{
    private readonly record struct Group(ForestSpecies Species, string Soil);
    private sealed class Balance
    {
        internal int Opening, Recruits, Deaths, Closing;
        internal double DeathAge, PriorWater, PriorLight, PriorSpace, PriorStress, ClosingHealth;
    }

    private static string Mean(double sum, int count) => count == 0 ? "" : FormattableString.Invariant($"{sum / count:F3}");

    internal static void Run(int years, int seed, string condition)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        const int secondsPerYear = 120, ticksPerSecond = 30;
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, seed),
            forestYearSeconds: secondsPerYear, enableRendering: false);
        var project = world.RuleProjectDocument;
        if (condition != "control") {
            if (condition is not ("forest.light" or "forest.space" or "water.stress"))
                throw new ArgumentException("Use control, forest.light, forest.space or water.stress.", nameof(condition));
            var graph = project.Behaviors.Graphs.Single(g => g.Hook == condition);
            graph.Nodes = new() { new() { Id = "value", Name = "Constant", Operation = BehaviorOperation.Constant, Value = 1 } };
            graph.Output = "value";
        }
        world.QueueRuleProject(project);
        world.ExecutePendingCommands();
        var previous = new Dictionary<ulong, ForestTree>();
        var current = new Dictionary<ulong, ForestTree>();
        void Census(Dictionary<ulong, ForestTree> trees)
        {
            trees.Clear();
            foreach (var entry in world.Forest.IndividualTrees.Patches)
                for (int i = 0; i < entry.Value.Count; i++) {
                    var tree = entry.Value.Trees[i];
                    trees.Add(tree.Id, tree);
                }
        }
        Census(previous);
        Console.WriteLine("condition,seed,year,species,soil,opening,recruits,deaths,closing,mean_death_age,mean_prior_water,mean_prior_light,mean_prior_space,mean_prior_stress_years,mean_closing_health");
        for (int year = 1; year <= years; year++)
        {
            var balances = new Dictionary<Group, Balance>();
            Balance For(ForestTree tree)
            {
                var key = new Group(tree.Species, world.Soils.Profile(tree.TileId).Id);
                if (!balances.TryGetValue(key, out var value)) balances.Add(key, value = new());
                return value;
            }
            foreach (var tree in previous.Values) For(tree).Opening++;
            for (int month = 0; month < 12; month++) {
                for (int tick = 0; tick < secondsPerYear * ticksPerSecond / 12; tick++)
                    world.Update(1d / ticksPerSecond);
                Census(current);
                foreach (var tree in current.Values)
                    if (!previous.ContainsKey(tree.Id)) For(tree).Recruits++;
                foreach (var tree in previous.Values)
                    if (!current.ContainsKey(tree.Id)) {
                        var balance = For(tree);
                        balance.Deaths++;
                        balance.DeathAge += tree.Age(world.Forest.ForestYear);
                        balance.PriorWater += tree.Resources.Water;
                        balance.PriorLight += tree.Resources.Light;
                        balance.PriorSpace += tree.Resources.Space;
                        balance.PriorStress += tree.StressYears;
                    }
                (previous, current) = (current, previous);
            }
            foreach (var tree in previous.Values) {
                var balance = For(tree);
                balance.Closing++;
                balance.ClosingHealth += tree.Health;
            }
            foreach (var (group, balance) in balances.OrderBy(p => p.Key.Species).ThenBy(p => p.Key.Soil, StringComparer.Ordinal)) {
                if (balance.Opening + balance.Recruits - balance.Deaths != balance.Closing)
                    throw new InvalidOperationException("Forest census does not balance.");
                Console.WriteLine($"{condition},{seed},{year},{group.Species},{group.Soil},{balance.Opening},{balance.Recruits},{balance.Deaths},{balance.Closing},{Mean(balance.DeathAge, balance.Deaths)}," +
                    $"{Mean(balance.PriorWater, balance.Deaths)},{Mean(balance.PriorLight, balance.Deaths)},{Mean(balance.PriorSpace, balance.Deaths)}," +
                    $"{Mean(balance.PriorStress, balance.Deaths)},{Mean(balance.ClosingHealth, balance.Closing)}");
            }
        }
    }
}
