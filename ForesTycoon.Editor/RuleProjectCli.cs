using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForesTycoon.Rules;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Editor;

/// <summary>The same compiler and world adapter as the GUI, usable by an agent without graphics.</summary>
internal static class RuleProjectCli
{
    internal static bool Run(string[] args)
    {
        if (args.Length == 0) return false;
        if (args.Length == 2 && args[0] == "--export-opening-scene") {
            var project = WorldLoader.OpeningProject(); project.Algorithms = null;
            RuleProject.WriteAtomic(args[1], project.ToJson());
            Console.WriteLine("Nyitójelenet projektje mentve: " + Path.GetFullPath(args[1])); return true;
        }
        if (args[0] == "--compare-project" && args.Length == 6 && args[4] == "--world") {
            if (!int.TryParse(args[2], out int ticks) || ticks < 0 || ticks > 100000) throw new ArgumentException("0–100000 tick szükséges.");
            string save = Path.GetFullPath(args[5]); using var input = File.OpenRead(save); var checkpoint = WorldSaveSerializer.Read(input).Checkpoint;
            if (checkpoint == null || checkpoint.PendingCommands != 0) throw new InvalidDataException("Függő parancs nélküli checkpoint szükséges.");
            var baseline = new RuleProject { Version = checkpoint.RuleBook?.ProjectVersion ?? 2, Name = checkpoint.RuleProjectName ?? "Mentés szabályai",
                Rules = checkpoint.RuleBook?.Rules == null ? RuleProject.CreateDefault().Rules : RuleEngine.Clone(checkpoint.RuleBook.Rules),
                Fields = checkpoint.RuleBook?.Fields == null ? new() : RuleEngine.CloneFields(checkpoint.RuleBook.Fields),
                RemovedStarters = checkpoint.RuleBook?.RemovedStarters?.ToList() ?? new(),
                Tuning = checkpoint.Tuning ?? new(), Behaviors = checkpoint.Behaviors ?? WorldBehaviorPolicy.DefaultDocument(), Dynamics = checkpoint.Dynamics ?? new(), Algorithms = checkpoint.Algorithms };
            var edited = ReadRuntimeProject(args[1]); GameWorld.ValidateRuleProject(edited);
            var result = RuleTrialRunner.Run(baseline, edited, save, Path.GetFullPath(args[3]), edited.ContentRevision(), save, ticks, default).GetAwaiter().GetResult();
            Console.WriteLine(result.OutputPath); return true;
        }
        if (args[0] == "--publish-project" && args.Length == 5 && args[3] == "--expected") {
            string destination = Path.GetFullPath(args[2]), expected = args[4]; var guard = new ProjectFileGuard(); guard.Observe(destination);
            if ((ProjectFileGuard.Revision(destination) ?? "missing") != expected) throw new IOException("A célprojekt verziója eltér az elvárttól; nem írtam felül.");
            var project = GameWorld.NormalizeRuleProject(ReadRuntimeProject(args[1]));
            if (project.Algorithms != null) _ = AlgorithmBuild.CompileAsync(project.Algorithms).GetAwaiter().GetResult();
            guard.RequireUnchanged(destination); ProjectHistory.Archive(destination); RuleProject.WriteAtomic(destination, project.ToJson()); Console.WriteLine(project.ContentRevision()); return true;
        }
        if (args[0] == "--project-file-revision" && args.Length == 2) { Console.WriteLine(ProjectFileGuard.Revision(args[1]) ?? "missing"); return true; }
        if (args[0] == "--export-algorithm-graphs" && args.Length == 3) {
            var bundle = AlgorithmBundle.ImportWorkspace(args[1]); RuleProject.WriteAtomic(args[2], bundle.ToJson());
            Console.WriteLine($"{bundle.Files.Count} gráf, {bundle.Files.Sum(f => f.Nodes.Count)} csomópont; {bundle.Revision()}"); return true;
        }
        if (args[0] == "--attach-algorithm-graphs" && args.Length == 4) {
            var project = RuleProject.FromJson(File.ReadAllText(args[1])); var bundle = AlgorithmBundle.FromJson(File.ReadAllText(args[2]));
            project.Algorithms = AlgorithmBuild.Store(bundle, args[3], args[1]); GameWorld.ValidateRuleProject(project);
            RuleProject.WriteAtomic(args[1], project.ToJson()); Console.WriteLine(project.Algorithms.Revision); return true;
        }
        if (args[0] == "--compile-project" && args.Length == 2) {
            var project = ReadRuntimeProject(args[1]); GameWorld.ValidateRuleProject(project);
            if (project.Algorithms == null) throw new ArgumentException("A projektben nincs algoritmusgráf.");
            var result = AlgorithmBuild.CompileAsync(project.Algorithms).GetAwaiter().GetResult(); Console.WriteLine(result.AssemblyPath); return true;
        }
        if (args[0] == "--new-project" && args.Length == 2) {
            var project = GameWorld.DefaultRuleProject(); var workspace = AlgorithmBuild.FindWorkspace();
            if (workspace != null) project.Algorithms = AlgorithmBuild.Store(AlgorithmBundle.ImportWorkspace(workspace), workspace, args[1]);
            RuleProject.WriteAtomic(args[1], project.ToJson()); Console.WriteLine(Path.GetFullPath(args[1])); return true;
        }
        if (args[0] == "--validate-project" && args.Length == 2) {
            var project = ReadRuntimeProject(args[1]); GameWorld.ValidateRuleProject(project);
            if (project.Algorithms != null) _ = AlgorithmBuild.Read(project.Algorithms);
            Console.WriteLine($"Valid: {project.Name}; {project.Behaviors.Graphs.Count} expressions; {project.Behaviors.Controllers.Count} controllers; {project.Dynamics.Stocks.Count} stocks; {project.Dynamics.Flows.Count} flows."); return true;
        }
        if (args[0] == "--export-system-model" && (args.Length == 2 || args.Length == 4 && args[2] == "--project")) {
            var project = args.Length == 4 ? RuleProject.FromJson(File.ReadAllText(args[3])) : GameWorld.DefaultRuleProject();
            var topics = project.Authoring?.Entries ?? new();
            var model = RuleWorkshopModel.SystemModel(CurrentGameRules.Build(), project.Dynamics, project.Behaviors.Graphs,
                id => topics.TryGetValue(id, out var entry) ? entry.Topic : 5);
            object Loop(SystemLoop l) => new { variables = l.Variables, steps = l.Steps, subsystems = model.Span(l) };
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
            RuleProject.WriteAtomic(args[1], JsonSerializer.Serialize(new {
                schema = "forest-system-model-analysis", version = 1,
                note = "Edges are catalogued data access (reads -> writes), not execution order or proven sign. Extend the vocabulary in ForesTycoon.Rules/Catalog/system-model.json.",
                subsystems = model.Vocabulary.Subsystems, variables = model.Variables,
                processes = model.Processes.Select(p => new { p.Id, p.Name, p.Subsystem, p.Reads, p.Writes, p.RuleId,
                    stocksChanged = model.StocksChangedBy(p.Id).Select(v => v.Id), loops = model.LoopsThroughProcess(p.Id).Take(5).Select(Loop) }),
                influences = model.Influences, coupledGroups = model.CoupledGroups, keyLoops = model.KeyLoops.Select(Loop),
                openStocks = model.Variables.Where(v => v.Kind == SystemVariableKind.Stock && model.CoupledGroupOf(v.Id) == null).Select(v => v.Id)
            }, options));
            Console.WriteLine($"{model.Variables.Count} mennyiség, {model.Processes.Count} folyamat, {model.KeyLoops.Count} fő kör: {Path.GetFullPath(args[1])}"); return true;
        }
        if (args[0] == "--export-rule-api" && args.Length == 2) {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
            RuleProject.WriteAtomic(args[1], JsonSerializer.Serialize(new {
                schema = "forest-rule-api", version = 1, projectPath = RuleProject.DefaultPath,
                hooks = WorldBehaviorPolicy.Hooks, worldFields = GameWorld.RuleWorldFields,
                objectModel = GameWorld.RuleModel,
                operations = Enum.GetValues<BehaviorOperation>().Select(op => new { name = op.ToString(), inputs = BehaviorModel.Inputs(op) }),
                algorithms = new { schema = "forest-algorithm-graphs", version = 1, nodeKinds = AlgorithmBundle.NodeKinds,
                    templates = AlgorithmBundle.Templates, projects = AlgorithmBundle.Projects,
                    ports = "ordered child node IDs", terminal = "one value, identifier or operator", currentRevision = AlgorithmBuild.CurrentRevision },
                authoring = new { templateSchema = "forest-rule-template-library", topics = RuleWorkshopModel.Topics.Select(t => t.Name),
                    entries = "rule ID, graph.ID, stock.ID or flow.ID -> topic index and notes", drafts = "project.json.draft.json",
                    validation = "--validate-project draft.json", comparison = "--compare-project draft.json ticks folder --world checkpoint.json",
                    publication = "--publish-project draft.json project.json --expected file-revision; read with --project-file-revision" },
                defaultProject = GameWorld.DefaultRuleProject()
            }, options)); return true;
        }
        if (args[0] == "--export-world" && args.Length == 3) {
            using var world = Sandbox(args[1]); RuleProject.WriteAtomic(args[2], world.ExportRuleWorld(Path.GetFullPath(args[1])).ToJson()); return true;
        }
        if (args[0] == "--simulate-project" && args.Length is 4 or 6) {
            if (!int.TryParse(args[2], out int ticks) || ticks < 0 || ticks > 100000) throw new ArgumentException("0–100000 tick szükséges.");
            if (args.Length == 6 && args[4] != "--world") throw new ArgumentException("--world játékmentés.json szükséges.");
            var graphProject = ReadRuntimeProject(args[1]);
            if (graphProject.Algorithms != null) {
                var runtime = AlgorithmBuild.CompileAsync(graphProject.Algorithms).GetAwaiter().GetResult();
                string snapshot = Path.Combine(AlgorithmBuild.CacheRoot, "cli-projects", graphProject.ContentRevision() + ".json"); RuleProject.WriteAtomic(snapshot, graphProject.ToJson());
                var forwarded = new List<string> { "--run-rule-project", snapshot, args[2], Path.GetFullPath(args[3]) };
                if (args.Length == 6) { forwarded.Add("--world"); forwarded.Add(Path.GetFullPath(args[5])); }
                using var process = AlgorithmBuild.StartRuntime(runtime, forwarded, args[1]); process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidDataException("A gráfszimuláció sikertelen; kilépési kód: " + process.ExitCode);
                return true;
            }
            using var world = args.Length == 6 ? Sandbox(args[5]) : WorldLoader.CreateProject(graphProject);
            if (args.Length == 6) { world.QueueRuleProject(graphProject); world.ExecutePendingCommands(); }
            var trace = new List<object>();
            for (int i = 0; i <= ticks; i++) {
                if (i % 30 == 0 || i == ticks) trace.Add(new { tick = world.SimulationTick, seconds = i / 30d, stocks = world.DynamicsValues.ToDictionary(p => p.Key, p => p.Value) });
                if (i < ticks) world.Update(1d / 30);
                if (world.DynamicsError != null || world.BehaviorError != null) throw new InvalidDataException(world.DynamicsError ?? world.BehaviorError);
            }
            RuleProject.WriteAtomic(args[3], JsonSerializer.Serialize(new { trace, world = world.ExportRuleWorld("CLI szimuláció") }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase })); return true;
        }
        return false;
    }
    private static GameWorld Sandbox(string save)
    {
        var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        try { if (save != null) { using var input = File.OpenRead(save); world.Load(input); } return world; }
        catch { world.Dispose(); throw; }
    }
    private static RuleProject ReadRuntimeProject(string file)
    {
        var project = RuleProject.FromJson(File.ReadAllText(file));
        if (project.Algorithms?.Revision == "draft") {
            var bundle = AlgorithmBundle.FromJson(File.ReadAllText(project.Algorithms.BundlePath));
            project.Algorithms = AlgorithmBuild.Store(bundle, project.Algorithms.WorkspaceRoot,
                Path.Combine(AlgorithmBuild.CacheRoot, "prepared-drafts", bundle.Revision() + ".json"));
        }
        return project;
    }
}
