using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon;

internal enum MissionState { Active, Reached, Failed }

internal readonly record struct MissionProgress(MissionGoal Goal, double Value, MissionState State, string Error = null);

/// <summary>The active scene's missions, evaluated from the current world: the game runs what the editor authored.</summary>
partial class SimulationWorld
{
    internal IReadOnlyList<MissionGoal> Missions => (IReadOnlyList<MissionGoal>)ruleProjectWorld?.Missions ?? Array.Empty<MissionGoal>();

    /// <summary>A mission's value: the rule world fields and dynamics stocks, plus delivered timber.</summary>
    internal double MissionValue(string metric) => ReadRuleValue(metric);

    private static readonly Lazy<(string Key, string Name)[]> missionMetrics = new(() => {
        var world = RuleObjects.Find("world");
        return world.Fields.Where(f => f.DeclaredBy == "world").Concat(world.Summaries)
            .Select(f => ("world." + f.Name, f.Display + (f.Unit.Length == 0 ? "" : " (" + f.Unit + ")"))).ToArray();
    });
    internal static (string Key, string Name)[] MissionMetrics => missionMetrics.Value;

    internal IEnumerable<MissionProgress> MissionStatus(IReadOnlyList<MissionGoal> goals = null)
    {
        foreach (var goal in goals ?? Missions) {
            double value; string error = null;
            try { value = MissionValue(goal.Metric); }
            catch (ArithmeticException e) { value = double.NaN; error = e.Message; }
            if (error != null) { yield return new(goal, value, MissionState.Active, error); continue; }
            double[] inputs = { value, goal.Target, goal.Comparison == "<=" ? 1 : 0, forest.ForestYear, goal.DeadlineYear };
            double reached = behaviorPolicy.Gameplay.Evaluate("mission.reached", inputs);
            double failed = behaviorPolicy.Gameplay.Evaluate("mission.failed", inputs);
            var state = (MissionState)(int)behaviorPolicy.Gameplay.Evaluate("mission.state",
                reached, failed, value, goal.Target, goal.Comparison == "<=" ? 1 : 0, forest.ForestYear, goal.DeadlineYear);
            yield return new(goal, value, state);
        }
    }
}
