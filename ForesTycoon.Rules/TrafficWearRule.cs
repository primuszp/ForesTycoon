using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>A fresh passage template from the authored Editor project.</summary>
public static class TrafficWearRule
{
    public static WorldRule Default() => RuleProject.CreateDefault().Rules.Single(r => r.Id == "traffic.wear");
}
