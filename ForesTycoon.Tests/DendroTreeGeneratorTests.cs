using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class DendroTreeGeneratorTests
{
    public static IEnumerable<object[]> Cases => Enum.GetValues<ForestSpecies>().Where(s => s != ForestSpecies.None)
        .SelectMany(s => Enum.GetValues<TreeLifeStage>().SelectMany(stage => new[] { 0, 42, -1, int.MinValue }
            .Select(seed => new object[] { s, stage, seed })));

    [Theory]
    [MemberData(nameof(Cases))]
    internal void GeneratedTreeHasOneConnectedManifoldCrownAndFiniteNondegenerateWood(
        ForestSpecies species, TreeLifeStage stage, int seed)
    {
        var size = ForestTreeGrowth.Initial(species, stage == TreeLifeStage.Seedling ? 0.1f : 40, 1);
        var mesh = DendroTreeGenerator.Generate(species, seed, stage, 0.8f, size, 0.7f, ForestLod.Near);
        Assert.True(mesh.StemCount > 1); Assert.True(mesh.LeafCount > 0);
        Assert.NotEmpty(mesh.Trunk); Assert.NotEmpty(mesh.Branches);
        Assert.InRange(mesh.Crown.Length, 1, 360);
        Assert.InRange(mesh.Trunk.Length + mesh.Branches.Length, 1, 228);
        var edges = new Dictionary<(Vector3, Vector3), int>();
        var neighbours = new Dictionary<Vector3, HashSet<Vector3>>();
        for (int i = 0; i < mesh.Crown.Length; i += 3)
        {
            Vector3 a = mesh.Crown[i].Position, b = mesh.Crown[i + 1].Position, c = mesh.Crown[i + 2].Position;
            Vector3 face = Vector3.Cross(b - a, c - a);
            Assert.True(face.LengthSquared > 1e-14f);
            Assert.True(Vector3.Dot(face, mesh.Crown[i].Normal + mesh.Crown[i + 1].Normal + mesh.Crown[i + 2].Normal) > 0);
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        Assert.All(edges.Values, count => Assert.Equal(2, count));
        var reached = new HashSet<Vector3>(); var work = new Stack<Vector3>();
        work.Push(neighbours.Keys.First());
        while (work.TryPop(out var p)) if (reached.Add(p)) foreach (var n in neighbours[p]) work.Push(n);
        Assert.Equal(neighbours.Count, reached.Count);
        Assert.Equal(size.Height * Terrain.TreeMetresToWorld, mesh.Crown.Max(v => v.Position.Z), 5);
        foreach (var vertices in new[] { mesh.Trunk, mesh.Branches, mesh.Crown })
        {
            Assert.All(vertices, v => {
                Assert.True(float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z));
                Assert.Equal(1, v.Normal.Length, 4);
                Assert.Equal((uint)Terrain.SurfaceSpeciesCode(species), v.Color >> 24);
            });
            for (int i = 0; i < vertices.Length; i += 3)
                Assert.True(Vector3.Cross(vertices[i+1].Position - vertices[i].Position, vertices[i+2].Position - vertices[i].Position).LengthSquared > 0);
        }
        void Edge(Vector3 a, Vector3 b)
        {
            if (!neighbours.TryGetValue(a, out var ns)) neighbours.Add(a, ns = new()); ns.Add(b);
            if (!neighbours.TryGetValue(b, out ns)) neighbours.Add(b, ns = new()); ns.Add(a);
            if (a.X > b.X || (a.X == b.X && (a.Y > b.Y || (a.Y == b.Y && a.Z > b.Z)))) (a, b) = (b, a);
            edges[(a, b)] = edges.GetValueOrDefault((a, b)) + 1;
        }
    }

    [Theory]
    [InlineData(ForestSpecies.Spruce)]
    [InlineData(ForestSpecies.Birch)]
    [InlineData(ForestSpecies.Oak)]
    [InlineData(ForestSpecies.Beech)]
    internal void SeedAgeAndLightChangeStructureAtEqualPhysicalSizeAndReplayIsExact(ForestSpecies species)
    {
        var size = ForestTreeGrowth.Initial(species, 40, 1);
        DendroTreeGenerator.Mesh Build(int seed, TreeLifeStage stage, float light, ForestLod lod = ForestLod.Near) =>
            DendroTreeGenerator.Generate(species, seed, stage, light, size, 0, lod);
        var full = Build(42, TreeLifeStage.Mature, 1);
        Assert.Equal(full.Trunk, Build(42, TreeLifeStage.Mature, 1).Trunk);
        Assert.Equal(full.Branches, Build(42, TreeLifeStage.Mature, 1).Branches);
        Assert.Equal(full.Crown, Build(42, TreeLifeStage.Mature, 1).Crown);
        Assert.False(full.Crown.SequenceEqual(Build(43, TreeLifeStage.Mature, 1).Crown));
        Assert.False(full.Crown.SequenceEqual(Build(42, TreeLifeStage.Young, 1).Crown));
        Assert.False(full.Crown.SequenceEqual(Build(42, TreeLifeStage.Old, 1).Crown));
        var shade = Build(42, TreeLifeStage.Mature, 0.1f);
        Assert.True(shade.StemCount < full.StemCount);
        Assert.True(shade.Crown.Max(v => v.Position.Xy.Length) < full.Crown.Max(v => v.Position.Xy.Length));
        Assert.True(shade.Crown.Min(v => v.Position.Z) > full.Crown.Min(v => v.Position.Z));
        var medium = Build(42, TreeLifeStage.Mature, 1, ForestLod.Medium);
        var far = Build(42, TreeLifeStage.Mature, 1, ForestLod.Far);
        Assert.True(far.Crown.Length < medium.Crown.Length && medium.Crown.Length < full.Crown.Length);
        Assert.InRange((medium.Trunk.Length + medium.Branches.Length + medium.Crown.Length) / 3, 1, 98);
        Assert.Equal(20, far.Crown.Length / 3);
        Assert.Empty(far.Trunk); Assert.Empty(far.Branches);
    }

    [Fact]
    public void GpuStatePublishesContinuousLightWithoutChangingGrowthScales()
    {
        var size = new ForestTreeDimensions(0.3f, 18, 4);
        var tree = new ForestTree(1, 0, ForestSpecies.Oak, 0.5f, 0.5f, 42, -30, 0,
            size, new(0.01f, 0.2f, 0.05f), 0.8f, new(1, 1, 1));
        var sunny = ForestTreeRenderState.Create(tree, size, 0.2);
        var shaded = ForestTreeRenderState.Create(tree with { Resources = new(0.12f, 1, 1) }, size, 0.2);
        Assert.Equal(sunny.Scale, shaded.Scale);
        Assert.Equal(sunny.Rate.Xyz, shaded.Rate.Xyz);
        Assert.Equal(1, sunny.Rate.W); Assert.Equal(0.12f, shaded.Rate.W);
    }

    [Fact]
    public void CrownFollowsLeafPositionsWithoutChangingTopologyBudget()
    {
        Vertex[] Build(Vector3 leaf) => DendroCrownMesh.Build(ForestSpecies.Oak, 42,
            TreeLifeStage.Mature, 6, 2, 0.7f, 0, new[] { leaf }, 0xffffffff, ForestLod.Near);
        var east = Build(new(1.5f, 0, 4));
        var west = Build(new(-1.5f, 0, 4));
        Assert.Equal(360, east.Length);
        Assert.Equal(east.Length, west.Length);
        Assert.False(east.SequenceEqual(west));
        Assert.True(east.Max(v => v.Position.X) > west.Max(v => v.Position.X));
    }

    [Fact]
    public void ShrubHasLeafDrivenLowCrownAndBoundedGeometry()
    {
        var size = new ForestTreeDimensions(0.06f, 1.8f, 1.4f);
        var mesh = DendroTreeGenerator.GenerateShrub(42, TreeLifeStage.Mature, 1, size, 0, ForestLod.Near);
        Assert.True(mesh.LeafCount > 0);
        Assert.True(mesh.StemCount > 3);
        Assert.NotEmpty(mesh.Trunk);
        Assert.True(mesh.Crown.Min(v => v.Position.Z) < size.Height * Terrain.TreeMetresToWorld * 0.1f);
        Assert.True(mesh.Crown.Max(v => v.Position.Xy.Length) * 2 > mesh.Crown.Max(v => v.Position.Z));
        Assert.InRange((mesh.Trunk.Length + mesh.Branches.Length + mesh.Crown.Length) / 3, 1, 280);
        Assert.Equal(mesh.Crown, DendroTreeGenerator.GenerateShrub(42, TreeLifeStage.Mature, 1, size, 0, ForestLod.Near).Crown);
    }
}
