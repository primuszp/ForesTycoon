using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class StumpRuleExampleTests
{
    private static RuleProject Project()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var project = world.RuleProjectDocument;
        var rule = project.Rules.Single(r => r.Id == "forest.stumps");
        Assert.Equal("add", rule.Effects[0].Mode);
        Assert.True(rule.Effects[0].Influence.PerYear);
        Assert.Equal(new[] { "tile.moisture", "diameter" }, rule.Effects[0].Influence.Terms.Single().Factors.Select(f => f.Source));
        return RuleProject.FromJson(project.ToJson());
    }

    [Fact]
    public void GameOpeningAndDefaultSaveBothUseMoistureDiameterRule()
    {
        using var opening = WorldLoader.CreateStart(enableRendering: false);
        var rule = opening.RuleProjectDocument.Rules.Single(r => r.Id == "forest.stumps");
        Assert.Equal("add", rule.Effects[0].Mode);
        var factors = rule.Effects[0].Influence.Terms.Single().Factors;
        Assert.Equal(1, factors.Single(f => f.Source == "tile.moisture").ToCurve().Sign);
        Assert.Equal(-1, factors.Single(f => f.Source == "diameter").ToCurve().Sign);
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        Assert.NotNull(world.CaptureCheckpoint().RuleBook); // Even an unedited book belongs to this saved world.
        using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        restored.Load(stream);
        var loaded = restored.RuleProjectDocument.Rules.Single(r => r.Id == "forest.stumps");
        Assert.Equal("add", loaded.Effects[0].Mode);
        Assert.Equal(new[] { "tile.moisture", "diameter" }, loaded.Effects[0].Influence.Terms.Single().Factors.Select(f => f.Source));
    }

    [Fact]
    public void FourRealStumpsConfirmMoistureAcceleratesDiameterSlowsAndCompleteDecayRemoves()
    {
        var result = StumpRuleTrial.Run(Project(), "forest.stumps");
        Assert.Equal(4, result.Cases.Length);
        var drySmall = result.Cases[0]; var wetSmall = result.Cases[1];
        var dryLarge = result.Cases[2]; var wetLarge = result.Cases[3];
        Assert.Equal(.175, drySmall.OneYearProgress, 6);
        Assert.Equal(.325, wetSmall.OneYearProgress, 6);
        Assert.Equal(.7 / 12, dryLarge.OneYearProgress, 6);
        Assert.Equal(1.3 / 12, wetLarge.OneYearProgress, 6);
        Assert.True(wetSmall.RemovedAtYears < drySmall.RemovedAtYears);
        Assert.True(wetLarge.RemovedAtYears < dryLarge.RemovedAtYears);
        Assert.True(drySmall.RemovedAtYears < dryLarge.RemovedAtYears);
        Assert.True(wetSmall.RemovedAtYears < wetLarge.RemovedAtYears);
        foreach (var row in result.Cases) {
            Assert.NotNull(row.RemovedAtYears);
            Assert.InRange(row.RemovedAtYears!.Value, 1 / row.OneYearProgress - 1e-5,
                1 / row.OneYearProgress + result.StepYears + 1e-5);
        }
    }

    [Fact]
    public void NormalGameUpdateRunsTheExampleAndRemovesCompletedStumps()
    {
        using var world = StumpRuleTrial.CreateWorld(Project(), "forest.stumps");
        var scope = RuleEngine.Scope("stump"); var ids = scope.Elements(world);
        var progress = scope.Fields.Single(f => f.Name == "decayProgress");
        double secondsPerYear = world.CaptureCheckpoint().Ecology.ForestYearSeconds;
        var expected = ids.Select(id => world.RuleBook.PreviewInfluences(world, "forest.stumps", id, .5 / secondsPerYear)[0].After).ToArray();
        world.Update(.5);
        Assert.Null(world.RuleBook.LastError);
        for (int i = 0; i < ids.Length; i++) Assert.Equal(expected[i], progress.Read(world, ids[i]), 7);
        foreach (int id in ids) progress.Write(world, id, .99999);
        world.Update(.5);
        Assert.Empty(scope.Elements(world));
    }

    [Fact]
    public void TrialUsesEditedRuleAndDoesNotRemoveStumpsWhenRemovalConnectionIsDisabled()
    {
        var project = Project(); var rule = project.Rules.Single(r => r.Id == "forest.stumps");
        rule.Effects[1].Influence.Terms[0].Enabled = false;
        var result = StumpRuleTrial.Run(project, rule.Id);
        Assert.All(result.Cases, c => Assert.Null(c.RemovedAtYears));
        rule.Effects[0].Influence.Terms[0].Amount *= 2;
        var faster = StumpRuleTrial.Run(project, rule.Id);
        for (int i = 0; i < result.Cases.Length; i++)
            Assert.Equal(result.Cases[i].OneYearProgress * 2, faster.Cases[i].OneYearProgress, 6);
    }
}
