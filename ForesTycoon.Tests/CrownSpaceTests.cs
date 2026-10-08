using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class CrownSpaceTests
{
    private static CrownSpace Measure(params (Vector2 Position, float Radius, float Height)[] others) =>
        CrownSpace.Measure(Vector2.Zero, 3, 20, others);

    [Fact]
    public void SolitaryTreeSpreadsEverywhere()
    {
        var space = Measure();
        for (int k = 0; k < CrownSpace.Sectors; k++) Assert.Equal(CrownSpace.Open, space.Level(k));
        Assert.Equal(1.6f, space.Factor(0.3f), 4);
    }

    [Fact]
    public void ClosedStandFillsTheSpaceBetweenEqualCrowns()
    {
        // Neighbours 5 m away all round: each crown gets half the gap (2.5 m, interlocking to 2.8 m) of its 3 m radius.
        var ring = new (Vector2, float, float)[8];
        for (int k = 0; k < 8; k++) ring[k] = (new Vector2(MathF.Cos(k * MathF.Tau / 8), MathF.Sin(k * MathF.Tau / 8)) * 5, 3, 20);
        var space = Measure(ring);
        for (int k = 0; k < CrownSpace.Sectors; k++) Assert.Equal(CrownSpace.Plain, space.Level(k));
        // Widely spaced stems: small simulated crowns still fill the canopy between them.
        var sparse = new (Vector2, float, float)[8];
        for (int k = 0; k < 8; k++) sparse[k] = (ring[k].Item1 * 1.8f, 3, 20);
        var filled = Measure(sparse);
        for (int k = 0; k < CrownSpace.Sectors; k++) Assert.True(filled.Level(k) > CrownSpace.Plain);
    }

    [Fact]
    public void EdgeTreeSpreadsOnlyTowardsTheOpenSide()
    {
        // Stand to the west, open land to the east.
        var space = Measure((new(-5, 0), 3, 20), (new(-4, 4), 3, 20), (new(-4, -4), 3, 20));
        Assert.Equal(CrownSpace.Open, space.Level(0));         // east: open
        Assert.True(space.Level(4) <= CrownSpace.Plain);       // west: pressed
        Assert.True(space.Factor(0) > space.Factor(MathF.PI));
    }

    [Fact]
    public void LargerNeighbourTakesTheLargerShareAndUnderstoreyDoesNotPress()
    {
        var small = Measure((new(5, 0), 6, 20));
        var equal = Measure((new(5, 0), 3, 20));
        Assert.True(small.Level(0) < equal.Level(0));
        Assert.True(Measure((new(4, 0), 3, 8)).IsUniform == false);
        Assert.Equal(CrownSpace.Open, Measure((new(4, 0), 3, 8)).Level(0)); // a 8 m tree under a 20 m crown
    }

    [Fact]
    public void DefaultIsTheUnshapedCrownAndTakesPartInTheShapeKey()
    {
        Assert.True(default(CrownSpace).IsUniform);
        Assert.Equal(1, default(CrownSpace).Factor(2));
        var spec = new TreeShapeSpec(ForestSpecies.Beech, 1, TreeLifePhase.Mature, new(0.5f, 25, 5), 1, new TreeSite(0.9f), LeafState.Full, 0);
        var edge = spec with { Site = spec.Site with { Space = Measure((new(-3.5f, 0), 3, 20), (new(-2.8f, 2.8f), 3, 20), (new(-2.8f, -2.8f), 3, 20)) } };
        Assert.NotEqual(spec.ShapeKey, edge.ShapeKey);
        // The pressed side of the crown pulls in, the open side spreads.
        var plain = DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown;
        var shaped = DendroTreeGenerator.Generate(edge, ForestLod.Near).Crown;
        Assert.True(shaped.Max(v => v.Position.X) > plain.Max(v => v.Position.X));
        Assert.True(shaped.Min(v => v.Position.X) > plain.Min(v => v.Position.X));
        Assert.Equal(plain.Max(v => v.Position.Z), shaped.Max(v => v.Position.Z), 4);
    }
}
