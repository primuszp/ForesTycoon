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
    private readonly Guid sessionId = Guid.NewGuid();
    private (SetRuleProjectCommand Command, string Revision, string AlgorithmRevision) pendingApplication;
    private string appliedRevision, appliedAlgorithmRevision, attemptedRevision, applicationError;
    private ulong? appliedTick;
    private double statusRefresh;
    private string installed;
    private TerrainMap lastMap;
    private double poll, publish;
    private Task<(AlgorithmBuildResult Runtime, string Project)> building;
    private string buildingJson, buildingFileRevision, rejectedJson, acknowledgement;
    private Process replacement;
    private string handoffProject, handoffCheckpoint;
    private RuntimeHandoffAcknowledgement expectedHandoff;
    private bool transferred;
    private long handoffStarted;
    private readonly TimeSpan handoffTimeout;
    private readonly Action closeOldRuntime;
    private readonly Func<AlgorithmBuildResult, string, string, string, string, Process> launchRuntime;
    private readonly bool bootstrapAlgorithms, adoptRunning;
    private Task<string> exportSerialization;
    private Task exportWriting;
    private bool exportCapturing;
    private bool exportRequested;
    private TerrainMap exportMap;
    internal bool ExportPending => exportCapturing || exportSerialization != null || exportWriting != null;
    internal bool HandoffPending => replacement != null || transferred;
    internal string Error { get; private set; }
    internal string Path => path;
    internal RuleProjectLink(string path = null, Action closeOldRuntime = null,
        Func<AlgorithmBuildResult, string, string, string, string, Process> launchRuntime = null, bool bootstrapAlgorithms = true, bool adoptRunning = false,
        TimeSpan? handoffTimeout = null) {
        // adoptRunning: the world already runs its scene's rules; only projects published after it started replace them.
        this.adoptRunning = adoptRunning;
        this.path = System.IO.Path.GetFullPath(path ?? RuleProject.DefaultPath); this.closeOldRuntime = closeOldRuntime;
        this.launchRuntime = launchRuntime ?? ((runtime, save, ready, snapshot, project) =>
            AlgorithmBuild.StartRuntime(runtime, new[] { "--algorithm-handoff", save, ready, snapshot }, project));
        this.bootstrapAlgorithms = bootstrapAlgorithms;
        this.handoffTimeout = handoffTimeout ?? TimeSpan.FromMinutes(2);
        if (this.handoffTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(handoffTimeout));
    }
    internal void Poll(GameWorld world, double elapsed, bool allowAutomaticWorldExport = true)
    {
        if (transferred) return;
        if (lastMap != world.Map) {
            installed = null; poll = publish = statusRefresh = 0; lastMap = world.Map;
            pendingApplication = default;
            appliedRevision = appliedAlgorithmRevision = attemptedRevision = applicationError = null;
            appliedTick = null;
        }
        ObserveApplication(world);
        PollProject(world, elapsed, allowAutomaticWorldExport);
        if (replacement == null && !transferred) PublishStatus(elapsed);
    }

    private void PollProject(GameWorld world, double elapsed, bool allowAutomaticWorldExport)
    {
        PollExport(world);
        if (world.BoundaryPending) return;
        if (replacement != null) {
            if (expectedHandoff.Matches(acknowledgement)) { transferred = true; FinishHandoff(); closeOldRuntime(); return; }
            if (replacement.HasExited) {
                Error = applicationError = "Az új algoritmusfuttató betöltése sikertelen (kilépési kód: " + replacement.ExitCode + "). A korábbi játék folytatódik.";
                statusRefresh = 0;
                FinishHandoff(); rejectedJson = buildingJson;
            }
            else if (Stopwatch.GetElapsedTime(handoffStarted) >= handoffTimeout) {
                try {
                    replacement.Kill(entireProcessTree: true);
                    if (!replacement.WaitForExit(1000)) { Error = "Az új futtató leállítása folyamatban; az átadás szünetel."; return; }
                    FinishHandoff(); rejectedJson = buildingJson;
                    Error = applicationError = "Az új algoritmusfuttató nem igazolta vissza időben a betöltést. A korábbi játék folytatódik.";
                    statusRefresh = 0;
                } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) {
                    Error = "Az új futtató leállítása sikertelen: " + e.Message;
                }
            }
            return;
        }
        if (world.ForegroundPending || pendingApplication.Command != null) return;
        if (building?.IsCompleted == true) {
            try {
                var result = building.GetAwaiter().GetResult();
                if (RuleProjectFile.Revision(path) == buildingFileRevision) {
                    if (result.Project != buildingJson) {
                        try { RuleProjectFile.Write(path, result.Project, buildingFileRevision); }
                        catch (IOException error) {
                            // A concurrent writer can win while compilation finishes. Poll the file again.
                            Error = error.Message; building = null; poll = 0; return;
                        }
                        buildingJson = result.Project;
                    }
                    // The host finishes pending input in slices before Poll reaches this boundary.
                    // Runtime migration requires a complete checkpoint without pending commands.
                    string handoff = System.IO.Path.Combine(AlgorithmBuild.CacheRoot, "handoff-" + Guid.NewGuid().ToString("N"));
                    acknowledgement = handoff + ".ready";
                    handoffProject = handoff + ".project.json";
                    handoffCheckpoint = handoff + ".json";
                    expectedHandoff = new(RuleProject.FromJson(result.Project).ContentRevision(), result.Runtime.Revision, world.SimulationTick);
                    using var save = new MemoryStream(); world.Save(save);
                    RuleProject.WriteAtomic(handoffCheckpoint, System.Text.Encoding.UTF8.GetString(save.ToArray()));
                    RuleProject.WriteAtomic(handoffProject, result.Project);
                    replacement = launchRuntime(result.Runtime, handoffCheckpoint, acknowledgement, handoffProject, path);
                    handoffStarted = Stopwatch.GetTimestamp();
                    Error = "Az új algoritmusfuttató a világot tölti; a szimuláció az átadásig szünetel.";
                }
            } catch (Exception e) { FinishHandoff(); Error = applicationError = e.Message; rejectedJson = buildingJson; statusRefresh = 0; }
            building = null;
            if (replacement != null) return;
        }
        poll -= Math.Max(0, elapsed); publish -= Math.Max(0, elapsed);
        if (poll <= 0) {
            poll = .5;
            try {
                if (File.Exists(path + ".world.request")) {
                    File.Delete(path + ".world.request"); exportRequested = true;
                }
                // No published project yet: publish the running one, which is installed by definition.
                var (json, fileRevision) = RuleProjectFile.Read(path);
                if (json == null) {
                    var runningProject = world.RuleProjectDocument;
                    json = runningProject.ToJson();
                    fileRevision = RuleProjectFile.Write(path, json, expected: null);
                    installed = json;
                    RecordApplied(runningProject.ContentRevision(), runningProject.Algorithms?.Revision, world.SimulationTick);
                }
                if (installed == null && adoptRunning && building == null && replacement == null) installed = json;
                if (json == installed && building == null && replacement == null) {
                    if (applicationError != null) statusRefresh = 0;
                    Error = applicationError = null;
                }
                if (json != installed && json != rejectedJson && building == null && replacement == null) {
                    attemptedRevision = applicationError = null;
                    RuleProject project;
                    // A project the game cannot run is reported once and kept out; the running rules continue.
                    try {
                        project = RuleProject.FromJson(json);
                        attemptedRevision = project.ContentRevision();
                        GameWorld.ValidateRuleProject(project);
                    }
                    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { rejectedJson = json; throw; }
                    string stale = AlgorithmBuild.StaleReason(project.Algorithms);
                    // Do not install only part of a project whose algorithm snapshot cannot run.
                    if (stale != null) { rejectedJson = json; throw new InvalidDataException("A projekt algoritmusmásolata elavult (" + stale + "); a projekt nem lett alkalmazva."); }
                    if (project.Algorithms == null && AlgorithmBuild.CurrentReference is AlgorithmGraphReference current) {
                        project.Algorithms = current; json = project.ToJson();
                        fileRevision = RuleProjectFile.Write(path, json, fileRevision);
                    }
                    if (project.Algorithms == null && bootstrapAlgorithms && closeOldRuntime != null) {
                        string workspace = AlgorithmBuild.FindWorkspace() ?? throw new InvalidDataException("A teljes algoritmusgráfok első létrehozásához hiányzik a munkaterület.");
                        buildingJson = json; buildingFileRevision = fileRevision;
                        building = Task.Run(async () => {
                            project.Algorithms = AlgorithmBuild.Store(AlgorithmBundle.ImportWorkspace(workspace), workspace, path);
                            var runtime = await AlgorithmBuild.CompileAsync(project.Algorithms);
                            return (runtime, project.ToJson());
                        });
                        Error = "A teljes játék alap-algoritmusgráfjai készülnek; a jelenlegi szimuláció folytatódik.";
                    }
                    else if (project.Algorithms != null && project.Algorithms.Revision != AlgorithmBuild.CurrentRevision) {
                        if (closeOldRuntime == null) throw new InvalidDataException("Ez a host nem támogat algoritmusfuttató-cserét.");
                        buildingJson = json; buildingFileRevision = fileRevision;
                        building = Task.Run(async () => (await AlgorithmBuild.CompileAsync(project.Algorithms), project.ToJson()));
                        Error = "Az új algoritmusgráfok fordítása folyamatban; a játék a jelenlegi szabályokkal fut.";
                    } else {
                        var command = world.QueueRuleProject(project);
                        pendingApplication = (command, attemptedRevision, project.Algorithms?.Revision ?? AlgorithmBuild.CurrentRevision);
                        installed = json; rejectedJson = null;
                        Error = null;
                    }
                }
            } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) {
                Error = applicationError = e.Message;
                statusRefresh = 0;
            }
        }
        // Capturing the entire object catalog owns a world boundary. Never do that periodically
        // during gameplay: it stalls every simulated animation, even when no editor is open.
        if ((exportRequested || allowAutomaticWorldExport && publish <= 0) && !ExportPending && !world.ForegroundPending && !world.BoundaryPending) {
            publish = 3;
            exportRequested = false;
            exportCapturing = true; exportMap = world.Map;
            world.RequestRuleWorldExport("Futó játék", Error, task => { exportCapturing = false; exportSerialization = task; });
        }
    }

    private void FinishHandoff()
    {
        replacement?.Dispose();
        replacement = null;
        foreach (string file in new[] { acknowledgement, handoffProject, handoffCheckpoint }) {
            if (file == null) continue;
            try { File.Delete(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal void AdoptHandoff(GameWorld world, RuleProject project, ulong tick)
    {
        lastMap = world.Map;
        installed = project.ToJson();
        RecordApplied(project.ContentRevision(), project.Algorithms?.Revision, tick);
    }

    private void ObserveApplication(GameWorld world)
    {
        if (pendingApplication.Command?.AppliedTick is ulong tick && !world.BoundaryPending) {
            RecordApplied(pendingApplication.Revision, pendingApplication.AlgorithmRevision, tick);
            pendingApplication = default;
        }
    }

    private void RecordApplied(string revision, string algorithmRevision, ulong tick)
    {
        appliedRevision = revision;
        appliedAlgorithmRevision = algorithmRevision;
        appliedTick = tick;
        statusRefresh = 0;
    }

    private void PublishStatus(double elapsed)
    {
        statusRefresh -= Math.Max(0, elapsed);
        if (statusRefresh > 0) return;

        statusRefresh = 3;
        try {
            new RuleProjectStatus(sessionId, appliedRevision, appliedAlgorithmRevision, appliedTick,
                applicationError == null ? null : attemptedRevision, applicationError, DateTimeOffset.UtcNow).Write(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Error = error.Message; }
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
