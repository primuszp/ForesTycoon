using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private static ForestCoverageInstance CoverageOf(Tile tile, in ForestTree tree, double year, TreeLifePhase phase, out bool whorled)
        {
            SurfacePoint(tile, tree.U, tree.V, out float x, out float y, out float z);
            var spec = TreeShapeSpec.From(tree, year, TreeSite.Open, 0) with { Leaves = LeafState.Full };
            var bands = spec.Bands;
            var traits = ForestSpeciesTraits.For(tree.Species);
            float fraction = TreeArchitecture.CrownFraction(tree.Species, phase, bands.Light);
            float radiusScale = TreeMetresToWorld * (.74f + .26f * TreeArchitecture.LightResponse(tree.Species, bands.Light01));
            var crownForm = DendroCrownMesh.For(tree.Species, phase);
            whorled = DendroCrownMesh.Whorled(crownForm);
            float cone = crownForm switch {
                CrownForm.Spruce or CrownForm.Fir or CrownForm.Larch or CrownForm.YoungPine or CrownForm.Juniper => 1,
                CrownForm.Pine => .15f,
                _ => 0
            };
            float pigment = ForestTreeVariation.Range(spec.Seed, 601, .90f, 1.08f);
            uint wood = TreeForm.Pack(traits.Bark, tree.Species, pigment);
            uint crown = (TreeForm.Pack(TreeForm.LeafTint(traits.Crown, spec), tree.Species, pigment) & 0xffffffu)
                | (uint)TreeScale.CrownSpeciesCode(tree.Species) << 24;
            var season = ForestSeasonRenderState.Create(spec);
            return new(new(x, y, z, spec.Size.CrownRadius * radiusScale),
                new(spec.Size.Height * TreeMetresToWorld, fraction, spec.Size.Diameter * .5f * TreeMetresToWorld, cone),
                new(tree.AnnualGrowth.CrownRadius * radiusScale, tree.AnnualGrowth.Height * TreeMetresToWorld,
                    tree.AnnualGrowth.Diameter * .5f * TreeMetresToWorld, tree.Health),
                Unpack(crown), Unpack(wood), season.Tint, season.Bounds);

            static Vector4 Unpack(uint packed) =>
                new Vector4(packed & 255, (packed >> 8) & 255, (packed >> 16) & 255, packed >> 24) / 255f;
        }
    }
}
