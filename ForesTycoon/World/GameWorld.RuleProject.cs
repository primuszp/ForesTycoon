using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ForesTycoon.RuleCompiler;
using ForesTycoon.Rules;

namespace ForesTycoon;

sealed partial class GameWorld
{
    // Compiled programs survive a world publication. Only this small indirection changes owner.
    private sealed class RuleValueBinding(GameWorld world)
    {
        internal GameWorld World = world;
        internal double ReadWorld(string key) => World.ReadWorldValue(key);
        internal double ReadRule(string key) => World.ReadRuleValue(key);
    }
    private RuleValueBinding ruleValues;

    private DynamicsRuntime dynamics;
    private RuleEngine ruleEngine;
    internal RuleEngine RuleBook => ruleEngine;
    private string ruleProjectName = "ForesTycoon szabályrendszer";
    internal string ActiveRuleProjectName => ruleProjectName;
    private AlgorithmGraphReference algorithmGraphs;
    internal RuleProject RuleProjectDocument => new() { Name = ruleProjectName,
        Tuning = Tuning.ToValues(), Behaviors = BehaviorDocument, Dynamics = dynamics.Document, Algorithms = AlgorithmSnapshot(),
        Rules = RuleEngine.Clone(ruleEngine.Rules), RemovedStarters = ruleEngine.RemovedStarters.ToList(), Fields = RuleEngine.CloneFields(ruleEngine.Fields),
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
    internal static RuleProject DefaultRuleProject()
    {
        var project = RuleProject.CreateDefault();
        project.Algorithms = AlgorithmBuild.CurrentReference;
        return project;
    }
    internal static void ValidateRuleProject(RuleProject project)
    {
        _ = PrepareRuleProject(project, _ => 0, prepareExecutables: false);
    }
    internal static RuleProject NormalizeRuleProject(RuleProject project)
    {
        var prepared = PrepareRuleProject(project, _ => 0, prepareExecutables: false);
        prepared.Document.Tuning = prepared.Tuning.ToValues();
        return prepared.Document;
    }
    private static (RuleProject Document, GameTuning Tuning, DynamicsRuntime Dynamics, WorldBehaviorPolicy Policy, RuleEngine Engine)
        PrepareRuleProject(RuleProject project, Func<string, double> readWorld, Func<string, double> readRule = null, bool prepareExecutables = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        var copy = project.Clone();
        bool seedLegacyGraphs = copy.Version == 1;
        if (copy.Schema != "forest-rule-project" || copy.Version < 1 || copy.Version > RuleProject.CurrentVersion || string.IsNullOrWhiteSpace(copy.Name)
            || copy.Tuning == null || copy.Behaviors?.Graphs == null || copy.Behaviors.Graphs.Count > 512) throw new InvalidDataException("Ismeretlen vagy hiányos szabályprojekt.");
        if (copy.Algorithms is AlgorithmGraphReference algorithms && (algorithms.Revision?.Length != 64
            || !algorithms.Revision.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(algorithms.BundlePath)
            || !Path.IsPathFullyQualified(algorithms.BundlePath) || string.IsNullOrWhiteSpace(algorithms.WorkspaceRoot)
            || !Path.IsPathFullyQualified(algorithms.WorkspaceRoot))) throw new InvalidDataException("Hibás algoritmusgráf-hivatkozás.");
        copy.World?.Validate();
        foreach (var species in copy.World?.Species ?? new()) _ = ShapeOverride(species);
        copy.Rules ??= new(); copy.Fields ??= new(); copy.RemovedStarters ??= new();
        if (copy.Version == 1) RuleEngine.AddMissingStarters(copy.Rules, copy.RemovedStarters);
        RuleProjectMigrations.UpgradeProject(copy);
        var tuning = GameTuning.FromOverrides(copy.Tuning);
        var runtime = new DynamicsRuntime(copy.Dynamics, RuleWorldFields, readWorld);
        // Every host query has an explicit editable graph. Missing authored bindings remain inactive.
        foreach (var hook in WorldBehaviorPolicy.Hooks)
            if (!copy.Behaviors.Graphs.Any(g => g != null && g.Hook == hook.Id && g.Kind == hook.Kind && !g.ObjectId.HasValue))
                copy.Behaviors.Graphs.Add(seedLegacyGraphs ? WorldBehaviorPolicy.DefaultGraph(hook) : WorldBehaviorPolicy.InactiveGraph(hook));
        var policy = new WorldBehaviorPolicy(copy.Behaviors, DynamicsRuntime.Keys(copy.Dynamics, RuleWorldFields), runtime.Read, prepareExecutables);
        // Only old documents need migration. An authored book is authoritative, including an empty one.
        copy.Rules ??= new(); copy.Fields ??= new(); copy.RemovedStarters ??= new();
        var engine = new RuleEngine(copy.Rules, copy.Fields, copy.Dynamics.Stocks.Select(s => s.Id), readRule ?? (_ => 0), prepareExecutables) { RemovedStarters = copy.RemovedStarters };
        return (copy, tuning, runtime, policy, engine);
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
        var next = PrepareRuleProject(project, ruleValues.ReadWorld, ruleValues.ReadRule);
        InstallRuleProject(next);
    }

    private void InstallRuleProject((RuleProject Document, GameTuning Tuning, DynamicsRuntime Dynamics, WorldBehaviorPolicy Policy, RuleEngine Engine) next)
    {
        if (UpdatePending) throw new InvalidOperationException("A rule project is applied at a complete tick boundary.");
        next.Engine.Preserve(ruleEngine);
        if (dynamics != null) next.Dynamics.Preserve(dynamics);
        next.Policy.Controllers.PreserveUnchanged(behaviorPolicy.Controllers);
        bool refresh = ruleEngine == null || GrowthSignature(ruleEngine.Rules) != GrowthSignature(next.Engine.Rules)
            || ResponseSignature(behaviorPolicy.Document) != ResponseSignature(next.Policy.Document);
        dynamics = next.Dynamics; behaviorPolicy = next.Policy; ruleEngine = next.Engine; ruleProjectName = next.Document.Name;
        wildlife.InvalidateRules();
        // A rules-only project keeps the running scene (its map and missions); a project with a world replaces it.
        ruleProjectWorld = next.Document.World ?? ruleProjectWorld;
        // Species numbers are world content the rules read; a project change installs them for the whole process.
        var previousSpecies = ForestSpeciesBook.CaptureContext();
        ForestSpeciesBook.Apply(SpeciesEdits(next.Document.World));
        refresh |= !ReferenceEquals(previousSpecies, ForestSpeciesBook.CaptureContext());
        // Springs are part of the hydrology the rules run: a project change moves, adds or stops them at once.
        Environment.Springs = next.Document.World?.Springs;
        algorithmGraphs = next.Document.Algorithms ?? AlgorithmBuild.CurrentReference;
        BindBehaviors(); ApplyTuning(next.Tuning);
        if (refresh) { pendingForestRateRefresh = true; Environment.InvalidateVegetation(); }
        LastRuleSample = "Szabályprojekt alkalmazva; a következő események ezt futtatják.";
    }

    private IEnumerable<int> ApplyRuleProjectWork(RuleProject project) => CompileProjectWork(() => project);
    private IEnumerable<int> ApplyRuleProjectJsonWork(string json) => CompileProjectWork(() => RuleProject.FromJson(json));
    private IEnumerable<int> CompileProjectWork(Func<RuleProject> source)
    {
        var preparing = Task.Run(() => {
            var project = source(); RequireAlgorithmRuntime(project.Algorithms);
            return PrepareRuleProject(project, ruleValues.ReadWorld, ruleValues.ReadRule);
        });
        while (!preparing.IsCompleted) yield return 64;
        InstallRuleProject(preparing.GetAwaiter().GetResult());
    }

    private static string ResponseSignature(BehaviorModel model) => System.Text.Json.JsonSerializer.Serialize(model.Graphs.Where(g => EcologyResponseProgram.Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind)));
    private static string GrowthSignature(IEnumerable<WorldRule> rules) => System.Text.Json.JsonSerializer.Serialize(rules.Where(r => r.Schedule == "growth"));

    private void InitializeRuleProject()
    {
        dynamics = new(new DynamicsModel(), RuleWorldFields, ruleValues.ReadWorld);
        ApplyRuleProject(DefaultRuleProject());
    }
    private static void RequireAlgorithmRuntime(AlgorithmGraphReference reference)
    {
        if (reference != null && reference.Revision != AlgorithmBuild.CurrentRevision)
            throw new InvalidDataException("Az algoritmusgráf revíziója másik futtatót igényel; a projektkapcsolat előbb lefordítja és elindítja azt.");
    }
}
