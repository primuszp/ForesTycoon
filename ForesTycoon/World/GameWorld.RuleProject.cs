using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon;

sealed partial class GameWorld
{
    private DynamicsRuntime dynamics;
    private RuleEngine ruleEngine;
    internal RuleEngine RuleBook => ruleEngine;
    private string ruleProjectName = "ForesTycoon szabályrendszer";
    private AlgorithmGraphReference algorithmGraphs;
    internal RuleProject RuleProjectDocument => new() { Name = ruleProjectName, Road = RuleDocument,
        Tuning = Tuning.ToOverrides(), Behaviors = BehaviorDocument, Dynamics = dynamics.Document, Algorithms = AlgorithmSnapshot(),
        Rules = RuleEngine.Clone(ruleEngine.Rules), Fields = RuleEngine.CloneFields(ruleEngine.Fields),
        World = ruleProjectWorld == null ? null : RuleProject.FromJson(new RuleProject { World = ruleProjectWorld }.ToJson()).World };
    /// <summary>The world map of the active project: used by the next new world, never applied to the running one.</summary>
    private WorldScenario ruleProjectWorld;
    private AlgorithmGraphReference AlgorithmSnapshot() => algorithmGraphs == null ? null : new() {
        Revision = algorithmGraphs.Revision, BundlePath = algorithmGraphs.BundlePath,
        WorkspaceRoot = algorithmGraphs.WorkspaceRoot, HostVersion = algorithmGraphs.HostVersion
    };
    internal IReadOnlyDictionary<string, double> DynamicsValues => dynamics.Values;
    internal string DynamicsError => dynamics.LastError;
    internal double ReadRuleValue(string key) => key.StartsWith("stock.", StringComparison.Ordinal) ? dynamics.Read(key)
        : key.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(key[7..]) is TuningSpec spec ? Tuning[spec.Key] : ReadWorldValue(key);
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
    private static (RuleProject Document, CompiledRoadRule Road, GameTuning Tuning, DynamicsRuntime Dynamics, WorldBehaviorPolicy Policy, RuleEngine Engine)
        PrepareRuleProject(RuleProject project, Func<string, double> readWorld, Func<string, double> readRule = null)
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
        // The rule book always holds the built-in rules (moved out of C#); a project may edit or disable them.
        copy.Rules ??= new(); copy.Fields ??= new();
        RuleEngine.AddMissingBuiltIns(copy.Rules);
        var engine = new RuleEngine(copy.Rules, copy.Fields, copy.Dynamics.Stocks.Select(s => s.Id), readRule ?? (_ => 0));
        return (copy, road, tuning, runtime, policy, engine);
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
        var next = PrepareRuleProject(project, ReadWorldValue, ReadRuleValue);
        next.Engine.Preserve(ruleEngine);
        next.Dynamics.Preserve(dynamics);
        next.Policy.Controllers.PreserveUnchanged(behaviorPolicy.Controllers);
        bool refresh = ForestBehaviorSignature(BehaviorDocument) != ForestBehaviorSignature(next.Document.Behaviors);
        roadRule = next.Road; dynamics = next.Dynamics; behaviorPolicy = next.Policy; ruleEngine = next.Engine; ruleProjectName = next.Document.Name;
        // A rules-only project keeps the running scene (its map and missions); a project with a world replaces it.
        ruleProjectWorld = next.Document.World ?? ruleProjectWorld;
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
