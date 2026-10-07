using OpenTK.Mathematics;
using Xunit.Abstractions;

namespace ForesTycoon.Tests;

public class TreeSkeletonTests
{
    private readonly ITestOutputHelper output;
    public TreeSkeletonTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> Architectures =>
        new[] { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech }
            .SelectMany(s => Enum.GetValues<TreeLifePhase>().SelectMany(p => new[] { 0, 1, 2 }
                .Select(light => new object[] { s, p, light })));

    [Theory]
    [MemberData(nameof(Architectures))]
    internal void SkeletonIsAValidRootedTreeWithPipeModelRadii(ForestSpecies species, TreeLifePhase phase, int light)
    {
        foreach (int seed in new[] { 0, 1, 7, 31, 42, -1 })
        {
            var skeleton = TreeArchitecture.Skeleton(species, seed, phase, light, 0);
            Assert.Empty(skeleton.Validate());
            Assert.True(skeleton.StemCount > (phase == TreeLifePhase.Seedling ? 3 : 10), $"{species} {phase}: {skeleton.StemCount} stems");
            Assert.True(skeleton.Leaves.Length > 0);
            Assert.True(skeleton.Height > 0 && skeleton.LeafRadius > 0);
            Assert.All(skeleton.Leaves, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z)));
        }
    }

    [Fact]
    public void PrintSkeletonStatistics()
    {
        foreach (var species in new[] { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech })
            foreach (var phase in Enum.GetValues<TreeLifePhase>())
            {
                var sk = TreeArchitecture.Skeleton(species, 42, phase, 2, 0);
                int[] perLevel = new int[4];
                foreach (var s in sk.Stems) perLevel[Math.Min(3, s.Level)]++;
                output.WriteLine($"{species,-7}{phase,-10} stems L0/L1/L2 = {perLevel[0]}/{perLevel[1]}/{perLevel[2]} vertices {sk.Points.Length} leaves {sk.LeafTotal} height {sk.Height:0.00} leafR {sk.LeafRadius:0.00} flow(base) {sk.Flow[0]:0}");
            }
    }
}
