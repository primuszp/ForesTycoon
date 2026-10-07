using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Ecology
{
    /// <summary>Finer-grained life phases than the legacy four-step <see cref="TreeLifeStage"/>.</summary>
    internal enum TreeLifePhase : byte { Seedling, Sapling, Young, Mature, Old, Senescent }

    /// <summary>Foliage state of a (deciduous) crown through the year.</summary>
    internal enum LeafState : byte { Bare, Budding, Full, Autumn, Falling }

    /// <summary>Coarse vitality class: drives crown dieback, not physical size.</summary>
    internal enum TreeVigorBand : byte { Vigorous, Reduced, Declining, Dying }

    internal static class TreeLifePhases
    {
        internal static TreeLifePhase Of(ForestSpecies species, float age)
        {
            var profile = ForestSpeciesProfile.For(species);
            return age < SeedlingAge(species) ? TreeLifePhase.Seedling
                : age < profile.MatureAgeYears * 0.30f ? TreeLifePhase.Sapling
                : age < profile.MatureAgeYears * 0.75f ? TreeLifePhase.Young
                : age < profile.MaximumAgeYears * 0.65f ? TreeLifePhase.Mature
                : age < profile.MaximumAgeYears * 0.88f ? TreeLifePhase.Old : TreeLifePhase.Senescent;
        }

        /// <summary>Age at which the phase after <paramref name="phase"/> begins.</summary>
        internal static float NextAge(ForestSpecies species, TreeLifePhase phase)
        {
            var profile = ForestSpeciesProfile.For(species);
            return phase switch
            {
                TreeLifePhase.Seedling => SeedlingAge(species),
                TreeLifePhase.Sapling => profile.MatureAgeYears * 0.30f,
                TreeLifePhase.Young => profile.MatureAgeYears * 0.75f,
                TreeLifePhase.Mature => profile.MaximumAgeYears * 0.65f,
                TreeLifePhase.Old => profile.MaximumAgeYears * 0.88f,
                _ => float.PositiveInfinity
            };
        }

        // Shrubs mature in a few years, so their seedling phase is correspondingly short.
        private static float SeedlingAge(ForestSpecies species) =>
            Math.Min(species == ForestSpecies.Birch ? 2f : 3f, 0.15f * ForestSpeciesProfile.For(species).MatureAgeYears);

        /// <summary>Mapping onto the legacy stages used by the glTF and lobe-based crowns.</summary>
        internal static TreeLifeStage Coarse(TreeLifePhase phase) => phase switch
        {
            TreeLifePhase.Seedling => TreeLifeStage.Seedling,
            TreeLifePhase.Sapling or TreeLifePhase.Young => TreeLifeStage.Young,
            TreeLifePhase.Mature => TreeLifeStage.Mature,
            _ => TreeLifeStage.Old
        };

        internal static TreeLifePhase From(TreeLifeStage stage) => stage switch
        {
            TreeLifeStage.Seedling => TreeLifePhase.Seedling,
            TreeLifeStage.Young => TreeLifePhase.Young,
            TreeLifeStage.Mature => TreeLifePhase.Mature,
            _ => TreeLifePhase.Old
        };

        /// <summary>0 = just germinated ... 1 = fully grown; later phases stay above 1.</summary>
        internal static float Maturity(TreeLifePhase phase) => phase switch
        {
            TreeLifePhase.Seedling => 0f, TreeLifePhase.Sapling => 0.2f, TreeLifePhase.Young => 0.45f,
            TreeLifePhase.Mature => 0.75f, TreeLifePhase.Old => 1f, _ => 1.15f
        };

        /// <summary>Broadly the crown fraction of the total height, before light and vigour.</summary>
        internal static float CrownFraction(ForestSpecies species, TreeLifePhase phase)
        {
            if (ForestSpeciesTraits.For(species).Shrub) return 0.98f;
            if (species == ForestSpecies.Pine)
                // Scots pine self-prunes early: tiered cone when young, a high flat crown on a long clear bole later.
                return phase switch { TreeLifePhase.Seedling => 0.95f, TreeLifePhase.Sapling => 0.92f, TreeLifePhase.Young => 0.78f,
                    TreeLifePhase.Mature => 0.46f, TreeLifePhase.Old => 0.40f, _ => 0.34f };
            float mature = species switch { ForestSpecies.Ash => 0.62f, ForestSpecies.Maple => 0.68f, ForestSpecies.SessileOak => 0.66f,
                ForestSpecies.TurkeyOak => 0.70f, ForestSpecies.Larch or ForestSpecies.Fir => 0.86f, _ => -1f };
            if (mature < 0)
                return phase switch
                {
                    TreeLifePhase.Sapling => 0.5f * (TreeLifeStages.CrownFraction(species, TreeLifeStage.Seedling)
                        + TreeLifeStages.CrownFraction(species, TreeLifeStage.Young)),
                    // Old broadleaves keep a broad low crown; they lose a little from the base and a lot from the top.
                    TreeLifePhase.Old => species == ForestSpecies.Spruce ? TreeLifeStages.CrownFraction(species, TreeLifeStage.Old)
                        : TreeLifeStages.CrownFraction(species, TreeLifeStage.Mature) - 0.04f,
                    TreeLifePhase.Senescent => species == ForestSpecies.Spruce ? TreeLifeStages.CrownFraction(species, TreeLifeStage.Old) - 0.05f
                        : TreeLifeStages.CrownFraction(species, TreeLifeStage.Mature) - 0.10f,
                    _ => TreeLifeStages.CrownFraction(species, Coarse(phase))
                };
            return phase switch
            {
                TreeLifePhase.Seedling => 0.90f, TreeLifePhase.Sapling => 0.86f, TreeLifePhase.Young => 0.80f,
                TreeLifePhase.Mature => mature, TreeLifePhase.Old => mature - 0.04f, _ => mature - 0.10f
            };
        }
    }
}
