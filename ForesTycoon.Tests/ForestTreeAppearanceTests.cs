namespace ForesTycoon.Tests;

public class ForestTreeAppearanceTests
{
    public static IEnumerable<object[]> Species => new[] { ForestSpecies.Spruce, ForestSpecies.Oak, ForestSpecies.Birch, ForestSpecies.Beech }
        .Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Species))]
    internal void StageBoundariesAreOrderedAndSelectTheNextPhaseInclusively(ForestSpecies species)
    {
        float last = 0;
        foreach (TreeLifeStage stage in Enum.GetValues<TreeLifeStage>())
        {
            Assert.Equal(stage, TreeLifeStages.Of(species, last));
            float next = TreeLifeStages.NextAge(species, stage);
            if (float.IsPositiveInfinity(next)) break;
            Assert.True(next > last);
            Assert.Equal(stage, TreeLifeStages.Of(species, next - 0.001f));
            last = next;
        }
    }
}
