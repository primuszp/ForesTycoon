using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Two RGBA32F texels per living tree. X=diameter, Y=crown radius, Z=height.
    // Scale is relative to the dimensions baked into the cached mesh, not the last month.
    internal readonly record struct ForestTreeRenderState(Vector4 Scale, Vector4 Rate)
    {
        internal static ForestTreeRenderState Create(in ForestTree tree, ForestTreeDimensions meshSize, double year)
        {
            var size = tree.At(year);
            return new(new(size.Diameter / meshSize.Diameter, size.CrownRadius / meshSize.CrownRadius,
                    size.Height / meshSize.Height, tree.Health),
                new(tree.AnnualGrowth.Diameter / meshSize.Diameter, tree.AnnualGrowth.CrownRadius / meshSize.CrownRadius,
                    tree.AnnualGrowth.Height / meshSize.Height, 0));
        }
    }
}
