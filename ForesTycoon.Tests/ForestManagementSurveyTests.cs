namespace ForesTycoon.Tests;

public class ForestManagementSurveyTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 8 * 6;
        public int Seed => 42;
        public (int Columns, int Rows) TileGrid => (8, 6);
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id / 6 * 16, id % 6 * 16, 16, 16);
        public bool CanSupportForest(int id) => id != 47;
        public float GetMoisture(int id) => .55f;
        public float GetNormalizedElevation(int id) => .45f;
        public int GetAdjacentTileIds(int id, Span<int> target)
        {
            int n = 0, column = id / 6, row = id % 6;
            if (column > 0) target[n++] = id - 6;
            if (column < 7) target[n++] = id + 6;
            if (row > 0) target[n++] = id - 1;
            if (row < 5) target[n++] = id + 1;
            return n;
        }
    }

    private static ForestTileSurvey Stand(float health = .9f, float water = .7f, float drought = 0, float waterlogging = 0,
        float dead = 0, float crowded = 0, float maturity = .5f, ForestLimit limit = ForestLimit.None) =>
        new(ForestSpecies.Oak, 40, maturity, health, 4, .8f, 120, dead, 0, crowded, limit, water, drought, waterlogging, .8f, true, false,
            ManagementIssue.None, 0);

    private static readonly ManagementRules Rules = ManagementRules.Default;

    [Fact]
    public void HealthyMidAgedStandNeedsNothing() =>
        Assert.Equal((ManagementIssue.None, (byte)0), ForestManagementSurvey.Diagnose(Stand(), Rules));

    [Fact]
    public void EachRuleFiresOnItsOwnSignal()
    {
        Assert.Equal(ManagementIssue.Drought, ForestManagementSurvey.Diagnose(Stand(water: .1f, limit: ForestLimit.Water), Rules).Issue);
        Assert.Equal(ManagementIssue.Waterlogging, ForestManagementSurvey.Diagnose(Stand(waterlogging: .8f), Rules).Issue);
        Assert.Equal(ManagementIssue.Dieback, ForestManagementSurvey.Diagnose(Stand(dead: .4f), Rules).Issue);
        Assert.Equal(ManagementIssue.Dieback, ForestManagementSurvey.Diagnose(Stand(health: .2f), Rules).Issue);
        Assert.Equal(ManagementIssue.Overstocked, ForestManagementSurvey.Diagnose(Stand(crowded: .6f), Rules).Issue);
        Assert.Equal(ManagementIssue.Harvestable, ForestManagementSurvey.Diagnose(Stand(maturity: 1.2f), Rules).Issue);
        // Dry soil alone, without the trees being water-limited or stressed, is not an emergency.
        Assert.Equal(ManagementIssue.None, ForestManagementSurvey.Diagnose(Stand(water: .1f), Rules).Issue);
        // One dead tree among a handful is ordinary self-thinning, not dieback.
        Assert.Equal(ManagementIssue.None, ForestManagementSurvey.Diagnose(Stand(dead: .17f), Rules).Issue);
    }

    [Fact]
    public void DiebackOutranksOtherIssuesAndSeverityGrows()
    {
        var both = Stand(dead: .35f, waterlogging: .9f, maturity: 1.5f);
        Assert.Equal(ManagementIssue.Dieback, ForestManagementSurvey.Diagnose(both, Rules).Issue);
        Assert.Equal(1, ForestManagementSurvey.Diagnose(Stand(dead: .32f), Rules).Severity);
        Assert.Equal(3, ForestManagementSurvey.Diagnose(Stand(dead: .65f), Rules).Severity);
    }

    [Fact]
    public void OnlyFertileForestableClearingsAreRegenerated()
    {
        var clearing = new ForestTileSurvey(ForestSpecies.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, ForestLimit.None, .7f, 0, 0, .8f, true, true,
            ManagementIssue.None, 0);
        Assert.Equal(ManagementIssue.Regenerate, ForestManagementSurvey.Diagnose(clearing, Rules).Issue);
        Assert.Equal(ManagementIssue.None, ForestManagementSurvey.Diagnose(clearing with { Cleared = false }, Rules).Issue);
        Assert.Equal(ManagementIssue.None, ForestManagementSurvey.Diagnose(clearing with { Forestable = false }, Rules).Issue);
        Assert.Equal(ManagementIssue.None, ForestManagementSurvey.Diagnose(clearing with { Fertility = .2f }, Rules).Issue);
    }

    [Fact]
    public void SurveyReadsTheLiveWorldWithoutChangingIt()
    {
        var world = new Ecosystem(new Habitat(), 120, SoilLandscapeDefinition.Default);
        world.Forest.Clear();
        Assert.Equal(ForestryActionResult.Planted, world.Forest.Plant(14, ForestSpecies.Oak));
        world.Update(3);
        ulong revision = world.Forest.Revision;
        int trees = world.Forest.IndividualTreeCount;

        var survey = ForestManagementSurvey.Build(world.Forest, world.Environment, world.Soils);

        Assert.Equal(48, survey.Length);
        Assert.Equal(revision, world.Forest.Revision);
        Assert.Equal(trees, world.Forest.IndividualTreeCount);
        Assert.True(survey[14].IsForest);
        Assert.Equal(ForestSpecies.Oak, survey[14].Species);
        Assert.Equal(trees, survey[14].Trees);
        Assert.InRange(survey[14].Health, 0, 1);
        Assert.InRange(survey[14].WaterAvailability, 0, 1);
        Assert.Equal(survey, ForestManagementSurvey.Build(world.Forest, world.Environment, world.Soils));
        // Untouched open land is not flagged; a felled stand leaves a clearing to regenerate.
        Assert.DoesNotContain(survey, s => s.Issue == ManagementIssue.Regenerate);
        Assert.False(survey[47].Forestable);
        Assert.Equal(ForestryActionResult.Harvested, world.Forest.Harvest(14, out _));
        var felled = ForestManagementSurvey.Build(world.Forest, world.Environment, world.Soils)[14];
        Assert.False(felled.IsForest);
        Assert.True(felled.Cleared);
        Assert.Equal(ManagementIssue.Regenerate, felled.Issue);
    }

    [Fact]
    public void RecommendationsAreRankedPlayableTrees()
    {
        var ranked = ForestManagementSurvey.Recommend(new Habitat(), 3);
        Assert.Equal(3, ranked.Length);
        Assert.All(ranked, r => Assert.False(ForestSpeciesTraits.For(r.Species).Shrub));
        Assert.True(ranked[0].Suitability >= ranked[1].Suitability && ranked[1].Suitability >= ranked[2].Suitability);
    }
}
