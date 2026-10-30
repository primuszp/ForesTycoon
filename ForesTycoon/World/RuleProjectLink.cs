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
    private long handoffStarted;
    private readonly TimeSpan handoffTimeout;
    private readonly Action closeOldRuntime;
    private readonly Func<AlgorithmBuildResult, string, string, string, Process> launchRuntime;
    private readonly bool bootstrapAlgorithms, adoptRunning;
    private Task<string> exportSerialization;
    private Task exportWriting;
    private bool exportCapturing;
    private TerrainMap exportMap;
    internal bool ExportPending => exportCapturing || exportSerialization != null || exportWriting != null;
    internal bool HandoffPending => replacement != null;
    internal string Error { get; private set; }
    internal string Path => path;
    internal RuleProjectLink(string path = null, Action closeOldRuntime = null,
        Func<AlgorithmBuildResult, string, string, string, Process> launchRuntime = null, bool bootstrapAlgorithms = true, bool adoptRunning = false,
        TimeSpan? handoffTimeout = null) {
        // adoptRunning: the world already runs its scene's rules; only projects published after it started replace them.
        this.adoptRunning = adoptRunning;
        this.path = System.IO.Path.GetFullPath(path ?? RuleProject.DefaultPath); this.closeOldRuntime = closeOldRuntime;
        this.launchRuntime = launchRuntime ?? ((runtime, save, ready, project) => AlgorithmBuild.StartRuntime(runtime, new[] { "--algorithm-handoff", save, ready }, project));
        this.bootstrapAlgorithms = bootstrapAlgorithms;
        this.handoffTimeout = handoffTimeout ?? TimeSpan.FromMinutes(2);
        if (this.handoffTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(handoffTimeout));
    }
    internal void Poll(GameWorld world, double elapsed)
    {
        PollExport(world);
        if (world.BoundaryPending) return;
        if (replacement != null) {
            if (File.Exists(acknowledgement)) { closeOldRuntime(); return; }
            if (replacement.HasExited) {
                Error = "Az új algoritmusfuttató betöltése sikertelen (kilépési kód: " + replacement.ExitCode + "). A korábbi játék folytatódik.";
                replacement.Dispose(); replacement = null; rejectedJson = buildingJson;
            }
            else if (Stopwatch.GetElapsedTime(handoffStarted) >= handoffTimeout) {
                try {
                    replacement.Kill(entireProcessTree: true);
                    if (!replacement.WaitForExit(1000)) { Error = "Az új futtató leállítása folyamatban; az átadás szünetel."; return; }
                    replacement.Dispose(); replacement = null; rejectedJson = buildingJson;
                    Error = "Az új algoritmusfuttató nem igazolta vissza időben a betöltést. A korábbi játék folytatódik.";
                } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) {
                    Error = "Az új futtató leállítása sikertelen: " + e.Message;
                }
            }
            return;
        }
        if (world.ForegroundPending) return;
        if (building?.IsCompleted == true) {
            try {
                var result = building.GetAwaiter().GetResult();
                if (File.ReadAllText(path) == buildingJson) {
                    if (result.Project != buildingJson) { RuleProject.WriteAtomic(path, result.Project); buildingJson = result.Project; }
                    // The host finishes pending input in slices before Poll reaches this boundary.
                    // Runtime migration requires a complete checkpoint without pending commands.
                    string handoff = System.IO.Path.Combine(AlgorithmBuild.CacheRoot, "handoff-" + Guid.NewGuid().ToString("N"));
                    acknowledgement = handoff + ".ready";
                    using var save = new MemoryStream(); world.Save(save);
                    RuleProject.WriteAtomic(handoff + ".json", System.Text.Encoding.UTF8.GetString(save.ToArray()));
                    replacement = launchRuntime(result.Runtime, handoff + ".json", acknowledgement, path);
                    handoffStarted = Stopwatch.GetTimestamp();
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
                // No published project yet: publish the running one, which is installed by definition.
                if (!File.Exists(path)) { installed = world.RuleProjectDocument.ToJson(); RuleProject.WriteAtomic(path, installed); }
                string json = File.ReadAllText(path);
                if (installed == null && adoptRunning && building == null && replacement == null) installed = json;
                if (json == installed && building == null && replacement == null) Error = null;
                if (json != installed && json != rejectedJson && building == null && replacement == null) {
                    RuleProject project;
                    // A project the game cannot run is reported once and kept out; the running rules continue.
                    try { project = RuleProject.FromJson(json); GameWorld.ValidateRuleProject(project); }
                    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { rejectedJson = json; throw; }
                    string stale = AlgorithmBuild.StaleReason(project.Algorithms);
                    // Do not install only part of a project whose algorithm snapshot cannot run.
                    if (stale != null) { rejectedJson = json; throw new InvalidDataException("A projekt algoritmusmásolata elavult (" + stale + "); a projekt nem lett alkalmazva."); }
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
                        Error = null;
                    }
                }
            } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { Error = e.Message; }
        }
        if (publish <= 0 && !ExportPending && !world.ForegroundPending && !world.BoundaryPending) {
            publish = 3;
            exportCapturing = true; exportMap = world.Map;
            world.RequestRuleWorldExport("Futó játék", Error, task => { exportCapturing = false; exportSerialization = task; });
        }
    }

    private void PollExport(GameWorld world)
    {
        if (exportCapturing && !ReferenceEquals(exportMap, world.Map)) exportCapturing = false;
        if (exportWriting?.IsCompleted == true) {
            try { exportWriting.GetAwaiter().GetResult(); }
            catch (Exception e) { Error = e.Message; }
            exportWriting = null;
        }
        if (exportSerialization?.IsCompleted == true) {
            try {
                string json = exportSerialization.GetAwaiter().GetResult();
                if (ReferenceEquals(exportMap, world.Map))
                    exportWriting = Task.Run(() => RuleProject.WriteAtomic(path + ".world.json", json));
            }
            catch (Exception e) { Error = e.Message; }
            exportSerialization = null;
        }
    }
}
