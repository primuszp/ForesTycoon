using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon;

sealed partial class GameWorld
{
    private DynamicsRuntime dynamics;
    private string ruleProjectName = "ForesTycoon szabályrendszer";
    private AlgorithmGraphReference algorithmGraphs;
    internal static readonly string[] RuleWorldFields = {
        "world.time", "world.treeCount", "world.roadCount", "world.rain", "world.temperature",
        "world.snow", "world.water", "world.income", "world.expenses", "world.runningCosts", "world.balance"
    };
    internal RuleProject RuleProjectDocument => new() { Name = ruleProjectName, Road = RuleDocument,
        Tuning = Tuning.ToOverrides(), Behaviors = BehaviorDocument, Dynamics = dynamics.Document, Algorithms = AlgorithmSnapshot(),
        World = ruleProjectWorld == null ? null : RuleProject.FromJson(new RuleProject { World = ruleProjectWorld }.ToJson()).World };
    /// <summary>The world map of the active project: used by the next new world, never applied to the running one.</summary>
    private WorldScenario ruleProjectWorld;
    private AlgorithmGraphReference AlgorithmSnapshot() => algorithmGraphs == null ? null : new() {
        Revision = algorithmGraphs.Revision, BundlePath = algorithmGraphs.BundlePath,
        WorkspaceRoot = algorithmGraphs.WorkspaceRoot, HostVersion = algorithmGraphs.HostVersion
    };
    internal IReadOnlyDictionary<string, double> DynamicsValues => dynamics.Values;
    internal string DynamicsError => dynamics.LastError;
    internal double ReadRuleValue(string key) => key.StartsWith("stock.", StringComparison.Ordinal) ? dynamics.Read(key) : ReadWorldValue(key);
    private double ReadWorldValue(string key) => key switch {
        "world.time" => forest.ForestYear, "world.treeCount" => ForestTreeCount, "world.roadCount" => RoadCount,
        "world.rain" => Environment.LiquidRainRate, "world.temperature" => Environment.Temperature,
        "world.snow" => Environment.SnowWater, "world.water" => Environment.StoredWater,
        "world.income" => Income, "world.expenses" => Expenses, "world.runningCosts" => Logistics.RunningCosts,
        "world.balance" => Balance, _ => throw new ArithmeticException("Ismeretlen világmező: " + key)
    };
    internal static RuleProject DefaultRuleProject() => new() { Behaviors = WorldBehaviorPolicy.DefaultDocument(), Algorithms = AlgorithmBuild.CurrentReference };
    internal static void ValidateRuleProject(RuleProject project)
    {
        _ = PrepareRuleProject(project, _ => 0);
    }
    internal static RuleProject NormalizeRuleProject(RuleProject project)
    {
        var prepared = PrepareRuleProject(project, _ => 0);
        prepared.Document.Tuning = prepared.Tuning.ToOverrides();
        return prepared.Document;
    }
    private static (RuleProject Document, CompiledRoadRule Road, GameTuning Tuning, DynamicsRuntime Dynamics, WorldBehaviorPolicy Policy)
        PrepareRuleProject(RuleProject project, Func<string, double> readWorld)
    {
        ArgumentNullException.ThrowIfNull(project);
        var copy = project.Clone();
        if (copy.Schema != "forest-rule-project" || copy.Version != 1 || string.IsNullOrWhiteSpace(copy.Name)
            || copy.Tuning == null || copy.Behaviors?.Graphs == null || copy.Behaviors.Graphs.Count > 512) throw new InvalidDataException("Ismeretlen vagy hiányos szabályprojekt.");
        if (copy.Algorithms is AlgorithmGraphReference algorithms && (algorithms.Revision?.Length != 64
            || !algorithms.Revision.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(algorithms.BundlePath)
            || !Path.IsPathFullyQualified(algorithms.BundlePath) || string.IsNullOrWhiteSpace(algorithms.WorkspaceRoot)
            || !Path.IsPathFullyQualified(algorithms.WorkspaceRoot))) throw new InvalidDataException("Hibás algoritmusgráf-hivatkozás.");
        copy.World?.Validate();
        var road = new CompiledRoadRule(copy.Road); road.ValidateRange();
        var tuning = GameTuning.FromOverrides(copy.Tuning);
        var runtime = new DynamicsRuntime(copy.Dynamics, RuleWorldFields, readWorld);
        // Every supported process has an explicit editable type graph, including unchanged native adapters.
        foreach (var hook in WorldBehaviorPolicy.Hooks)
            if (!copy.Behaviors.Graphs.Any(g => g != null && g.Hook == hook.Id && g.Kind == hook.Kind && !g.ObjectId.HasValue))
                copy.Behaviors.Graphs.Add(WorldBehaviorPolicy.DefaultGraph(hook));
        var policy = new WorldBehaviorPolicy(copy.Behaviors, DynamicsRuntime.Keys(copy.Dynamics, RuleWorldFields), runtime.Read);
        return (copy, road, tuning, runtime, policy);
    }
    internal void QueueRuleProject(RuleProject project)
    {
        RequireAlgorithmRuntime(project.Algorithms);
        ValidateRuleProject(project);
        Enqueue(new SetRuleProjectCommand(project.ToJson()));
    }
    void IWorldCommandTarget.ExecuteRuleProject(string json) => ApplyRuleProject(RuleProject.FromJson(json));
    private void ApplyRuleProject(RuleProject project)
    {
        RequireAlgorithmRuntime(project.Algorithms);
        var next = PrepareRuleProject(project, ReadWorldValue);
        next.Dynamics.Preserve(dynamics);
        next.Policy.Controllers.PreserveUnchanged(behaviorPolicy.Controllers);
        bool refresh = ForestBehaviorSignature(BehaviorDocument) != ForestBehaviorSignature(next.Document.Behaviors);
        roadRule = next.Road; dynamics = next.Dynamics; behaviorPolicy = next.Policy; ruleProjectName = next.Document.Name; ruleProjectWorld = next.Document.World;
        // Species numbers are world content the rules read; a project change installs them for the whole process.
        ForestSpeciesBook.Apply(SpeciesEdits(next.Document.World));
        // Springs are part of the hydrology the rules run: a project change moves, adds or stops them at once.
        Environment.Springs = next.Document.World?.Springs;
        algorithmGraphs = next.Document.Algorithms ?? AlgorithmBuild.CurrentReference;
        BindBehaviors(); ApplyTuning(next.Tuning);
        if (refresh) forest.RefreshEnvironmentRates();
        LastRuleSample = "Szabályprojekt alkalmazva; a következő események ezt futtatják.";
    }
    private void InitializeRuleProject()
    {
        dynamics = new(new DynamicsModel(), RuleWorldFields, ReadWorldValue);
        ApplyRuleProject(DefaultRuleProject());
    }
    private static void RequireAlgorithmRuntime(AlgorithmGraphReference reference)
    {
        if (reference != null && reference.Revision != AlgorithmBuild.CurrentRevision)
            throw new InvalidDataException("Az algoritmusgráf revíziója másik futtatót igényel; a projektkapcsolat előbb lefordítja és elindítja azt.");
    }
}
