using System.Reflection;
using System.Text.Json;

namespace ForesTycoon.Tests;

public class SimulationModuleTests
{
    [Fact]
    public void SimulationDependencyClosureContainsNoPresentationOrGameHost()
    {
        var visited = new HashSet<string>();
        void Visit(Assembly assembly)
        {
            if (!visited.Add(assembly.GetName().Name!)) return;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name!;
                Assert.DoesNotContain(name, new[] { "ForesTycoon", "ForesTycoon.Editor", "ForesTycoon.Rendering",
                    "ForesTycoon.Effects", "ForesTycoon.Models", "ImGui.NET", "OpenTK.Graphics", "OpenTK.Windowing.Desktop", "OpenTK.Windowing.Common" });
                if (name.StartsWith("ForesTycoon.")) Visit(Assembly.Load(reference));
            }
        }
        Assert.NotEqual(typeof(GameWorld).Assembly, typeof(SimulationWorld).Assembly);
        Assert.Equal(typeof(SimulationWorld).Assembly, typeof(VehicleSystem).Assembly);
        Assert.Equal(typeof(SimulationWorld).Assembly, typeof(ForestryLogistics).Assembly);
        Assert.Equal(typeof(SimulationWorld).Assembly, typeof(WorldSaveSerializer).Assembly);
        Visit(typeof(SimulationWorld).Assembly);
    }

    [Fact]
    public void StandaloneAndGameHostProduceIdenticalCheckpointsAndInterchangeSaves()
    {
        var settings = TerrainSettings.Default.WithNodeSize(9, 42);
        using var simulation = new SimulationWorld(settings);
        using var host = new GameWorld(settings, enableRendering: false);
        foreach (SimulationWorld world in new SimulationWorld[] { simulation, host })
        {
            world.QueueWeather(WeatherPreset.Rain, 70, 30);
            world.ExecutePendingCommands();
            for (int i = 0; i < 45; i++) world.Update(1d / 30);
        }
        Assert.Equal(State(host), State(simulation));
        using var save = new MemoryStream(); host.Save(save);
        simulation.Regenerate(TerrainSettings.Default.WithNodeSize(9, 17));
        save.Position = 0; simulation.Load(save);
        Assert.Equal(State(host), State(simulation));
        simulation.Update(.1); host.Update(.1);
        Assert.Equal(State(host), State(simulation));
        save.SetLength(0); simulation.Save(save); save.Position = 0; host.Load(save);
        Assert.Equal(State(host), State(simulation));
    }

    [Fact]
    public void StandaloneScenarioCaptureAndSlicedGenerationPreserveTheWorld()
    {
        var project = SimulationWorld.DefaultRuleProject();
        project.World = SimulationWorld.DefaultScenario(seed: 17, size: 16);
        using var source = SimulationLoader.CreateProject(project);
        source.Update(.1);
        var captured = source.RuleProjectDocument;
        captured.World = SimulationLoader.CaptureScenario(source);
        using var restored = SimulationLoader.CreateProject(captured);
        using var restoredHost = WorldLoader.CreateProject(captured);
        Assert.Equal(State(restoredHost), State(restored));
        Assert.Equal(source.SimulationTick, restored.SimulationTick);
        Assert.Equal(source.ForestTreeCount, restored.ForestTreeCount);
        using var generated = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        using var generatedHost = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        generated.RequestScenario(captured.World, captured);
        generatedHost.RequestScenario(captured.World, captured);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (generated.GenerationPending || generatedHost.GenerationPending)
        {
            if (generated.GenerationPending) generated.ContinueGeneration(16);
            if (generatedHost.GenerationPending) generatedHost.ContinueGeneration(16);
            Thread.Yield();
            Assert.True(timeout.Elapsed < TimeSpan.FromMinutes(2), generated.GenerationStage);
        }
        Assert.Null(generated.GenerationError);
        Assert.False(generated.HasPresentation);
        Assert.Null(generatedHost.GenerationError);
        Assert.Equal(State(generatedHost), State(generated));
        generated.Update(.1); generatedHost.Update(.1);
        Assert.Equal(State(generatedHost), State(generated));
    }

    private static string State(SimulationWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
}
