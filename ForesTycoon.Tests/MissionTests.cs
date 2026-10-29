using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class MissionTests
{
    [Fact]
    public void GameStartsFromTheShippedOpeningSceneAndItsMissions()
    {
        string path = WorldLoader.OpeningScenePath()!;
        Assert.True(File.Exists(path));
        using var world = WorldLoader.CreateStart(path, enableRendering: false);
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OpeningReference.json")))!;
        var actual = OpeningSceneReference.Fingerprints(world);
        foreach (var field in new[] { "Terrain", "Ecology", "Graphics" }) Assert.Equal(expected[field], actual[field]);
        Assert.NotEmpty(world.Missions);
        Assert.All(world.MissionStatus(), m => { Assert.Null(m.Error); Assert.Equal(MissionState.Active, m.State); });
    }

    [Fact]
    public void MissionsRoundTripAndEvaluateAgainstTheWorld()
    {
        var project = GameWorld.DefaultRuleProject();
        project.World = GameWorld.DefaultScenario(11, 16);
        project.World.Missions.Add(new MissionGoal { Id = "fak", Title = "Fák", Metric = "world.treeCount", Target = 1 });
        project.World.Missions.Add(new MissionGoal { Id = "kevés", Title = "Kevés", Metric = "world.treeCount", Comparison = "<=", Target = 0 });
        project.World.Missions.Add(new MissionGoal { Id = "ár", Title = "Ár", Metric = "world.deliveredTimber", Target = 1, DeadlineYear = 0.0001 });
        project.World.Missions.Add(new MissionGoal { Id = "nincs", Title = "Nincs", Metric = "stock.nincs", Target = 1 });
        var copy = RuleProject.FromJson(project.ToJson());
        Assert.Equal(4, copy.World.Missions.Count);
        using var world = WorldLoader.CreateProject(copy);
        for (int i = 0; i < 120; i++) world.Update(1d / 30);
        var status = world.MissionStatus().ToDictionary(m => m.Goal.Id);
        Assert.Equal(MissionState.Reached, status["fak"].State);
        Assert.Equal(MissionState.Active, status["kevés"].State);
        Assert.Equal(MissionState.Failed, status["ár"].State);
        Assert.NotNull(status["nincs"].Error);
    }

    [Fact]
    public void InvalidMissionsAreRejected()
    {
        var scenario = GameWorld.DefaultScenario(11, 16);
        scenario.Missions.Add(new MissionGoal { Id = "a", Title = "A", Metric = "balance" });
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.Missions[0].Metric = "world.balance";
        scenario.Missions.Add(new MissionGoal { Id = "a", Title = "B" });
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }
}

[Collection("SpeciesBook")]
public sealed class SceneRulesTests
{
    [Fact]
    public void SceneTuningRunsAndTheLocalLinkOnlyTakesLaterPublications()
    {
        var scene = GameWorld.DefaultRuleProject(); scene.Algorithms = null;
        scene.World = GameWorld.DefaultScenario(11, 16);
        scene.World.Springs[0] = 2;
        scene.World.Species[0].TimberPrice = 777;
        var species = Enum.Parse<ForestSpecies>(scene.World.Species[0].Id);
        scene.Tuning["DieselPrice"] = 1.5;
        using var world = WorldLoader.CreateProject(scene);
        Assert.Equal(1.5, world.Tuning[Tune.DieselPrice]);

        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-scene-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try {
            string file = Path.Combine(directory, "project.json");
            var local = GameWorld.DefaultRuleProject(); local.Algorithms = null; local.Name = "helyi";
            RuleProject.WriteAtomic(file, local.ToJson());
            var link = new RuleProjectLink(file, adoptRunning: true);
            link.Poll(world, 1); Assert.Equal(0, world.ExecutePendingCommands());
            Assert.Equal(1.5, world.Tuning[Tune.DieselPrice]);
            Assert.NotEmpty(world.RuleProjectDocument.World.SoilTypes);

            local.Name = "később közzétett"; local.Tuning["DieselPrice"] = 2;
            RuleProject.WriteAtomic(file, local.ToJson());
            link.Poll(world, 1); world.ExecutePendingCommands();
            Assert.Equal(2, world.Tuning[Tune.DieselPrice]);
            // A rules-only publication keeps the running scene's map and missions.
            Assert.NotNull(world.RuleProjectDocument.World);
            Assert.Equal(2, world.Environment.Springs[0]);
            Assert.Equal(777, world.WithSpecies(() => ForestSpeciesBook.TimberPrice(species)));
        } finally { Directory.Delete(directory, true); }
    }
}
