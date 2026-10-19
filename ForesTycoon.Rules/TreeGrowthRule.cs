using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>A fresh growth template from the authored Editor project.</summary>
public static class TreeGrowthRule
{
    public static WorldRule Default() => RuleProject.CreateDefault().Rules.Single(r => r.Id == "forest.growth");
}
