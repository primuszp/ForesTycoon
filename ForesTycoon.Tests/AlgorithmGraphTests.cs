using ForesTycoon.RuleCompiler;
using ForesTycoon.Rules;
using System.Text.Json;
using ForesTycoon;

namespace ForesTycoon.Tests;

public class AlgorithmGraphTests
{
    [Fact]
    public void StructuralGraphRoundTripsControlFlowTriviaAndInterpolation()
    {
        const string source = "// graph\nclass Rule { double Step(double value) { if (value > 2) { value *= 3; } foreach (var x in new[]{1,2}) value += x; System.Console.WriteLine($\"v={value:0.00}\"); return value; } }\n";
        var file = AlgorithmBundle.Import("ForesTycoon/World/Test.cs", source);
        Assert.Contains(file.Nodes, n => n.Kind == "IfStatement"); Assert.Contains(file.Nodes, n => n.Kind == "ForEachStatement");
        Assert.Equal(source, AlgorithmBundle.Render(file));
        file.Nodes.Single(n => n.Token && n.Text == "3").Text = "7";
        Assert.Contains("value *= 7", AlgorithmBundle.Render(file));
        var comparison = file.Nodes.Single(n => n.Token && n.Text == ">"); comparison.Text = "<";
        Assert.Contains("value < 2", AlgorithmBundle.Render(file));
    }
    [Fact]
    public void GraphPortsCanInsertDuplicateAndReorderInstructions()
    {
        var file = AlgorithmBundle.Import("ForesTycoon/World/Test.cs", "class Rule { void Step() { double value = 2; } }");
        var block = file.Nodes.Single(n => n.Kind == "Block");
        string statement = AlgorithmBundle.InsertTemplate(file, 4); block.Children.Insert(block.Children.Count - 1, statement);
        var copied = AlgorithmBundle.Duplicate(file, statement); block.Children.Insert(block.Children.Count - 1, copied);
        Assert.Equal(2, AlgorithmBundle.Render(file).Split("value = 0;").Length - 1);
    }
    [Fact]
    public void MalformedConnectionsAndSourceTextInTokenAreRejected()
    {
        var file = AlgorithmBundle.Import("ForesTycoon/World/Test.cs", "class Rule { double value = 1; }");
        var literal = file.Nodes.Single(n => n.Token && n.Text == "1"); literal.Text = "1; double secret = 2";
        Assert.Throws<InvalidDataException>(() => AlgorithmBundle.Render(file)); literal.Text = "1";
        file.Nodes[0].Children.Add(file.Root); Assert.Throws<InvalidDataException>(() => AlgorithmBundle.Render(file));
    }
    [Fact]
    public void DataGraphEditsDefinitionsAndSemanticRevisionIgnoresLayout()
    {
        var bundle = new AlgorithmBundle(); var file = AlgorithmBundle.ImportData("ForesTycoon.Ecology/Soil/profiles.json", "{\"soil\":[{\"capacity\":0.2,\"name\":\"loam\"}]}"); bundle.Files.Add(file);
        string before = bundle.Revision(); file.Nodes[0].X = 30; Assert.Equal(before, bundle.Revision());
        file.Nodes.Single(n => n.Kind == "JsonNumber").Text = "0.8";
        Assert.Contains("0.8", AlgorithmBundle.Render(file)); Assert.NotEqual(before, bundle.Revision());
        Assert.Equal(bundle.Revision(), AlgorithmBundle.FromJson(bundle.ToJson()).Revision());
    }
    [Fact]
    public void BundleDoesNotWriteOutsideKnownProjectRootsAndChecksImmutableRevision()
    {
        var bundle = new AlgorithmBundle(); bundle.Files.Add(AlgorithmBundle.Import("ForesTycoon/../outside.cs", "class R {}"));
        Assert.Throws<InvalidDataException>(() => bundle.RenderAll());
        bundle.Files[0].Path = "ForesTycoon/World/R.cs";
        string root = Path.Combine(Path.GetTempPath(), "forest-graphs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var reference = AlgorithmBuild.Store(bundle, root, Path.Combine(root, "project.json"));
            Assert.Equal(reference.Revision, AlgorithmBuild.Read(reference).Revision());
            bundle.Files[0].Nodes.Single(n => n.Text == "R").Text = "Changed"; File.WriteAllText(reference.BundlePath, bundle.ToJson());
            Assert.Throws<InvalidDataException>(() => AlgorithmBuild.Read(reference));
        } finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void EveryImportedGameAlgorithmRoundTripsWithoutOpaqueMethodBodies()
    {
        string root = AlgorithmBuild.FindWorkspace()!; var bundle = AlgorithmBundle.ImportWorkspace(root);
        Assert.Contains(bundle.Files, f => f.Path.EndsWith("EnvironmentSystem.cs"));
        Assert.Contains(bundle.Files, f => f.Path.EndsWith("WildlifeSystem.cs"));
        foreach (var file in bundle.Files.Where(f => f.Format == "csharp")) Assert.Equal(File.ReadAllText(Path.Combine(root, file.Path)), AlgorithmBundle.Render(file));
        Assert.Contains(bundle.Files, f => f.Format == "json");
    }
    [Fact]
    public async Task CompiledGraphsExecuteInternalAlgorithmEditsAndMigrateWorldState()
    {
        string workspace = AlgorithmBuild.FindWorkspace()!; var bundle = AlgorithmBundle.ImportWorkspace(workspace);
        var file = bundle.Files.Single(f => f.Path == "ForesTycoon/World/GameWorld.cs"); var index = file.Index();
        var balance = index[file.Entries.Single(e => e.Name == "Balance").Id];
        var arrow = balance.Children.Select(id => index[id]).Single(n => n.Kind == "ArrowExpressionClause");
        string expression = AlgorithmBundle.InsertTemplate(file, 0);
        AlgorithmSemantics.SetValue(file.Nodes.Single(n => n.Id == expression).Children.Select(id => file.Nodes.Single(n => n.Id == id)).Single(), "123.0");
        arrow.Children[1] = expression;
        string output = Path.Combine(workspace, "artifacts", "algorithm-integration"); Directory.CreateDirectory(output);
        string projectPath = Path.Combine(output, "project.json"), savePath = Path.Combine(output, "world.json"), tracePath = Path.Combine(output, "trace.json");
        var project = GameWorld.DefaultRuleProject(); project.Algorithms = AlgorithmBuild.Store(bundle, workspace, projectPath);
        RuleProject.WriteAtomic(projectPath, project.ToJson());
        using (var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false)) {
            world.Update(.1); Assert.NotEqual(123, world.Balance);
            using var stream = File.Create(savePath); world.Save(stream);
        }
        var compiled = await AlgorithmBuild.CompileAsync(project.Algorithms);
        using var process = AlgorithmBuild.StartRuntime(compiled, new[] { "--run-rule-project", projectPath, "30", tracePath, "--world", savePath, "--migrate" }, projectPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token); Assert.Equal(0, process.ExitCode);
        using var trace = JsonDocument.Parse(File.ReadAllText(tracePath));
        Assert.Equal(project.Algorithms.Revision, trace.RootElement.GetProperty("revision").GetString());
        Assert.Equal(1UL, trace.RootElement.GetProperty("trace")[0].GetProperty("tick").GetUInt64());
        Assert.Equal(31UL, trace.RootElement.GetProperty("trace")[1].GetProperty("tick").GetUInt64());
        Assert.Equal(123, trace.RootElement.GetProperty("trace")[1].GetProperty("fields").GetProperty("world.balance").GetDouble());
        // The live host keeps the old world until the new runtime acknowledges its restored snapshot.
        bool closed = false; string linkTrace = Path.Combine(output, "handoff-trace.json");
        using (var oldWorld = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false)) {
            oldWorld.Update(.1);
            var link = new RuleProjectLink(projectPath, () => closed = true, (runtime, save, ready, path) => AlgorithmBuild.StartRuntime(runtime,
                new[] { "--run-rule-project", path, "0", linkTrace, "--world", save, "--migrate", "--ack", ready }, path));
            for (int i = 0; i < 1200 && !closed; i++) { link.Poll(oldWorld, .5); await Task.Delay(50); }
            Assert.True(closed, link.Error); Assert.Equal(1UL, oldWorld.SimulationTick);
            using var handoff = JsonDocument.Parse(File.ReadAllText(linkTrace));
            Assert.Equal(123, handoff.RootElement.GetProperty("trace")[0].GetProperty("fields").GetProperty("world.balance").GetDouble());
        }
        // Semantically invalid graphs do not replace the already compiled executable.
        file.Nodes.Single(n => n.Id == expression).Children.Select(id => file.Nodes.Single(n => n.Id == id)).Single().Text = "missing_graph_variable";
        var invalid = AlgorithmBuild.Store(bundle, workspace, projectPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => AlgorithmBuild.CompileAsync(invalid));
        Assert.True(File.Exists(compiled.AssemblyPath));
        Assert.Equal(project.Algorithms.Revision, RuleProject.FromJson(File.ReadAllText(projectPath)).Algorithms.Revision);
    }
}
