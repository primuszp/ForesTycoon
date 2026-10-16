using System.Collections.Generic;

namespace ForesTycoon;

sealed partial class GameWorld
{
    private double? treeRuleYear;
    internal double RuleEvaluationYear => treeRuleYear ?? forest.ForestYear;

    private bool growthRuleActive;
    internal IEnumerable<int> TreeGrowthWork(int tile, double year) {
        var previous = treeRuleYear; treeRuleYear = year;
        var binding = BindGrowthPatch(tile); bool wasActive = growthRuleActive; growthRuleActive = true;
        try {
            ruleEngine.Begin(this, 0, year, "growth");
            foreach (int unit in ruleEngine.ContinueWork(this)) yield return unit;
        } finally {
            binding.View.Trees = binding.Trees; binding.View.PreparedElements = binding.Elements;
            growthRuleActive = wasActive; treeRuleYear = previous;
        }
    }

    /// <summary>The ecology host suspends its month here until the same shared rule engine has finished.</summary>
    internal IEnumerable<int> TreeMonthWork(double years, double year)
    {
        var previous = treeRuleYear; treeRuleYear = year;
        try {
            ruleEngine.Begin(this, years, year, "month");
            foreach (int unit in ruleEngine.ContinueWork(this)) yield return unit;
            if (ruleEngine.NetworkChanged) {
                Logistics?.TrailsChanged();
                foreach (int unit in vehicles.RemoveInvalidRoutesWork(map.IsNetworkTile, map.AreNetworkNeighbours)) yield return unit;
            }
        }
        finally { treeRuleYear = previous; }
    }
}
