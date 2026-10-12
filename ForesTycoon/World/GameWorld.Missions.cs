using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon;

internal enum MissionState { Active, Reached, Failed }

internal readonly record struct MissionProgress(MissionGoal Goal, double Value, MissionState State, string Error = null);

/// <summary>The active scene's missions, evaluated from the current world: the game runs what the editor authored.</summary>
sealed partial class GameWorld
{
    internal IReadOnlyList<MissionGoal> Missions => (IReadOnlyList<MissionGoal>)ruleProjectWorld?.Missions ?? Array.Empty<MissionGoal>();

    /// <summary>A mission's value: the rule world fields and dynamics stocks, plus delivered timber.</summary>
    internal double MissionValue(string metric) => metric == "world.deliveredTimber" ? DeliveredTimber : ReadRuleValue(metric);

    internal static readonly (string Key, string Name)[] MissionMetrics = {
        ("world.balance", "Egyenleg (ezer Ft)"), ("world.income", "Bevétel (ezer Ft)"), ("world.deliveredTimber", "Leszállított faanyag (m³)"),
        ("world.treeCount", "Fák száma"), ("world.roadCount", "Utak száma"), ("world.expenses", "Kiadás (ezer Ft)"), ("world.water", "Tárolt víz") };

    internal IEnumerable<MissionProgress> MissionStatus(IReadOnlyList<MissionGoal> goals = null)
    {
        foreach (var goal in goals ?? Missions) {
            double value; string error = null;
            try { value = MissionValue(goal.Metric); }
            catch (ArithmeticException e) { value = double.NaN; error = e.Message; }
            if (error != null) { yield return new(goal, value, MissionState.Active, error); continue; }
            var state = goal.Reached(value) ? MissionState.Reached
                : goal.DeadlineYear > 0 && forest.ForestYear > goal.DeadlineYear ? MissionState.Failed : MissionState.Active;
            yield return new(goal, value, state);
        }
    }
}
