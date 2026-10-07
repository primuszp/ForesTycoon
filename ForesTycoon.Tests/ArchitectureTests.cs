using System.Reflection;

namespace ForesTycoon.Tests;

// The layering is Engine <- Ecology <- TreeModels <- game (terrain, rendering, app). Lower layers know
// nothing about higher ones and none of the three libraries touches OpenGL, windowing or UI.
public class ArchitectureTests
{
    private static readonly Assembly Engine = typeof(FixedStepClock).Assembly;
    private static readonly Assembly Ecology = typeof(Ecosystem).Assembly;
    private static readonly Assembly TreeModels = typeof(TreeScale).Assembly;
    private static readonly Assembly Game = typeof(GameWorld).Assembly;

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => !n.StartsWith("System") && n != "netstandard" && n != "mscorlib").Distinct();

    [Fact]
    public void EngineKnowsOnlyMathAndTheBaseLibrary() =>
        Assert.Equal(new[] { "OpenTK.Mathematics" }, References(Engine).OrderBy(n => n));

    [Fact]
    public void EcologyDependsOnlyOnTheEngine() =>
        Assert.Equal(new[] { "ForesTycoon.Engine", "OpenTK.Mathematics" }, References(Ecology).OrderBy(n => n));

    [Fact]
    public void TreeModelsDependOnlyOnEcologyEngineAndTheGenerator() =>
        Assert.Equal(new[] { "DendroKit.Core", "ForesTycoon.Ecology", "ForesTycoon.Engine", "OpenTK.Mathematics" },
            References(TreeModels).OrderBy(n => n));

    [Fact]
    public void TheGameIsTheOnlyLayerThatSeesAllOthers()
    {
        var names = References(Game).ToHashSet();
        Assert.Contains("ForesTycoon.Engine", names);
        Assert.Contains("ForesTycoon.Ecology", names);
        Assert.Contains("ForesTycoon.TreeModels", names);
    }

    [Theory]
    [InlineData("ForesTycoon.Engine")]
    [InlineData("ForesTycoon.Ecology")]
    [InlineData("ForesTycoon.TreeModels")]
    public void LibrarySourcesNeverUseGraphicsWindowingOrUi(string project)
    {
        string dir = Path.Combine(RepositoryRoot(), project);
        var forbidden = new[] { "OpenTK.Graphics", "OpenTK.Windowing", "OpenTK.Input", "ImGuiNET", "System.Windows" };
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);
            foreach (string word in forbidden) Assert.False(text.Contains(word), $"{file} uses {word}");
        }
    }

    [Fact]
    public void EachLibraryKeepsItsOwnNamespace()
    {
        static IEnumerable<Type> Own(Assembly assembly) => assembly.GetTypes()
            .Where(t => !t.FullName!.Contains('<') && t.Namespace is not ("System.Runtime.CompilerServices" or "Microsoft.CodeAnalysis"));
        Assert.All(Own(Engine), t => Assert.StartsWith("ForesTycoon.Engine", t.Namespace));
        Assert.All(Own(Ecology), t => Assert.StartsWith("ForesTycoon.Ecology", t.Namespace));
        Assert.All(Own(TreeModels), t => Assert.StartsWith("ForesTycoon.TreeModels", t.Namespace));
    }

    [Fact]
    public void EcosystemRunsHeadlessWithoutTerrainOrRendering()
    {
        var habitat = new ForestSystemTests.TestHabitat(64, seed: 5);
        var ecosystem = new Ecosystem(habitat);
        for (int i = 0; i < 200; i++) ecosystem.Update(1.0 / 30);
        Assert.True(ecosystem.Environment.Time > 0);
        Assert.True(ecosystem.Forest.Count > 0);
        Assert.Equal(EcologyTime.DefaultGameSecondsPerYear, ecosystem.ForestYearSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Ecosystem(habitat, 5));
        ecosystem.Reset(habitat, EcologyTime.SecondsPerForestYear);
        Assert.Equal(EcologyTime.SecondsPerForestYear, ecosystem.ForestYearSeconds);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ForesTycoon.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("ForesTycoon.sln");
    }
}
