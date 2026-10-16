using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForesTycoon.Tests;

/// <summary>Bounded logical ticks, exact replay and direct-reader equivalence.</summary>
[Collection("SpeciesBook")]
public sealed class RuleStepTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Ticks(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Update(1d / 30); }
    private static GameWorld Raining()
    {
        var world = World();
        world.QueueWeather(WeatherPreset.Rain, 30, 120); world.ExecutePendingCommands();
        return world;
    }
    private static string State(GameWorld world) =>
        JsonSerializer.Serialize(world.CaptureCheckpoint()) + " " + RuleStepBenchmark.Fingerprint(world) + " " + JsonSerializer.Serialize(world.RuleBook.FlowTotals);

    [Fact]
    public void DirectTileReadersGiveExactlyTheViewsValues()
    {
        using var world = Raining();
        Ticks(world, 90);
        var fields = RuleEngine.Scope("tile").Fields.Where(f => f.ViewRead != null).ToArray();
        Assert.Contains(fields, f => f.Name == "surfaceWater");
        Assert.Contains(fields, f => f.Name == "drop");
        for (int id = 0; id < world.Map.Tiles.Count; id++)
            foreach (var f in fields)
                Assert.True(BitConverter.DoubleToInt64Bits(f.ViewRead(world, id)) == BitConverter.DoubleToInt64Bits(f.Read(world, id)),
                    $"{f.Name} on tile {id}: view {f.ViewRead(world, id)}, direct {f.Read(world, id)}");
    }

    [Fact]
    public void ALogicalTickYieldsWithAFixedBudgetAndPublishesOnlyWhenDone()
    {
        using var world = Raining();
        using var reference = Raining();
        int yielded = 0;
        for (int tick = 0; tick < 60; tick++) {
            reference.Update(1d / 30);
            while (!world.ContinueUpdate(1d / 30, 7)) {
                Assert.Equal((ulong)tick, world.SimulationTick);
                Assert.InRange(world.LastUpdateWorkUnits, 1, 7);
                yielded++;
            }
            Assert.False(world.RuleBook.StepPending);
            Assert.Equal(State(reference), State(world));
        }
        Assert.True(yielded > 100);
    }

    [Fact]
    public void AStepInSlicesEndsExactlyAsTheWholeStep()
    {
        using var whole = World(); using var sliced = World();
        foreach (var world in new[] { whole, sliced }) world.Environment.ForceWeather(WeatherPreset.Rain, 30, 120);
        whole.RuleBook.Run(whole, .002, 0);
        sliced.RuleBook.Begin(sliced, .002, 0);
        int slices = 0;
        while (sliced.RuleBook.StepPending) { sliced.RuleBook.Continue(sliced, 97); slices++; }
        Assert.True(slices > 5);
        Assert.Null(sliced.RuleBook.LastError);
        Assert.NotEmpty(sliced.RuleBook.FlowTotals);
        Assert.Equal(State(whole), State(sliced));
    }

    [Fact]
    public void ASaveInTheMiddleOfAStepContinuesExactly()
    {
        using var world = Raining();
        Ticks(world, 40);
        world.RuleBook.Begin(world, .002, 0);
        world.RuleBook.Continue(world, 97);
        Assert.True(world.RuleBook.StepPending);
        Assert.NotNull(world.CaptureCheckpoint().RuleStep);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.True(restored.RuleBook.StepPending);
        Assert.Equal(State(world), State(restored));
        Ticks(world, 45); Ticks(restored, 45);
        Assert.Equal(State(world), State(restored));
    }

    [Fact]
    public void AReplayCutsTheStepsTheSameWay()
    {
        using var world = Raining();
        Ticks(world, 37);
        var middle = world.CaptureCheckpoint();
        Assert.False(world.UpdatePending);
        Ticks(world, 50);
        using var save = new MemoryStream(); world.Save(save);
        // The same save from a checkpoint in the middle of a step, and from the start: both replay up to this tick.
        foreach (var checkpoint in new[] { middle, null }) {
            var document = JsonNode.Parse(save.ToArray())!.AsObject();
            document["checkpoint"] = checkpoint == null ? null : JsonSerializer.SerializeToNode(checkpoint);
            using var edited = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
            using var replayed = World(); replayed.Load(edited);
            Assert.True(replayed.LastLoadReplayedTicks > 0);
            Assert.Equal(State(world), State(replayed));
        }
    }
}
