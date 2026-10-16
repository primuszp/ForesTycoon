using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon;

/// <summary>Host IO only. A file change becomes one validated journal command, never an unscheduled world mutation.</summary>
internal sealed class RuleProjectLink
{
    private readonly string path;
    private string installed;
    private TerrainMap lastMap;
    private double poll, publish;
    private Task<(AlgorithmBuildResult Runtime, string Project)> building;
    private string buildingJson, rejectedJson, acknowledgement;
    private Process replacement;
    private readonly Action closeOldRuntime;
    private readonly Func<AlgorithmBuildResult, string, string, string, Process> launchRuntime;
    private readonly bool bootstrapAlgorithms, adoptRunning;
    internal bool HandoffPending => replacement != null;
    internal string Error { get; private set; }
    internal string Path => path;
    internal RuleProjectLink(string path = null, Action closeOldRuntime = null,
        Func<AlgorithmBuildResult, string, string, string, Process> launchRuntime = null, bool bootstrapAlgorithms = true, bool adoptRunning = false) {
        // adoptRunning: the world already runs its scene's rules; only projects published after it started replace them.
        this.adoptRunning = adoptRunning;
        this.path = System.IO.Path.GetFullPath(path ?? RuleProject.DefaultPath); this.closeOldRuntime = closeOldRuntime;
        this.launchRuntime = launchRuntime ?? ((runtime, save, ready, project) => AlgorithmBuild.StartRuntime(runtime, new[] { "--algorithm-handoff", save, ready }, project));
        this.bootstrapAlgorithms = bootstrapAlgorithms;
    }
    internal void Poll(GameWorld world, double elapsed)
    {
        if (world.BoundaryPending) return;
        if (replacement != null) {
            if (File.Exists(acknowledgement)) { closeOldRuntime(); return; }
            if (replacement.HasExited) {
                Error = "Az új algoritmusfuttató betöltése sikertelen (kilépési kód: " + replacement.ExitCode + "). A korábbi játék folytatódik.";
                replacement.Dispose(); replacement = null; rejectedJson = buildingJson;
            }
            return;
        }
        if (building?.IsCompleted == true) {
            try {
                var result = building.GetAwaiter().GetResult();
                if (File.ReadAllText(path) == buildingJson) {
                    if (result.Project != buildingJson) { RuleProject.WriteAtomic(path, result.Project); buildingJson = result.Project; }
                    world.ExecutePendingCommands();
                    string handoff = System.IO.Path.Combine(AlgorithmBuild.CacheRoot, "handoff-" + Guid.NewGuid().ToString("N"));
                    acknowledgement = handoff + ".ready";
                    using var save = new MemoryStream(); world.Save(save);
                    RuleProject.WriteAtomic(handoff + ".json", System.Text.Encoding.UTF8.GetString(save.ToArray()));
                    replacement = launchRuntime(result.Runtime, handoff + ".json", acknowledgement, path);
                    Error = "Az új algoritmusfuttató a világot tölti; a szimuláció az átadásig szünetel.";
                }
            } catch (Exception e) { Error = e.Message; rejectedJson = buildingJson; }
            building = null;
        }
        poll -= Math.Max(0, elapsed); publish -= Math.Max(0, elapsed);
        if (lastMap != world.Map) { installed = null; poll = publish = 0; lastMap = world.Map; }
        if (poll <= 0) {
            poll = .5;
            try {
                if (!File.Exists(path)) RuleProject.WriteAtomic(path, world.RuleProjectDocument.ToJson());
                string json = File.ReadAllText(path);
                if (installed == null && adoptRunning && building == null && replacement == null) installed = json;
                if (json == installed && building == null && replacement == null) Error = null;
                if (json != installed && json != rejectedJson && building == null && replacement == null) {
                    var project = RuleProject.FromJson(json); GameWorld.ValidateRuleProject(project);
                    string stale = AlgorithmBuild.StaleReason(project.Algorithms);
                    // An outdated code snapshot never replaces the running code; the rules, formulas and map still apply.
                    if (stale != null) project.Algorithms = AlgorithmBuild.CurrentReference;
                    if (project.Algorithms == null && AlgorithmBuild.CurrentReference is AlgorithmGraphReference current) {
                        project.Algorithms = current; json = project.ToJson(); RuleProject.WriteAtomic(path, json);
                    }
                    if (project.Algorithms == null && bootstrapAlgorithms && closeOldRuntime != null) {
                        string workspace = AlgorithmBuild.FindWorkspace() ?? throw new InvalidDataException("A teljes algoritmusgráfok első létrehozásához hiányzik a munkaterület.");
                        buildingJson = json;
                        building = Task.Run(async () => {
                            project.Algorithms = AlgorithmBuild.Store(AlgorithmBundle.ImportWorkspace(workspace), workspace, path);
                            var runtime = await AlgorithmBuild.CompileAsync(project.Algorithms);
                            return (runtime, project.ToJson());
                        });
                        Error = "A teljes játék alap-algoritmusgráfjai készülnek; a jelenlegi szimuláció folytatódik.";
                    }
                    else if (project.Algorithms != null && project.Algorithms.Revision != AlgorithmBuild.CurrentRevision) {
                        if (closeOldRuntime == null) throw new InvalidDataException("Ez a host nem támogat algoritmusfuttató-cserét.");
                        buildingJson = json; building = Task.Run(async () => (await AlgorithmBuild.CompileAsync(project.Algorithms), project.ToJson()));
                        Error = "Az új algoritmusgráfok fordítása folyamatban; a játék a jelenlegi szabályokkal fut.";
                    } else {
                        world.QueueRuleProject(project); installed = json; rejectedJson = null;
                        Error = stale == null ? null : "A projekt algoritmusmásolata elavult (" + stale + "); a játék a saját kódjával fut, a szabályokat átvette.";
                    }
                }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { Error = e.Message; }
        }
        if (publish <= 0) {
            publish = 3;
            try { RuleProject.WriteAtomic(path + ".world.json", world.ExportRuleWorld("Futó játék", Error).ToJson()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = e.Message; }
        }
    }
}

sealed partial class GameWorld
{
    internal RuleWorldCatalog ExportRuleWorld(string source, string error = null) => new() {
        Source = source, Tick = SimulationTick, Seed = map.Settings.Seed,
        AlgorithmRevision = algorithmGraphs?.Revision ?? "",
        ProjectRevision = RuleProjectDocument.ContentRevision(),
        PublishedUtc = DateTime.UtcNow.ToString("O"), ProjectName = ruleProjectName, Error = error ?? DynamicsError ?? BehaviorError,
        Objects = RuleObjectKinds
            .SelectMany(kind => BehaviorTargets(kind).Select(t => new RuleWorldObject(kind, t.Id, t.Label))).ToList(),
        Hooks = WorldBehaviorPolicy.Hooks.ToList(), Fields = RuleWorldFields.ToDictionary(k => k, ReadWorldValue), Stocks = dynamics.Capture()
    };
}
