namespace ForesTycoon.Tests;

public class ForestGrowthShapeTests
{
    [Theory]
    [InlineData((int)ForestSpecies.Spruce)]
    [InlineData((int)ForestSpecies.Birch)]
    [InlineData((int)ForestSpecies.Oak)]
    [InlineData((int)ForestSpecies.Beech)]
    public void FactoredCurveExactlyMatchesOriginalArithmetic(int speciesValue)
    {
        var species = (ForestSpecies)speciesValue;
        var random = new Random(351);
        for (int i = 0; i < 500; i++)
        {
            double year = i / 12.0;
            var tree = new ForestTree(1, 0, species, .5f, .5f, 42, -40, year - .02,
                new((float)random.NextDouble(), (float)random.NextDouble() * 50, 3),
                new(.01f, .2f, .1f), (float)random.NextDouble());
            float fitness = (float)random.NextDouble();
            var resources = new ForestResources((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            Assert.Equal(Original(tree, year, fitness, resources), ForestTreeGrowth.RatesWithResources(tree, year, fitness, resources));
        }
    }

    // Frozen pre-refactor formula: catches reordered floating-point operations even when
    // both the synchronous and prepared production paths share the new growth helper.
    private static ForestTreeDimensions Original(ForestTree tree, double year, float fitness, ForestResources resources)
    {
        var size = tree.At(year);
        float season = Math.Clamp(.65f + .8f * MathF.Cos(MathF.Tau * ((float)(year % 1) - .25f)), 0, 1.45f);
        float factor = Math.Clamp(fitness, 0, 1) * resources.LightResponse(tree.Species)
            * Math.Clamp(resources.Water, 0, 1) * MathF.Sqrt(Math.Clamp(resources.Space, 0, 1)) * tree.Health * season;
        float maxHeight = tree.Species switch { ForestSpecies.Spruce => 40, ForestSpecies.Birch => 28, ForestSpecies.Oak => 35, _ => 38 };
        float radial = tree.Species == ForestSpecies.Birch ? .0055f : tree.Species == ForestSpecies.Oak ? .0045f : .005f;
        float diameter = radial * 2 * factor / (1 + size.Diameter * .8f);
        float height = .9f * Math.Max(0, 1 - size.Height / maxHeight) * factor;
        float crownRatio = tree.Species switch { ForestSpecies.Oak => .28f, ForestSpecies.Spruce => .26f, ForestSpecies.Birch => .16f, _ => .22f };
        return new(diameter, height, Math.Max(height * crownRatio, diameter * (tree.Species == ForestSpecies.Oak ? 9 : 6)));
    }
}
