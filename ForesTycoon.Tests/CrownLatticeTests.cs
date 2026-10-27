using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public sealed class CrownLatticeTests
{
    private static int[] Brute(Vector3[] d)
    {
        var faces = new List<int>();
        for (int a = 0; a < d.Length; a++)
            for (int b = a + 1; b < d.Length; b++)
                for (int c = b + 1; c < d.Length; c++)
                {
                    var n = Vector3.Cross(d[b] - d[a], d[c] - d[a]);
                    if (Vector3.Dot(n, d[a]) < 0) n = -n;
                    bool hull = true;
                    for (int k = 0; k < d.Length && hull; k++)
                        if (k != a && k != b && k != c && Vector3.Dot(n, d[k] - d[a]) > 1e-7f) hull = false;
                    if (!hull) continue;
                    if (Vector3.Dot(Vector3.Cross(d[b] - d[a], d[c] - d[a]), d[a]) >= 0) faces.AddRange(new[] { a, b, c });
                    else faces.AddRange(new[] { a, c, b });
                }
        return faces.ToArray();
    }

    private static string Faces(int[] f) => string.Join(";", Enumerable.Range(0, f.Length / 3)
        .Select(i => new[] { f[3 * i], f[3 * i + 1], f[3 * i + 2] })
        .Select(t => { int m = Array.IndexOf(t, t.Min()); return $"{t[m]},{t[(m + 1) % 3]},{t[(m + 2) % 3]}"; })
        .Order(StringComparer.Ordinal));

    [Theory]
    [InlineData(17)] [InlineData(40)] [InlineData(80)] [InlineData(128)] [InlineData(192)]
    public void TheLocalHullIsTheExactHullOfTheCrownLattice(int count)
    {
        var (directions, triangles) = LobeCrownMesh.Sphere(count);
        Assert.Equal(Faces(Brute(directions)), Faces(triangles));
    }

    [Theory]
    [InlineData(384)] [InlineData(1536)]
    public void TheLargestCrownLatticeIsAClosedSurfaceBuiltWithoutAStall(int count)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (directions, triangles) = LobeCrownMesh.Sphere(count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(count > 384 ? 8 : 2), $"{count}-point lattice took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(3 * (2 * directions.Length - 4), triangles.Length);
        // Closed and consistently wound: every directed edge appears once and its reverse once.
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < triangles.Length; i += 3)
            for (int e = 0; e < 3; e++) Assert.True(edges.Add((triangles[i + e], triangles[i + (e + 1) % 3])));
        Assert.All(edges, e => Assert.Contains((e.Item2, e.Item1), edges));
    }
}
