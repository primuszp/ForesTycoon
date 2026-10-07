using System;

namespace ForesTycoon.Ecology
{
    /// <summary>The original four-step life stages, kept for the legacy crown meshes and imported models.</summary>
    internal enum TreeLifeStage { Seedling, Young, Mature, Old }

    // Visual ages are gameplay art thresholds, not a biological mortality model.
    internal static class TreeLifeStages
    {
        internal static float NextAge(ForestSpecies species, TreeLifeStage stage) => stage switch
        {
            TreeLifeStage.Seedling => species == ForestSpecies.Birch ? 2 : 3,
            TreeLifeStage.Young => ForestSpeciesProfile.For(species).MatureAgeYears * 0.75f,
            TreeLifeStage.Mature => ForestSpeciesProfile.For(species).MaximumAgeYears * 0.65f,
            _ => float.PositiveInfinity
        };

        internal static TreeLifeStage Of(ForestSpecies species, float age)
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
    }
}
