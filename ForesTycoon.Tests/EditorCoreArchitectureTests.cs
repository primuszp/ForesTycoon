using System.Reflection;
using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class EditorCoreArchitectureTests
{
    [Fact]
    public void EditorCoreIsASeparateAssemblyWithNoTransitiveHostGraphicsOrUiDependency()
    {
        var core = typeof(EditorDocument).Assembly;
        Assert.Equal("ForesTycoon.Editor.Core", core.GetName().Name);
        Assert.NotEqual(typeof(RuleEditorView).Assembly, core);
        var visited = new HashSet<string>();
        void Visit(Assembly assembly)
        {
            if (!visited.Add(assembly.GetName().Name!)) return;
            foreach (var reference in assembly.GetReferencedAssemblies()) {
                Assert.DoesNotContain(reference.Name, new[] { "ForesTycoon", "ForesTycoon.Editor", "ImGui.NET", "ForesTycoon.Rendering", "ForesTycoon.Models", "ForesTycoon.Effects", "OpenTK.Graphics", "OpenTK.Windowing.Common", "OpenTK.Windowing.Desktop" });
                if (reference.Name!.StartsWith("ForesTycoon.")) Visit(Assembly.Load(reference));
            }
        }
        Visit(core);
    }

    [Fact]
    public void EditingPersistenceAndWorkflowServicesAreOwnedByTheCoreAssembly()
    {
        var core = typeof(EditorDocument).Assembly;
        foreach (var type in new[] { typeof(EditorBuffer<>), typeof(EditorHistoryService<>), typeof(EditorDocumentHistory),
                     typeof(BehaviorEditorState), typeof(BehaviorGraphEditor), typeof(BehaviorControllerEditor), typeof(DynamicsEditorState),
                     typeof(RuleBookEditor), typeof(RuleInfluenceEditor), typeof(EditorSceneCommands), typeof(EditorAuthoringCommands),
                     typeof(EditorTuning), typeof(ProjectPersistence), typeof(ProjectWorkflow), typeof(EditorProjectLoader), typeof(SandboxSession),
                     typeof(TrialService), typeof(WorldTrialResult), typeof(RuleBookSession), typeof(SceneSession), typeof(RuleLibrarySession),
                     typeof(RuleFieldAnalysis), typeof(RuleFieldCatalog) })
            Assert.Equal(core, type.Assembly);
    }

    [Fact]
    public void PageContractsExposeCommandsAndQueriesWithoutTheCompositionHostOrDocument()
    {
        foreach (var context in new[] { typeof(EditorPageContext), typeof(RuleWorkspaceContext), typeof(VehicleGeometryContext) })
            Assert.DoesNotContain(context.GetProperties(), property => property.PropertyType == typeof(EditorDocument)
                || property.PropertyType == typeof(RuleEditorView));
        Assert.IsAssignableFrom<IReadOnlyList<WorldRule>>(new EditorDocument().RuleBook.Entries);
        Assert.True(((ICollection<WorldRule>)new EditorDocument().RuleBook.Entries).IsReadOnly);
    }

    [Fact]
    public void CliAndGuiPublishThroughTheSameCoreWorkflowAndProtectExpectedRevisions()
    {
        string root = Path.Combine(Path.GetTempPath(), "editor-core-cli-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            string source = Path.Combine(root, "source.json"), destination = Path.Combine(root, "published.json");
            var project = SimulationWorld.DefaultRuleProject(); project.Algorithms = null;
            File.WriteAllText(source, project.ToJson());
            Assert.True(RuleProjectCli.Run(new[] { "--publish-project", source, destination, "--expected", "missing" }));
            Assert.Equal(SimulationWorld.NormalizeRuleProject(project).ContentRevision(), RuleProject.FromJson(File.ReadAllText(destination)).ContentRevision());
            string saved = File.ReadAllText(destination);
            Assert.Throws<IOException>(() => RuleProjectCli.Run(new[] { "--publish-project", source, destination, "--expected", "missing" }));
            Assert.Equal(saved, File.ReadAllText(destination)); Assert.False(File.Exists(destination + ".scene.json"));
        } finally { Directory.Delete(root, true); }
    }
}
