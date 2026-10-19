using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Fresh monthly lifecycle templates from the authored Editor project.</summary>
public static class TreeVitalityRules
{
    public static IEnumerable<WorldRule> Defaults() => RuleProject.CreateDefault().Rules
        .Where(r => r.Id is "forest.health" or "forest.stress" or "forest.mortality" or "forest.deadwood");
}
