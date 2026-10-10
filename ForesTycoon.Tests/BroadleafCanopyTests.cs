using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class BroadleafCanopyTests
{
    private static TreeShapeSpec Spec(ForestSpecies species, CrownSpace space) => new(species, 42,
        TreeLifePhase.Mature, new(.45f, 18, 5), 1, new TreeSite(.85f, Space: space), LeafState.Full, 0);

    [Fact]
    public void MeasuredNeutralSpaceIsClosedCanopyRatherThanMissingNeighbourData()
    {
        var closed = CrownSpace.From(Enumerable.Repeat(CrownSpace.Plain, CrownSpace.Sectors).ToArray());
        var unknown = default(CrownSpace);
        Assert.NotEqual(closed, unknown);
        Assert.Equal(unknown.Factor(.7f), closed.Factor(.7f));
        var shaded = new TreeForm(Spec(ForestSpecies.Beech, closed));
        var plain = new TreeForm(Spec(ForestSpecies.Beech, unknown));
        Assert.True(shaded.SideExposure(0) < plain.SideExposure(0));
        Assert.True(shaded.CrownBase > plain.CrownBase);
        Assert.NotEqual(shaded.Spec.ShapeKey, plain.Spec.ShapeKey);
    }

    [Fact]
    public void BeechAdaptsMoreThanOakOnBothOpenAndCrowdedSides()
    {
        var space = CrownSpace.From(new[] { 6, 5, 3, 2, 1, 2, 3, 5 });
        var beech = new TreeForm(Spec(ForestSpecies.Beech, space));
        var oak = new TreeForm(Spec(ForestSpecies.Oak, space));
        var openPoint = new Vector3(1, 0, beech.Height);
        var crowdedPoint = new Vector3(-1, 0, beech.Height);
        Assert.True(beech.Warp.Apply(openPoint).X > oak.Warp.Apply(openPoint).X);
        Assert.True(Math.Abs(beech.Warp.Apply(crowdedPoint).X) < Math.Abs(oak.Warp.Apply(crowdedPoint).X));
        Assert.Equal(openPoint.Z, beech.Warp.Apply(openPoint).Z);
        Assert.Equal(crowdedPoint.Z, oak.Warp.Apply(crowdedPoint).Z);
    }

    [Theory]
    [InlineData(ForestSpecies.Beech)]
    [InlineData(ForestSpecies.Oak)]
    internal void EdgeFoliageIsBrighterTowardsOpenSpaceWhileTheUpperCanopyStaysFull(ForestSpecies species)
    {
        var space = CrownSpace.From(new[] { 6, 5, 3, 2, 1, 2, 3, 5 });
        var form = new TreeForm(Spec(species, space));
        var mesh = DendroTreeGenerator.Build(form, ForestLod.Near);
        float low = form.CrownBase + .3f * (form.Height - form.CrownBase), high = form.CrownBase + .7f * (form.Height - form.CrownBase);
        var mid = mesh.Crown.Where(v => v.Position.Z > low && v.Position.Z < high);
        double Brightness(Vertex v) => (v.Color & 255) + ((v.Color >> 8) & 255) + ((v.Color >> 16) & 255);
        double open = mid.Where(v => v.Position.X > 0 && Math.Abs(v.Position.Y) < .6f * v.Position.X).Average(Brightness);
        double shaded = mid.Where(v => v.Position.X < 0 && Math.Abs(v.Position.Y) < -.6f * v.Position.X).Average(Brightness);
        Assert.True(open > shaded, $"{species}: open {open}, shaded {shaded}");
        Assert.Equal(form.Height, mesh.Crown.Max(v => v.Position.Z), 5);
        Assert.InRange(mesh.Crown.Length / 3, 1, 764);
    }

    [Theory]
    [InlineData(ForestSpecies.Beech)]
    [InlineData(ForestSpecies.Oak)]
    internal void RoundedCanopyKeepsItsCoverageHeightAndCheapLodBudget(ForestSpecies species)
    {
        var space = CrownSpace.From(new[] { 6, 5, 3, 2, 1, 2, 3, 5 });
        var spec = Spec(species, space);
        var near = DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown;
        float Width(Vertex[] vertices) => vertices.Max(v => v.Position.X) - vertices.Min(v => v.Position.X);
        foreach (var lod in new[] { ForestLod.Medium, ForestLod.Far })
        {
            var mesh = DendroTreeGenerator.Generate(spec, lod).Crown;
            Assert.True(mesh.Length < near.Length);
            Assert.Equal(near.Max(v => v.Position.Z), mesh.Max(v => v.Position.Z), 5);
            Assert.InRange(Width(mesh) / Width(near), .8f, 1.2f);
            if (lod == ForestLod.Far) Assert.Equal(30, mesh.Length / 3);
        }
    }
}
