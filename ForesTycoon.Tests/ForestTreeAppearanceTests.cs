using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class ForestTreeAppearanceTests
{
    public static IEnumerable<object[]> Species => new[] { ForestSpecies.Spruce, ForestSpecies.Oak, ForestSpecies.Birch, ForestSpecies.Beech }
        .Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Species))]
    internal void EveryStageHasThreeDistinctVariantsAndAgeChangesStructureAtEqualSize(ForestSpecies species)
    {
        var models = new List<Vector3[]>();
        foreach (TreeLifeStage stage in Enum.GetValues<TreeLifeStage>())
            for (int variant = 0; variant < 3; variant++)
            {
                var vertices = new List<Vertex>();
                Assert.Equal(variant, ForestTreeAppearance.Variant(42 + variant));
                ForestCrownMesh.Append(vertices, species, Vector3.Zero, 2, 5, 0, 42 + variant, Color.Green, ForestLod.Near, stage);
                Vector3[] positions = vertices.Select(v => v.Position).ToArray();
                Assert.DoesNotContain(models, previous => previous.SequenceEqual(positions));
                models.Add(positions);
                Assert.Contains(positions, p => Math.Abs(p.Z - 5) < 0.0001f);
            }
        Assert.Equal(12, models.Count);
    }

    [Theory]
    [MemberData(nameof(Species))]
    internal void StageBoundariesAreOrderedAndSelectTheNextPhaseInclusively(ForestSpecies species)
    {
        float last = 0;
        foreach (TreeLifeStage stage in Enum.GetValues<TreeLifeStage>())
        {
            Assert.Equal(stage, ForestTreeAppearance.Stage(species, last));
            float next = ForestTreeAppearance.NextStageAge(species, stage);
            if (float.IsPositiveInfinity(next)) break;
            Assert.True(next > last);
            Assert.Equal(stage, ForestTreeAppearance.Stage(species, next - 0.001f));
            last = next;
        }
    }
}
