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

public class ArbaroPresetTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "ThirdParty", "ArbaroPresets"))) return dir.FullName;
        throw new DirectoryNotFoundException("ThirdParty/ArbaroPresets");
    }

    public static IEnumerable<object[]> Files()
    {
        string root = Root();
        foreach (string file in Directory.GetFiles(Path.Combine(root, "ThirdParty", "ArbaroPresets"), "*.xml").Concat(
            Directory.GetFiles(Path.Combine(root, "ForesTycoon", "Assets", "Trees"), "*.xml")))
            yield return new object[] { Path.GetRelativePath(root, file) };
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryArbaroParameterFileBuildsAValidSkeleton(string relative)
    {
        string xml = File.ReadAllText(Path.Combine(Root(), relative));
        foreach (int variant in new[] { 1, 2 })
        {
            var skeleton = TreeArchitecture.SkeletonFromXml(xml, variant);
            Assert.Empty(skeleton.Validate());
            Assert.InRange(skeleton.StemCount, 8, 700);
            Assert.True(skeleton.Leaves.Length > 10);
        }
    }

    [Theory]
    [InlineData(ForestSpecies.Spruce)]
    [InlineData(ForestSpecies.Oak)]
    [InlineData(ForestSpecies.Birch)]
    [InlineData(ForestSpecies.Beech)]
    internal void ShippedSpeciesSetsMatchTheEmbeddedResources(ForestSpecies species)
    {
        string file = Path.Combine(Root(), "ForesTycoon", "Assets", "Trees", TreeArchitecture.PresetName(species) + ".xml");
        using var stream = typeof(TreeArchitecture).Assembly.GetManifestResourceStream("Trees." + TreeArchitecture.PresetName(species) + ".xml");
        Assert.NotNull(stream);
        Assert.Equal(File.ReadAllText(file).Replace("\r\n", "\n"), new StreamReader(stream).ReadToEnd().Replace("\r\n", "\n"));
    }
}
