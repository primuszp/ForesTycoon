using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForesTycoon.Tests;

public class WorldLifecycleTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static GameWorld Create() => new(Settings, enableRendering: false);
    private static byte[] Save(GameWorld world)
    {
        using var stream = new MemoryStream(); world.Save(stream); return stream.ToArray();
    }
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    private static void BuildRoadAndTrail(GameWorld world)
    {
        for (int id = 0; id < world.Map.Tiles.Count && world.RoadCount == 0; id++)
        {
            world.QueueRoadPath(id, id + 1, false); world.ExecutePendingCommands();
        }
        for (int id = 0; id < world.Map.Tiles.Count && world.SkidTrailCount == 0; id++)
        {
            world.QueueSkidTrailPath(id, id + 1, false); world.ExecutePendingCommands();
        }
        Assert.True(world.RoadCount > 0 && world.SkidTrailCount > 0);
    }

    [Theory]
    [InlineData(8)] [InlineData(15)] [InlineData(17)]
    public void HeadlessSaveContinuesExactlyAcrossRoadWeatherBoundary(int ticks)
    {
        using var original = Create(); using var restored = Create();
        BuildRoadAndTrail(original);
        original.QueueWeather(WeatherPreset.Storm, 32, 20); original.ExecutePendingCommands();
        for (int i = 0; i < ticks; i++) original.Update(1.0 / 30);
        // Loading into a running world must replace its remainder too.
        restored.Update(0.4);
        using var stream = new MemoryStream(Save(original)); restored.Load(stream);
        Assert.False(original.HasPresentation); Assert.False(restored.HasPresentation);
        Assert.Equal(State(original), State(restored));
        for (int i = 0; i < 180; i++)
        {
            original.Update(1.0 / 30); restored.Update(1.0 / 30);
            Assert.Equal(State(original), State(restored));
        }
    }

    [Fact]
    public void RegenerationResetsEveryWorldStateAndPreservesExplicitTuning()
    {
        using var world = Create(); BuildRoadAndTrail(world);
        world.QueueTuning(new Dictionary<string, double> { ["DieselPrice"] = 1.5 }); world.ExecutePendingCommands();
        world.QueueHarvestForestArea(0, 255); world.ExecutePendingCommands(); world.Update(0.3);
        Assert.True(world.Expenses > 0);
        world.QueueRoadPath(0, 1, false);
        var nextSettings = Settings.WithSeed(43);
        world.Regenerate(nextSettings);
        using var fresh = new GameWorld(nextSettings, enableRendering: false);
        fresh.QueueTuning(world.Tuning.ToOverrides()); fresh.ExecutePendingCommands();
        // The only intentional difference is fresh's explicit tuning command journal.
        Assert.Equal(State(fresh), JsonSerializer.Serialize(world.CaptureCheckpoint() with { CommandCursor = 1 }));
        Assert.Equal(0, world.Expenses); Assert.Equal(0, world.ExecutePendingCommands());
        Assert.Equal(0, world.CaptureCheckpoint().RoadWeatherSeconds);
    }

    [Fact]
    public void FailedRegenerationKeepsLiveStateAndPendingInput()
    {
        using var world = Create(); BuildRoadAndTrail(world); world.Update(0.3);
        world.QueueWeather(WeatherPreset.Storm, 12, 20);
        byte[] before = Save(world);
        Assert.Throws<ArgumentNullException>(() => world.Regenerate(null!));
        Assert.Equal(before, Save(world));
        Assert.Equal(1, world.ExecutePendingCommands()); world.Update(1.0 / 30);
        Assert.True(Save(world).Length > 0);
    }

    [Fact]
    public void FailedRestoreKeepsLiveStateAndPendingInput()
    {
        using var world = Create(); BuildRoadAndTrail(world);
        world.QueueWeather(WeatherPreset.Storm, 12, 20);
        byte[] before = Save(world);
        var invalid = JsonNode.Parse(before)!;
        // Failure happens in the isolated candidate, after terrain has been restored.
        invalid["checkpoint"]!["ecology"]!["soilHash"] = "invalid";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => world.Load(stream));
        Assert.Equal(before, Save(world)); Assert.Equal(1, world.ExecutePendingCommands());
        world.Update(1.0 / 30);
    }

    [Theory]
    [InlineData(-0.1)] [InlineData(0.5)] [InlineData(double.PositiveInfinity)]
    public void InvalidRoadWeatherRemainderIsRejected(double remainder)
    {
        using var world = Create(); using var stream = new MemoryStream(Save(world));
        var save = WorldSaveSerializer.Read(stream);
        Assert.Throws<InvalidDataException>(() => new WorldSaveData { Terrain = save.Terrain, Climate = save.Climate,
            SoilModel = save.SoilModel, Checkpoint = save.Checkpoint with { RoadWeatherSeconds = remainder } }.Validate());
    }

    [Fact]
    public void PartiallyFailedWorldCannotContinueOrSaveAndCanRecoverByLoading()
    {
        using var world = Create(); byte[] valid = Save(world);
        var systems = (WorldSystemCollection)typeof(GameWorld).GetField("systems",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(world)!;
        systems.Add(new FailingSystem());
        Assert.Throws<IOException>(() => world.Update(1.0 / 30)); Assert.True(world.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => world.Update(1.0 / 30));
        Assert.Throws<InvalidOperationException>(() => Save(world));
        using var stream = new MemoryStream(valid); world.Load(stream);
        Assert.False(world.IsFaulted); world.Update(1.0 / 30);
        Assert.Equal((ulong)1, world.SimulationTick);
    }

    private sealed class FailingSystem : IWorldSystem
    {
        public void Update(double seconds) => throw new IOException("Injected subsystem failure.");
        public void Clear() { }
    }

    [Fact]
    public void SaveIdentifiesNativeRulesRuntime()
    {
        using var world = Create(); using var stream = new MemoryStream(Save(world));
        var data = WorldSaveSerializer.Read(stream);
        Assert.Equal(WorldSaveData.CurrentRuntimeRulesVersion, data.RuntimeRulesVersion);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("forestycoon-simulation/future")]
    public void IncompatibleRulesRuntimeIsRejectedWithoutChangingWorld(string? version)
    {
        using var world = Create(); world.Update(0.3); world.QueueWeather(WeatherPreset.Storm, 24, 20);
        byte[] before = Save(world);
        var invalid = JsonNode.Parse(before)!;
        if (version == null) invalid.AsObject().Remove("runtimeRulesVersion");
        else invalid["runtimeRulesVersion"] = version;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid.ToJsonString()));
        Assert.Throws<NotSupportedException>(() => world.Load(stream));
        Assert.Equal(before, Save(world)); Assert.Equal(1, world.ExecutePendingCommands());
    }

    [Fact]
    public void VersionTenSnapshotWithoutRoadRemainderStillLoads()
    {
        using var world = Create(); BuildRoadAndTrail(world);
        var legacy = JsonNode.Parse(Save(world))!;
        legacy["version"] = 10; legacy["checkpoint"]!["version"] = 1;
        legacy.AsObject().Remove("runtimeRulesVersion");
        legacy["checkpoint"]!.AsObject().Remove("roadWeatherSeconds");
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString()));
        using var restored = Create(); restored.Load(stream);
        Assert.Equal(world.RoadCount, restored.RoadCount);
        Assert.Equal(world.Expenses, restored.Expenses);
        Assert.Equal(0, restored.CaptureCheckpoint().RoadWeatherSeconds);
        restored.Update(1.0 / 30);
    }
}
