using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal readonly record struct ForestSeasonRenderState(Vector4 Tint, Vector4 Bounds)
    {
        internal static ForestSeasonRenderState Create(in TreeShapeSpec spec)
        {
            var calendar = TreePhenology.RenderCalendar(spec.Species, unchecked((uint)spec.Seed));
            return new(new Vector4(TreeForm.AutumnMultiplier(spec), calendar.Start),
                new Vector4(calendar.Budding, calendar.Full, calendar.Autumn, calendar.Falling));
        }
    }

    // First two of four RGBA32F texels per living tree. The other two hold static phenology metadata.
    // X=diameter, Y=crown radius, Z=height.
    // Scale is relative to the dimensions baked into the cached mesh, not the last month.
    internal readonly record struct ForestTreeRenderState(Vector4 Scale, Vector4 Rate)
    {
        internal static ForestTreeRenderState Create(in ForestTree tree, ForestTreeDimensions meshSize, double year)
        {
            var size = tree.At(year);
            return new(new(size.Diameter / meshSize.Diameter, size.CrownRadius / meshSize.CrownRadius,
                    size.Height / meshSize.Height, tree.Health),
                new(tree.AnnualGrowth.Diameter / meshSize.Diameter, tree.AnnualGrowth.CrownRadius / meshSize.CrownRadius,
                    tree.AnnualGrowth.Height / meshSize.Height, tree.Resources.Light));
        }
    }
}
