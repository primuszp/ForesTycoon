using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class ForestVisualPrototypeTests
{
    [Theory]
    [InlineData(ForestSpecies.Spruce)]
    [InlineData(ForestSpecies.Birch)]
    [InlineData(ForestSpecies.Oak)]
    [InlineData(ForestSpecies.Beech)]
    internal void Crown_IsDeterministicClosedAndHasFiniteOutwardNormals(ForestSpecies species)
    {
        var first = Build(species, ForestLod.Near, 42);
        var second = Build(species, ForestLod.Near, 42);
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.All(first, vertex =>
        {
            Assert.True(float.IsFinite(vertex.Position.X + vertex.Position.Y + vertex.Position.Z));
            Assert.Equal(1f, vertex.Normal.Length, 3);
            Assert.InRange(vertex.Position.Z, 0, 5);
        });
        // Every geometric edge is shared by exactly two triangles, including seam and poles.
        var edges = new Dictionary<(Vector3, Vector3), int>();
        for (int i = 0; i < first.Count; i += 3)
        {
            Vector3 a = first[i].Position, b = first[i + 1].Position, c = first[i + 2].Position;
            Vector3 face = Vector3.Cross(b - a, c - a);
            Assert.True(face.LengthSquared > 0.000001f);
            Vector3 normal = first[i].Normal + first[i + 1].Normal + first[i + 2].Normal;
            Assert.True(Vector3.Dot(face, normal) > 0);
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        Assert.All(edges.Values, count => Assert.Equal(2, count));
        Assert.True(Build(species, ForestLod.Far, 42).Count < Build(species, ForestLod.Medium, 42).Count);
        Assert.True(Build(species, ForestLod.Medium, 42).Count < first.Count);
        void Edge(Vector3 a, Vector3 b)
        {
            if (a.X > b.X || (a.X == b.X && (a.Y > b.Y || (a.Y == b.Y && a.Z > b.Z)))) (a, b) = (b, a);
            var key = (a, b); edges[key] = edges.GetValueOrDefault(key) + 1;
        }
    }

    [Fact]
    public void Fixture_HasFourSpeciesClearingAndStableSeed()
    {
        ForestStand[] first = ForestVisualFixture.CreateStands();
        Assert.Equal(first, ForestVisualFixture.CreateStands());
        Assert.Equal(256, first.Length);
        Assert.True(first[8 * 16 + 7].IsEmpty);
        Assert.Equal(4, first.Where(s => !s.IsEmpty).Select(s => s.Species).Distinct().Count());
        Assert.Contains(first, s => !s.IsEmpty && s.Maturity < 0.7f);
        Assert.Contains(first, s => s.Maturity >= 1f);
    }

    [Fact]
    public void SurfacePoint_UsesMeshDiagonalInsteadOfBilinearHeight()
    {
        var data = new TerrainData(TerrainSettings.Default.WithNodeSize(17, 42));
        Tile tile = data.GetTile(2, 2);
        tile.W.zPos = 0; tile.S.zPos = 2; tile.E.zPos = 0; tile.N.zPos = 2;
        Terrain.SurfacePoint(tile, 0.5f, 0.5f, out _, out _, out float z);
        Assert.Equal(0f, z); // W-E diagonal; bilinear interpolation would incorrectly give 1.
        Terrain.SurfacePoint(tile, 0.75f, 0.25f, out _, out _, out z);
        Assert.Equal(1f, z);
        tile.W.zPos = 0; tile.S.zPos = 2; tile.E.zPos = 4; tile.N.zPos = 2;
        Terrain.SurfacePoint(tile, 0.5f, 0.5f, out _, out _, out z);
        Assert.Equal(2f, z); // N-S diagonal.
    }

    private static List<Vertex> Build(ForestSpecies species, ForestLod lod, int seed)
    {
        var vertices = new List<Vertex>();
        ForestCrownMesh.Append(vertices, species, Vector3.Zero, 2, 5, 0, seed, Color.Green, lod);
        return vertices;
    }
}
