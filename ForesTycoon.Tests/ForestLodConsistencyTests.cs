using OpenTK.Mathematics;
using Xunit.Abstractions;

namespace ForesTycoon.Tests;

public class ForestLodConsistencyTests
{
    private readonly ITestOutputHelper output;
    public ForestLodConsistencyTests(ITestOutputHelper output) => this.output = output;

    private static readonly ForestSpecies[] Species =
        { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech, ForestSpecies.Maple, ForestSpecies.Pine };
    public static IEnumerable<object[]> Each => Species.Select(s => new object[] { s });

    private static TreeShapeSpec Spec(ForestSpecies species, LeafState leaves)
    {
        var profile = ForestSpeciesProfile.For(species);
        return new(species, 42, TreeLifePhase.Mature, ForestTreeGrowth.Initial(species, profile.MatureAgeYears * 1.5f, 1), 1,
            new TreeSite(0.85f), leaves, 0.3f);
    }

    /// <summary>Filled area of the projection of the triangles onto a plane (rasterised, in world units²).</summary>
    private static (double Area, double Width, double Top) Silhouette(IEnumerable<Vertex[]> parts, bool side)
    {
        const int N = 160; const float Extent = 12;
        var grid = new bool[N, N];
        foreach (var part in parts)
            for (int i = 0; i + 2 < part.Length; i += 3)
            {
                Vector2 P(Vertex v) => side ? new(v.Position.X, v.Position.Z) : new(v.Position.X, v.Position.Y);
                Vector2 a = P(part[i]), b = P(part[i + 1]), c = P(part[i + 2]);
                float minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
                float minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                for (int gx = Math.Max(0, (int)((minX + Extent / 2) / Extent * N)); gx <= Math.Min(N - 1, (int)((maxX + Extent / 2) / Extent * N)); gx++)
                    for (int gy = Math.Max(0, (int)(minY / Extent * N)); gy <= Math.Min(N - 1, (int)(maxY / Extent * N)); gy++)
                    {
                        Vector2 p = new((gx + .5f) / N * Extent - Extent / 2, (gy + .5f) / N * Extent);
                        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
                        if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) grid[gx, gy] = true;
                    }
            }
        double cell = Extent / N, area = 0; int minGx = N, maxGx = -1, top = 0;
        for (int x = 0; x < N; x++) for (int y = 0; y < N; y++) if (grid[x, y]) { area += cell * cell; minGx = Math.Min(minGx, x); maxGx = Math.Max(maxGx, x); top = Math.Max(top, y); }
        return (area, (maxGx - minGx + 1) * cell, (top + 1) * cell);
        static float Sign(Vector2 p, Vector2 a, Vector2 b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
    }

    [Theory]
    [MemberData(nameof(Each))]
    internal void EveryLodFillsTheSameSilhouette(ForestSpecies species)
    {
        var spec = Spec(species, LeafState.Full);
        var results = new Dictionary<ForestLod, (double Area, double Width, double Top)>();
        foreach (var lod in Enum.GetValues<ForestLod>())
        {
            var mesh = DendroTreeGenerator.Generate(spec, lod);
            results[lod] = Silhouette(new[] { mesh.Trunk, mesh.Branches, mesh.Crown }, side: true);
            output.WriteLine($"{species} {lod}: area {results[lod].Area:F2} width {results[lod].Width:F2} top {results[lod].Top:F2}");
        }
        var near = results[ForestLod.Near];
        foreach (var lod in new[] { ForestLod.Medium, ForestLod.Far })
        {
            Assert.InRange(results[lod].Area / near.Area, 0.88, 1.12);
            Assert.InRange(results[lod].Width / near.Width, 0.85, 1.2);
            Assert.InRange(results[lod].Top / near.Top, 0.95, 1.05);
        }
    }

    [Theory]
    [MemberData(nameof(Each))]
    internal void EveryLodCarriesTheSameSeasonColour(ForestSpecies species)
    {
        foreach (var leaves in new[] { LeafState.Budding, LeafState.Full, LeafState.Autumn, LeafState.Falling })
        {
            var spec = Spec(species, leaves);
            // Vertex occlusion varies over the surface. Compare area-weighted colour,
            // not the list of samples (different LODs place vertices differently).
            var colours = Enum.GetValues<ForestLod>()
                .Select(lod => Average(DendroTreeGenerator.Generate(spec, lod).Crown)).ToArray();
            foreach (var c in colours)
                Assert.InRange((c - colours[0]).Length, 0, 0.04f);
        }

        static Vector3 Average(Vertex[] mesh)
        {
            Vector3 sum = Vector3.Zero; float area = 0;
            for (int i = 0; i < mesh.Length; i += 3)
            {
                float weight = Vector3.Cross(mesh[i + 1].Position - mesh[i].Position,
                    mesh[i + 2].Position - mesh[i].Position).Length;
                for (int j = 0; j < 3; j++)
                {
                    uint c = mesh[i + j].Color;
                    sum += new Vector3(c & 255, (c >> 8) & 255, (c >> 16) & 255) * (weight / (3 * 255));
                }
                area += weight;
            }
            return sum / area;
        }
    }
}
