using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class ForestCompetitionGraphTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 2;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .6f;
        public float GetNormalizedElevation(int id) => .4f;
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id * 100, 0, 1, 1);
        public int GetAdjacentTileIds(int id, Span<int> target) { target[0] = 1 - id; return 1; }
    }
    private static GameWorld World(double year = EcologyTime.DefaultGameSecondsPerYear) =>
        new(TerrainSettings.Default.WithNodeSize(17, 42), forestYearSeconds: year, enableRendering: false);
    private static void Constant(RuleProject project, string hook, double value)
    {
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == hook);
        graph.Nodes = new() { new() { Id = "value", Name = "Választott hatás", Operation = BehaviorOperation.Constant, Value = value } }; graph.Output = "value";
    }
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    [Fact]
    public void AuthoredPairSourcesAreNotSilentlyCulledByTheOldCrownRange()
    {
        var habitat = new Habitat(); var store = new ForestTreeStore();
        for (int tile = 0; tile < 2; tile++) {
            store.Create(tile, new(ForestSpecies.Oak, 30, 1, 1), 0, 42); store.TryGet(tile, out var p);
            while (p.Count > 1) store.RemoveLiving(p, p.Count - 1);
            p.Trees[0] = p.Trees[0] with { U = 0, V = 0, Dimensions = new(.3f, tile == 0 ? 2 : 20, .1f), AnnualGrowth = default };
        }
        store.TryGet(0, out var own); store.TryGet(1, out var neighbor);
        var competition = new ForestCompetition(); competition.Snapshot(habitat, store, 0);
        Assert.Equal(1, competition.Evaluate(habitat, own.Trees[0], 0, 1).Light);
        var project = GameWorld.DefaultRuleProject();
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "forest.shading");
        graph.Nodes = new() { new() { Id = "height", Name = "Szomszéd magassága", Operation = BehaviorOperation.Input, Key = "neighbor.height" } }; graph.Output = "height";
        Constant(project, "forest.crowding", .5);
        var rules = EcologyResponseProgram.FromModel(project.Behaviors);
        neighbor.Trees[0] = neighbor.Trees[0] with { Dimensions = new(.3f, 99, .1f) }; // Snapshot must stay immutable.
        var loads = new WorkResult<ForestCompetitionLoads>(); int units = 0;
        foreach (int unit in competition.EvaluateLoadsWork(habitat, own.Trees[0], 0, loads, rules)) { units++; Assert.Equal(1, unit); }
        Assert.True(units >= 4);
        Assert.Equal(20, loads.Value.Shade); Assert.Equal(.5, loads.Value.CrownLoad);
        var result = ForestCompetition.Resolve(loads.Value, .7f, .8f, rules);
        Assert.True(result.Light < .8f); Assert.True(result.Space < 1); Assert.Equal(.7f, result.Water);
        store.RemoveTile(1);
        Assert.True(competition.SnapshotAffectedWork(habitat, store, 0, new() { 0, 1 }).Count() >= 4,
            "An empty dependency must yield as well as a living snapshot.");
    }

    [Fact]
    public void PreparedLoadsRunTheNonlinearAuthoredLightGraphWithActualRadiation()
    {
        var project = GameWorld.DefaultRuleProject();
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "forest.light");
        graph.Nodes = new() {
            new() { Id = "sun", Name = "Besugárzás", Operation = BehaviorOperation.Input, Key = "environment.radiation" },
            new() { Id = "two", Name = "Kitevő", Operation = BehaviorOperation.Constant, Value = 2 },
            new() { Id = "response", Name = "Saját fényválasz", Operation = BehaviorOperation.Power, A = "sun", B = "two" }
        }; graph.Output = "response";
        (ForestSystem Forest, ForestEnvironmentCoordinator Clock) Create(bool prepare) {
            var habitat = new Habitat(); var rules = EcologyResponseProgram.FromModel(project.Behaviors);
            var forest = new ForestSystem(habitat, secondsPerYear: 24, responseRules: rules);
            forest.Clear(); forest.Plant(0, ForestSpecies.Oak); forest.Plant(1, ForestSpecies.Oak);
            var water = new EnvironmentSystem(habitat, forest) { ResponseRules = rules };
            water.ForceWeather(WeatherPreset.Cloudy, 0, 600);
            return (forest, new(forest, water, prepare));
        }
        var prepared = Create(true); var direct = Create(false);
        prepared.Clock.Update(12); direct.Clock.Update(12);
        Assert.True(prepared.Forest.LastMonthlyPreparedTrees > 0);
        Assert.Equal(direct.Forest.Statistics, prepared.Forest.Statistics);
        for (int tile = 0; tile < 2; tile++) {
            Assert.True(direct.Forest.IndividualTrees.TryGet(tile, out var a));
            Assert.True(prepared.Forest.IndividualTrees.TryGet(tile, out var b));
            Assert.Equal(a.Trees, b.Trees);
        }
    }

    [Fact]
    public void GrowthStressAndTreeFieldsUseTheProjectLightAndResourceResponses()
    {
        using var world = World(); var project = world.RuleProjectDocument;
        Constant(project, "forest.light", .71); Constant(project, "forest.space", .62);
        Constant(project, "forest.lightResponse", .31); Constant(project, "forest.resourceLimit", .22);
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Update(1d / 30);
        Assert.All(world.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)), tree => {
            Assert.Equal(.71f, tree.Resources.Light, 6); Assert.Equal(.62f, tree.Resources.Space, 6);
            Assert.Equal(.31f, tree.Resources.LightResponse(tree.Species, world.Forest.ResponseRules), 6);
            Assert.Equal(.22f, tree.Resources.Limitation(tree.Species, world.Forest.ResponseRules), 6);
        });
        var scope = RuleEngine.Scope("tree"); SimulationWork.Complete(scope.PrepareElements(world));
        Assert.Equal(.31, scope.Fields.Single(f => f.Name == "lightResponse").Read(world, 0), 6);
        Assert.Equal(.22, scope.Fields.Single(f => f.Name == "resourceLimit").Read(world, 0), 6);
    }

    [Fact]
    public void GraphWrittenHealthInvalidatesCompetitionSnapshotsAndRates()
    {
        using var world = World(); var project = world.RuleProjectDocument;
        void Source(string hook, string field) {
            var graph = project.Behaviors.Graphs.Single(g => g.Hook == hook);
            graph.Nodes = new() { new() { Id = "source", Name = "Választott forrás", Operation = BehaviorOperation.Input, Key = field } };
            graph.Output = "source";
        }
        Source("forest.shading", "neighbor.health"); Source("forest.light", "competition.shade");
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Update(1d / 30);
        Assert.Contains(world.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)), t => t.Resources.Light > 0);
        var scope = RuleEngine.Scope("tree"); SimulationWork.Complete(scope.PrepareElements(world));
        var health = scope.Fields.Single(f => f.Name == "health");
        foreach (int id in scope.PreparedElements(world)) health.Write(world, id, 0);
        SimulationWork.Complete(world.Forest.CompleteRuleEditsWork(world.Forest.ForestYear));
        Assert.All(world.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)), t => Assert.Equal(0, t.Resources.Light));
    }

    [Fact]
    public void PairSlicingPendingSaveAndCommandJournalReplayStayExact()
    {
        using var whole = World(120); using var sliced = World(120); var project = whole.RuleProjectDocument;
        Constant(project, "forest.shading", .14); Constant(project, "forest.crowding", .29);
        whole.QueueRuleProject(project);
        using var pending = new MemoryStream(); whole.Save(pending); pending.Position = 0; sliced.Load(pending);
        whole.ExecutePendingCommands(); do { sliced.ContinueCommands(3); Thread.Yield(); } while (sliced.CommandPending);
        for (int tick = 0; tick < 225; tick++) {
            whole.Update(1d / 30);
            while (!sliced.ContinueUpdate(1d / 30, 3)) Thread.Yield();
        }
        Assert.Equal(State(whole), State(sliced));
        Assert.NotEmpty(whole.Forest.Capture().Preparation.Prepared);
        using var midMonth = new MemoryStream(); whole.Save(midMonth); midMonth.Position = 0; sliced.Load(midMonth);
        for (int tick = 0; tick < 180; tick++) {
            whole.Update(1d / 30);
            while (!sliced.ContinueUpdate(1d / 30, 3)) Thread.Yield();
        }
        Assert.Equal(State(whole), State(sliced));
        using var save = new MemoryStream(); whole.Save(save); save.Position = 0; var data = WorldSaveSerializer.Read(save);
        using var journal = new MemoryStream();
        WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate,
            Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; sliced.Load(journal);
        Assert.Equal(State(whole), State(sliced));
    }
}
