using System.Reflection;

namespace ForesTycoon.Tests;

// The layering is Engine <- Ecology <- TreeModels <- game (terrain, rendering, app). Lower layers know
// nothing about higher ones and none of the three libraries touches OpenGL, windowing or UI.
public class ArchitectureTests
{
    private static readonly Assembly Engine = typeof(FixedStepClock).Assembly;
    private static readonly Assembly Ecology = typeof(Ecosystem).Assembly;
    private static readonly Assembly TreeModels = typeof(TreeScale).Assembly;
    private static readonly Assembly Renderer = typeof(RenderDevice).Assembly;
    private static readonly Assembly Models = typeof(AnimatedGlbModel).Assembly;
    private static readonly Assembly Effects = typeof(WeatherVisualState).Assembly;
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
    public void TreeModelsContainTheGeneratorAndDependOnlyOnEcologyAndEngine() =>
        Assert.Equal(new[] { "ForesTycoon.Ecology", "ForesTycoon.Engine", "OpenTK.Mathematics" },
            References(TreeModels).OrderBy(n => n));

    [Fact]
    public void TheGpuCoreDependsOnlyOnTheEngineAndOpenTk() =>
        Assert.Equal(new[] { "ForesTycoon.Engine", "OpenTK" }, References(Renderer).Where(n => !n.StartsWith("OpenTK.")).Append("OpenTK").Distinct().OrderBy(n => n).ToArray());

    [Fact]
    public void ModelLoadingBuildsOnTheGpuCoreAloneAndEffectsAddOnlyTheEcosystemWeather()
    {
        static void OnlyDependsOn(Assembly assembly, params string[] allowed) =>
            Assert.All(References(assembly).Where(n => n.StartsWith("ForesTycoon")), n => Assert.Contains(n, allowed));
        OnlyDependsOn(Models, "ForesTycoon.Engine", "ForesTycoon.Rendering");
        OnlyDependsOn(Effects, "ForesTycoon.Engine", "ForesTycoon.Rendering", "ForesTycoon.Ecology");
        Assert.Contains("ForesTycoon.Rendering", References(Models));
        Assert.Contains("ForesTycoon.Ecology", References(Effects));
        foreach (var assembly in new[] { Renderer, Models, Effects })
            Assert.DoesNotContain(References(assembly), n => n is "ForesTycoon" or "ForesTycoon.TreeModels" or "ImGui.NET");
    }

    [Fact]
    public void TheGameIsTheOnlyLayerThatSeesAllOthers()
    {
        var names = References(Game).ToHashSet();
        Assert.Contains("ForesTycoon.Engine", names);
        Assert.Contains("ForesTycoon.Ecology", names);
        Assert.Contains("ForesTycoon.TreeModels", names);
        Assert.Contains("ForesTycoon.Rendering", names);
        Assert.Contains("ForesTycoon.Models", names);
        Assert.Contains("ForesTycoon.Effects", names);
    }

    [Theory]
    [InlineData("ForesTycoon.Rendering")]
    [InlineData("ForesTycoon.Models")]
    [InlineData("ForesTycoon.Effects")]
    public void RendererModulesNeverUseWindowingOrUi(string project)
    {
        string dir = Path.Combine(RepositoryRoot(), project);
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);
            foreach (string word in new[] { "OpenTK.Windowing", "ImGuiNET", "System.Windows" })
                Assert.False(text.Contains(word), $"{file} uses {word}");
        }
    }

    [Theory]
    [InlineData("ForesTycoon.Engine")]
    [InlineData("ForesTycoon.Ecology")]
    [InlineData("ForesTycoon.TreeModels")]
    [InlineData("ForesTycoon.Map")]
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
    public void ModelCpuContractsDoNotDependOnNativeGraphics()
    {
        string directory = Path.Combine(RepositoryRoot(), "ForesTycoon.Models");
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(file);
            foreach (string forbidden in new[] { "OpenTK.Graphics", "GL.", "RenderStateScope", "#version" })
                Assert.False(source.Contains(forbidden), $"{file} leaks backend detail: {forbidden}");
        }
    }

    [Theory]
    [InlineData("ForesTycoon.Rendering")]
    [InlineData("ForesTycoon.Models")]
    [InlineData("ForesTycoon.Effects")]
    [InlineData("ForesTycoon/App")]
    [InlineData("ForesTycoon/Terrain")]
    [InlineData("ForesTycoon/Rendering")]
    public void NativeGraphicsStayInsideBackendImplementations(string sourceDirectory)
    {
        string directory = Path.Combine(RepositoryRoot(), sourceDirectory);
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            string[] segments = Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment => segment is "OpenGl" or "obj" or "bin")) continue;
            string source = File.ReadAllText(file);
            foreach (string forbidden in new[] { "OpenTK.Graphics", "GL.", "#version", "PrimitiveType.",
                         "BufferUsageHint.", "ContextAPI.OpenGL", "ContextProfile.Core" })
                Assert.False(source.Contains(forbidden), $"{file} leaks backend detail: {forbidden}");
        }
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

    private sealed class Settings : IWeatherSettings
    {
        public bool Weather => true;
        public bool Lightning => false;
        public int LightningRequest => 0;
        public bool AutomaticWeather => false;
        public WeatherPreset Preset { get; init; }
        public bool ExperimentalSnow => false;
        public int RainBudget => 1000;
        public int CloudSteps => 8;
    }

    [Fact]
    public void WeatherEffectsRunWithoutAGraphicsContext()
    {
        var state = new WeatherVisualState();
        var rain = new Settings { Preset = WeatherPreset.Rain };
        for (double t = 0; t < 10; t += 0.1) state.Update(t, rain);
        Assert.True(state.Rain > 0.9f && state.Cloud > 0.9f);
        var clear = new Settings { Preset = WeatherPreset.Sunny };
        for (double t = 10; t < 40; t += 0.1) state.Update(t, clear);
        Assert.True(state.Rain < 0.1f);
    }

    [Fact]
    public void WorldEffectsExpireOnTheEngineClock()
    {
        var effects = new WorldEffectSystem();
        effects.Spawn(WorldEffectKind.TreePlanted, new OpenTK.Mathematics.Vector3(1, 2, 3));
        Assert.Equal(1, effects.Count);
        for (int i = 0; i < 400; i++) effects.Update(1.0 / 30);
        Assert.Equal(0, effects.Count);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ForesTycoon.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("ForesTycoon.sln");
    }
}
