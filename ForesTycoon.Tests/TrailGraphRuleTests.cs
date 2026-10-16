using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class TrailGraphRuleTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static int[] Trails(GameWorld world)
    {
        int start = Enumerable.Range(0, world.Map.Tiles.Count - 2).First(t => world.Map.CanCarrySkidTrail(t) && world.Map.CanCarrySkidTrail(t + 1) && world.Map.CanCarrySkidTrail(t + 2));
        world.QueueSkidTrailPath(start, start + 2, false); world.ExecutePendingCommands();
        return world.Map.SkidTrailTiles.OrderBy(id => id).ToArray();
    }

    [Fact]
    public void DefaultIsDiscoveredAndRemovalNeedsBothEditableConditions()
    {
        using var world = World(); int id = Trails(world)[0];
        var schema = GameWorld.RuleObjects.Find("trail");
        Assert.Equal(new[] { "wear", "idleYears" }, schema.Fields.Where(f => f.Writable).Select(f => f.Name));
        Assert.Contains(RuleEngine.Scope("trail").Fields, f => f.Name == "tile.moisture");
        var rule = world.RuleProjectDocument.Rules.Single(r => r.Id == "trail.age");
        Assert.All(rule.Effects, e => { Assert.NotNull(e.Influence); Assert.Equal("", e.Formula); });
        Assert.Equal(2, rule.Effects[2].Influence.Terms.Single().Conditions.Count);
        // Enough idle time alone does not remove a deeply rutted trail.
        world.Map.SetSkidTrailState(id, .8f, 3);
        world.RuleBook.Run(world, .5, 0);
        Assert.True(world.Map.IsSkidTrail(id)); Assert.Equal(.6f, world.Map.GetSkidTrailWear(id), 6);
        // Smooth enough alone does not remove a recently used trail.
        world.Map.SetSkidTrailState(id, 0, 0);
        world.RuleBook.Run(world, 0, 0); Assert.True(world.Map.IsSkidTrail(id));
        // Inclusive threshold, and a non-mutating preview of the same sequential rule.
        world.Map.SetSkidTrailState(id, 0, 3);
        Assert.Equal(1, world.RuleBook.PreviewInfluences(world, rule.Id, id, 0).Last().After);
        Assert.True(world.Map.IsSkidTrail(id));
        Assert.True(world.RuleBook.Run(world, 0, 0)); // route refresh signal is preserved
        Assert.False(world.Map.IsSkidTrail(id));
    }

    [Fact]
    public void DesignerCanChooseMoistureCurveReverseItAndRemoveTheRegeneration()
    {
        using var world = World(); var ids = Trails(world);
        var terrain = world.Map.Capture(); terrain.Moisture[ids[0]] = .2f; terrain.Moisture[ids[1]] = .8f; world.Map.Restore(terrain);
        var project = world.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "trail.age");
        var regeneration = rule.Effects[0].Influence.Terms.Single();
        var factor = new InfluenceFactor { Source = "tile.moisture", OutputLow = 1.5, OutputHigh = .5 };
        regeneration.Factors.Add(factor);
        void ApplyAndRun()
        {
            world.QueueRuleProject(RuleProject.FromJson(project.ToJson())); world.ExecutePendingCommands();
            foreach (int id in ids) world.Map.SetSkidTrailState(id, .8f, 0);
            world.RuleBook.Run(world, .5, 0); Assert.Null(world.RuleBook.LastError);
        }
        ApplyAndRun();
        Assert.Equal(.54, world.Map.GetSkidTrailWear(ids[0]), 6);
        Assert.Equal(.66, world.Map.GetSkidTrailWear(ids[1]), 6);
        factor.OutputLow = .5; factor.OutputHigh = 1.5; ApplyAndRun();
        Assert.Equal(.66, world.Map.GetSkidTrailWear(ids[0]), 6);
        Assert.Equal(.54, world.Map.GetSkidTrailWear(ids[1]), 6);
        rule.Effects[0].Influence.Terms.Clear(); ApplyAndRun();
        Assert.All(ids, id => Assert.Equal(.8f, world.Map.GetSkidTrailWear(id))); // no native fallback
    }

    [Theory]
    [InlineData("less", false)]
    [InlineData("atMost", true)]
    [InlineData("greater", false)]
    [InlineData("atLeast", true)]
    public void VisualComparisonsHonorEquality(string comparison, bool expected)
    {
        using var world = World(); int id = Trails(world)[0];
        var project = world.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "trail.age"); rule.Effects.RemoveRange(0, 2);
        rule.Effects[0].Influence.Terms[0].Conditions = new() { new() { Field = "wear", Comparison = comparison, Value = .5 } };
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Map.SetSkidTrailState(id, .5f, 0);
        world.RuleBook.Run(world, 0, 0); Assert.Equal(!expected, world.Map.IsSkidTrail(id));
    }

    [Fact]
    public void LinkedOvergrowthTimeAndEditedConditionsSurviveSaveAndMachinePassResetsIdleTime()
    {
        using var world = World(); int id = Trails(world)[0];
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Tuning["SkidTrailOvergrowYears"] = 10;
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Map.SetSkidTrailState(id, 0, 4);
        world.RuleBook.Run(world, 0, 0); Assert.True(world.Map.IsSkidTrail(id));
        world.Map.DriveSkidTrail(id, .2f); Assert.Equal(0, world.Map.GetSkidTrailIdle(id));
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        using var restored = World(); restored.Load(saved);
        Assert.Equal(JsonSerializer.Serialize(world.RuleProjectDocument.Rules), JsonSerializer.Serialize(restored.RuleProjectDocument.Rules));
        restored.Map.SetSkidTrailState(id, 0, 9); restored.RuleBook.Run(restored, 0, 0); Assert.True(restored.Map.IsSkidTrail(id));
        restored.Map.SetSkidTrailState(id, 0, 10); restored.Update(.5); Assert.False(restored.Map.IsSkidTrail(id));
    }

}
