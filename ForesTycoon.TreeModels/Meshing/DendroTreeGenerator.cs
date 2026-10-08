using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    internal enum ShrubForm { Hazel = 1, Hawthorn = 2 }

    // Builds the render meshes of one tree from a TreeShapeSpec: a wood mesh (trunk with root flare
    // and roots, limbs, or the complete leafless branch system) and, while the tree carries leaves,
    // one opaque, closed crown cloud derived from the live leaf positions. Leaves are construction
    // samples only; no leaf cards or alpha cut-outs are ever produced.
    // Species parameters follow docs/tree-generation-literature.md.
    internal static class DendroTreeGenerator
    {
        internal sealed record Mesh(Vertex[] Trunk, Vertex[] Branches, Vertex[] Crown, long StemCount, int LeafCount);

        internal static int LightBand(float light) => TreeShapeBands.LightBand(light);
        internal static float BandLight(int band) => TreeShapeBands.BandLight(band);

        /// <summary>Legacy entry: a healthy, fully leaved tree in an average site at the given light.</summary>
        internal static Mesh Generate(ForestSpecies species, int seed, TreeLifeStage stage,
            float light, ForestTreeDimensions size, float yaw, ForestLod lod, bool includeCrown = true) =>
            Generate(new TreeShapeSpec(species, seed, TreeLifePhases.From(stage), size, includeCrown ? 1 : 0,
                new TreeSite(light), includeCrown ? LeafState.Full : LeafState.Bare, yaw, !includeCrown), lod);

        /// <summary>Legacy entry for the two original shrub forms; shrubs are now ordinary species.</summary>
        internal static Mesh GenerateShrub(int seed, TreeLifeStage stage, float light,
            ForestTreeDimensions size, float yaw, ForestLod lod, ShrubForm form = ShrubForm.Hazel)
        {
            if (!Enum.IsDefined(form)) throw new ArgumentOutOfRangeException(nameof(form));
            return Generate(new TreeShapeSpec(form == ShrubForm.Hazel ? ForestSpecies.Hazel : ForestSpecies.Hawthorn, seed,
                TreeLifePhases.From(stage), size, 1, new TreeSite(light), LeafState.Full, yaw), lod);
        }

        internal static Mesh Generate(in TreeShapeSpec spec, ForestLod lod, TreeSkeleton skeleton = null) => Build(new TreeForm(spec, skeleton), lod);

        internal static Mesh Build(TreeForm form, ForestLod lod)
        {
            var spec = form.Spec; var sk = form.Skeleton;
            bool crowned = form.Foliage > 0 && !spec.Dead;
            var trunk = new List<Vertex>(); var branches = new List<Vertex>();
            TreeWoodMesh.Build(form, lod, crowned, trunk, branches);
            Vertex[] crown = Array.Empty<Vertex>();
            if (crowned && LobeCrownMesh.Applies(spec.Species, spec.Phase))
                crown = LobeCrownMesh.Build(form, lod);
            else if (crowned)
            {
                // Only living leaves shape the crown, so dieback opens it up where limbs died.
                var support = new List<Vector3>(sk.Leaves.Length);
                for (int i = 0; i < sk.Leaves.Length; i++)
                    if (!form.Dead[sk.LeafStem[i]]) support.Add(form.ToFrame(sk.Leaves[i]));
                if (support.Count == 0) foreach (var leaf in sk.Leaves) support.Add(form.ToFrame(leaf));
                // The crown applies the site warp itself, so it gets the unwarped support points.
                crown = DendroCrownMesh.Build(DendroCrownMesh.For(spec.Species, spec.Phase), spec.Seed, TreeLifePhases.Coarse(spec.Phase),
                    form.Height, form.CrownRadius, form.CrownFraction, spec.Yaw, support, form.CrownColor, lod,
                    new CrownShaping(form.Foliage, form.Dieback,
                        form.Dieback > 0.45f && spec.Phase >= TreeLifePhase.Old ? Math.Min(0.25f, (form.Dieback - 0.3f) * 0.5f) : 0,
                        form.Warp));
            }
            return new(trunk.ToArray(), branches.ToArray(), crown, sk.StemCount, sk.LeafTotal);
        }
    }
}
