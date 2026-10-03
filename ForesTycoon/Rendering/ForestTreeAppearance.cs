using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal enum TreeLifeStage { Seedling, Young, Mature, Old }

    // Visual ages are gameplay art thresholds, not a biological mortality model.
    internal static class ForestTreeAppearance
    {
        internal static int Variant(int seed) => (int)((uint)seed % 3);
        internal static float NextStageAge(ForestSpecies species, TreeLifeStage stage) => stage switch
        {
            TreeLifeStage.Seedling => species == ForestSpecies.Birch ? 2 : 3,
            TreeLifeStage.Young => ForestSpeciesProfile.For(species).MatureAgeYears * 0.75f,
            TreeLifeStage.Mature => ForestSpeciesProfile.For(species).MaximumAgeYears * 0.65f,
            _ => float.PositiveInfinity
        };
        internal static TreeLifeStage Stage(ForestSpecies species, float age)
        {
            float seedling = species == ForestSpecies.Birch ? 2 : 3;
            var profile = ForestSpeciesProfile.For(species);
            return age < seedling ? TreeLifeStage.Seedling
                : age < profile.MatureAgeYears * 0.75f ? TreeLifeStage.Young
                : age < profile.MaximumAgeYears * 0.65f ? TreeLifeStage.Mature : TreeLifeStage.Old;
        }

        internal static float CrownFraction(ForestSpecies species, TreeLifeStage stage) => stage switch
        {
            TreeLifeStage.Seedling => 0.90f,
            TreeLifeStage.Young => species == ForestSpecies.Spruce ? 0.94f : 0.80f,
            TreeLifeStage.Old => species == ForestSpecies.Spruce ? 0.66f : 0.53f,
            _ => species == ForestSpecies.Spruce ? 0.86f : species == ForestSpecies.Oak ? 0.70f
                : species == ForestSpecies.Birch ? 0.65f : 0.72f
        };

        internal readonly record struct CrownLobe(Vector3 Origin, float Radius, float Height);

        // Shared by the crown and its supporting branches. All lobes stay inside the
        // requested vertical envelope; variants change topology, not just overall scale.
        internal static int BroadleafLobes(Span<CrownLobe> lobes, ForestSpecies species,
            TreeLifeStage stage, int seed, float radius, float height, float yaw)
        {
            int variant = Variant(seed);
            if (stage == TreeLifeStage.Seedling)
            {
                int count = 3 + variant;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)(count - 1);
                    float angle = yaw + i * 2.399963f;
                    float reach = i == count - 1 ? 0 : radius * (0.32f + variant * 0.09f);
                    float leafHeight = height * (species == ForestSpecies.Birch ? 0.30f : 0.24f);
                    lobes[i] = new(new(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach,
                        t * (height - leafHeight)), radius * (species == ForestSpecies.Oak ? 0.52f : 0.40f), leafHeight);
                }
                return count;
            }
            if (stage == TreeLifeStage.Young)
            {
                lobes[0] = new(Vector3.UnitZ * height * 0.15f, radius * (0.57f + variant * 0.13f), height * 0.85f);
                int count = 3 + variant;
                for (int i = 1; i < count; i++)
                {
                    float angle = yaw + MathF.Tau * (i - 1) / (count - 1);
                    float z = height * (0.04f + 0.10f * (i % 2));
                    lobes[i] = new(new(MathF.Cos(angle) * radius * 0.40f, MathF.Sin(angle) * radius * 0.40f, z),
                        radius * (0.38f + variant * 0.045f), height * (0.52f - variant * 0.04f));
                }
                return count;
            }
            bool old = stage == TreeLifeStage.Old;
            int branches = (old ? 3 : 4) + variant;
            // Oak spreads, beech retains a high central crown, birch has smaller airy lobes.
            float centralWidth = species == ForestSpecies.Beech ? 0.60f : species == ForestSpecies.Birch ? 0.38f : 0.52f;
            lobes[0] = new(Vector3.UnitZ * height * (old ? 0.48f : 0.22f), radius * centralWidth,
                height * (old ? 0.52f : 0.78f));
            for (int i = 0; i < branches; i++)
            {
                float angle = yaw + MathF.Tau * i / branches
                    + ForestTreeVariation.Range(seed, 300 + i, -0.18f, 0.18f);
                float reach = radius * (old ? 0.65f : variant == 0 ? 0.38f : variant == 1 ? 0.63f : 0.52f);
                float z = height * (0.04f + (i % 3) * (old ? 0.13f : 0.09f));
                float h = height * (species == ForestSpecies.Beech ? 0.64f : species == ForestSpecies.Birch ? 0.48f : 0.55f);
                if (old) h *= 0.72f;
                lobes[i + 1] = new(new(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach, z),
                    radius * (species == ForestSpecies.Birch ? 0.36f : old ? 0.40f : 0.48f), h);
            }
            return branches + 1;
        }
    }
}
