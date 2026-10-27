using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class CrownSurfaceTests
{
    [Fact]
    public void LeafCloudFootprintFollowsItsPrincipalAxisWithoutAddingVolume()
    {
        float angle = .73f, c = MathF.Cos(angle), s = MathF.Sin(angle);
        // Covariance of a 4:1 cloud, rotated by angle.
        var footprint = LobeCrownMesh.Footprint(4 * c * c + s * s, 4 * s * s + c * c, 3 * c * s, 1);
        Assert.InRange(Math.Abs(Vector2.Dot(footprint.Axis, new(c, s))), .99999f, 1.00001f);
        Assert.Equal(MathF.Sqrt(2), footprint.Stretch, 5);
        float radius = .47f;
        Assert.Equal(radius * radius, (radius * footprint.Stretch) * (radius / footprint.Stretch), 6);
        Assert.Equal(1, LobeCrownMesh.Footprint(4, 1, 0, 0).Stretch);
        Assert.Equal(1, LobeCrownMesh.Footprint(0, 0, 0, 1).Stretch);
        Assert.Equal(1, LobeCrownMesh.Footprint(2, 2, 0, 1).Stretch);
    }

    [Fact]
    public void SurfaceDetailIsContinuousBoundedAndIndependentOfSampleOrder()
    {
        var directions = LobeCrownMesh.Sphere(384).Directions;
        var values = directions.Select(d => CrownSurfaceDetail.Factor(d, 13, .35f)).ToArray();
        Assert.All(values, v => Assert.InRange(v, .79f, 1.21f));
        Assert.True(values.Max() - values.Min() > .03f);
        for (int i = directions.Length - 1; i >= 0; i--)
        {
            var d = directions[i];
            Assert.Equal(values[i], CrownSurfaceDetail.Factor(d, 13, .35f));
            var nearby = (d + new Vector3(.00001f, -.00001f, .00001f)).Normalized();
            Assert.InRange(Math.Abs(values[i] - CrownSurfaceDetail.Factor(nearby, 13, .35f)), 0, .0001f);
            Assert.Equal(1, CrownSurfaceDetail.Factor(d, 13, 0));
        }
        Assert.False(values.SequenceEqual(directions.Select(d => CrownSurfaceDetail.Factor(d, 14, .35f))));
    }

    [Fact]
    public void RaySupportBoundContainsTheWholeStretchedLobeAndSkipsMisses()
    {
        Assert.Equal(1.3f, LobeCrownMesh.SupportExit(Vector3.Zero, Vector3.UnitX, 1), 5);
        Assert.Equal(2.6f, LobeCrownMesh.SupportExit(Vector3.Zero, -Vector3.UnitZ, 2), 5);
        Assert.Equal(0, LobeCrownMesh.SupportExit(new(0, 2, 0), Vector3.UnitX, 1));
        Assert.Equal(0, LobeCrownMesh.SupportExit(new(2, 0, 0), Vector3.UnitX, 1));
        Assert.Equal(3.3f, LobeCrownMesh.SupportExit(new(2, 0, 0), -Vector3.UnitX, 1), 5);
        Assert.Equal(0, LobeCrownMesh.SupportExit(Vector3.Zero, Vector3.Zero, 1));
    }

    [Fact]
    public void FillingFlatCrownShouldersKeepsTheSameFootprintFromEveryDirection()
    {
        Vector3[] points = { new(-1, -2, 0), new(1, -2, 0), new(1, 2, 1), new(-1, 2, 1), Vector3.Zero };
        var hull = CrownContour.Hull(points);
        Assert.Equal(4, hull.Length);
        var p = new Vector2(.5f, .2f);
        float factor = CrownContour.Limit(hull, p, 3);
        Assert.Equal(2, factor, 5);
        Assert.Equal(1, CrownContour.Limit(hull, new(1, 2), 3));
        // A rotated tree receives the same correction; an axis-aligned box clamp would not.
        const float angle = .73f;
        Vector2 Turn(Vector2 q) => new(MathF.Cos(angle) * q.X - MathF.Sin(angle) * q.Y,
            MathF.Sin(angle) * q.X + MathF.Cos(angle) * q.Y);
        var rotated = CrownContour.Hull(points.Select(q => { var xy = Turn(q.Xy); return new Vector3(xy.X, xy.Y, q.Z); }).ToArray());
        Assert.Equal(factor, CrownContour.Limit(rotated, Turn(p), 3), 5);
    }

    [Theory]
    [InlineData(ForestSpecies.Oak)] [InlineData(ForestSpecies.Beech)]
    [InlineData(ForestSpecies.Birch)] [InlineData(ForestSpecies.Pine)]
    [InlineData(ForestSpecies.Maple)] [InlineData(ForestSpecies.Ash)]
    internal void CrownDetailRotatesWithItsBranchesAndEveryLodStaysClosed(ForestSpecies species)
    {
        var spec = new TreeShapeSpec(species, 13, TreeLifePhase.Mature, new(.4f, 20, 4), 1, new TreeSite(1), LeafState.Full, 0);
        const float angle = .72f;
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        foreach (var lod in Enum.GetValues<ForestLod>())
        {
            var crown = DendroTreeGenerator.Generate(spec, lod).Crown;
            var rotated = DendroTreeGenerator.Generate(spec with { Yaw = angle }, lod).Crown;
            Assert.Equal(crown.Length, rotated.Length);
            var edges = new Dictionary<(Vector3, Vector3), int>();
            for (int i = 0; i < crown.Length; i++)
            {
                var p = crown[i].Position;
                var expected = new Vector3(c * p.X - s * p.Y, s * p.X + c * p.Y, p.Z);
                Assert.InRange((expected - rotated[i].Position).Length, 0, .0001f);
                Assert.True(float.IsFinite(p.LengthSquared + crown[i].Normal.LengthSquared));
                Assert.InRange(crown[i].Normal.Length, .999f, 1.001f);
                var edge = (p, crown[i / 3 * 3 + (i + 1) % 3].Position);
                edges.TryGetValue(edge, out int count); edges[edge] = count + 1;
            }
            Assert.All(edges, edge => {
                Assert.Equal(1, edge.Value);
                Assert.True(edges.TryGetValue((edge.Key.Item2, edge.Key.Item1), out int reverse) && reverse == 1);
            });
            for (int i = 0; i < crown.Length; i += 3)
                Assert.True(Vector3.Cross(crown[i + 1].Position - crown[i].Position,
                    crown[i + 2].Position - crown[i].Position).LengthSquared > 1e-14f);
        }
    }
}
