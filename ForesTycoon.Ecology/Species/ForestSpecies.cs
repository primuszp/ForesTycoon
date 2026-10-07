namespace ForesTycoon.Ecology
{
    enum ForestSpecies : byte
    {
        None = 0,
        Spruce = 2,
        Birch = 3,
        Oak = 4,
        Beech = 5,
        Maple = 6,
        Ash = 7,
        Pine = 8,
        Larch = 9,
        Fir = 10,
        SessileOak = 11,
        TurkeyOak = 12,
        Hazel = 13,
        Hawthorn = 14,
        Blackthorn = 15,
        Elder = 16,
        Juniper = 17
    }

    /// <summary>Silhouette family used by the tree renderer to build a species-specific model.</summary>
    enum TreeCrownShape : byte
    {
        /// <summary>Narrow, many-layered spire (spruce).</summary>
        Spire,
        /// <summary>Small, light, roughly spherical broadleaf crown (birch).</summary>
        Rounded,
        /// <summary>Wide, flattened, heavy broadleaf crown (oak).</summary>
        Broad,
        /// <summary>Tall egg-shaped broadleaf crown (beech).</summary>
        Ovoid
    }

    readonly record struct ForestSpeciesProfile(
        float MaximumAgeYears,
        float MatureAgeYears,
        float MaximumBiomass,
        float AnnualGrowthRate,
        float PreferredMoisture,
        float MoistureTolerance,
        float PreferredElevation,
        float ElevationTolerance,
        // 0 = light demanding pioneer, 1 = grows and regenerates happily under a closed canopy.
        float ShadeTolerance,
        // Recoverable roundwood per biomass unit, relative to spruce pulpwood.
        float WoodDensity,
        TreeCrownShape CrownShape)
    {
        /// <summary>Biomass a fully stocked neighbouring tile is assumed to carry; used to normalise crowding.</summary>
        public const float ReferenceCanopyBiomass = 1.2f;

        public static ForestSpeciesProfile For(ForestSpecies species) => species switch
        {
            ForestSpecies.Spruce => new ForestSpeciesProfile(
                // Widened site range: with the old narrow optimum spruce died out of every
                // lowland map within a decade, so conifers were effectively unplantable there.
                190f, 30f, 1.15f, 0.065f, 0.70f, 0.34f, 0.60f, 0.45f, 0.70f, 0.92f, TreeCrownShape.Spire),
            ForestSpecies.Birch => new ForestSpeciesProfile(
                90f, 15f, 0.72f, 0.105f, 0.64f, 0.30f, 0.45f, 0.50f, 0.10f, 1.12f, TreeCrownShape.Rounded),
            ForestSpecies.Oak => new ForestSpeciesProfile(
                320f, 45f, 1.35f, 0.045f, 0.55f, 0.30f, 0.32f, 0.40f, 0.45f, 1.45f, TreeCrownShape.Broad),
            ForestSpecies.Beech => new ForestSpeciesProfile(
                240f, 40f, 1.30f, 0.052f, 0.70f, 0.26f, 0.55f, 0.36f, 0.90f, 1.32f, TreeCrownShape.Ovoid),
            // Sycamore maple: fast, moderately shade tolerant, fresh montane soils.
            ForestSpecies.Maple => new ForestSpeciesProfile(
                250f, 35f, 1.20f, 0.060f, 0.65f, 0.28f, 0.50f, 0.40f, 0.55f, 1.25f, TreeCrownShape.Rounded),
            // Ash: light-demanding, wants fresh, base-rich soil; fast and straight.
            ForestSpecies.Ash => new ForestSpeciesProfile(
                250f, 35f, 1.25f, 0.070f, 0.70f, 0.22f, 0.35f, 0.35f, 0.30f, 1.35f, TreeCrownShape.Ovoid),
            // Scots pine: pioneer of poor, dry ground; very light-demanding.
            ForestSpecies.Pine => new ForestSpeciesProfile(
                300f, 30f, 1.00f, 0.060f, 0.40f, 0.40f, 0.45f, 0.50f, 0.12f, 1.00f, TreeCrownShape.Broad),
            // European larch: deciduous conifer of the high ground, light-demanding.
            ForestSpecies.Larch => new ForestSpeciesProfile(
                300f, 35f, 1.00f, 0.075f, 0.50f, 0.30f, 0.80f, 0.30f, 0.10f, 1.05f, TreeCrownShape.Spire),
            // Silver fir: the most shade-tolerant conifer, slow, long-lived, wet cool slopes.
            ForestSpecies.Fir => new ForestSpeciesProfile(
                400f, 45f, 1.40f, 0.050f, 0.75f, 0.25f, 0.60f, 0.35f, 0.95f, 0.95f, TreeCrownShape.Spire),
            // Sessile oak: the drier, warmer-slope oak; straighter bole than pedunculate oak.
            ForestSpecies.SessileOak => new ForestSpeciesProfile(
                400f, 50f, 1.35f, 0.042f, 0.45f, 0.35f, 0.38f, 0.40f, 0.45f, 1.45f, TreeCrownShape.Broad),
            // Turkey oak: warm, dry lowland oak; quicker and shorter-lived than the others.
            ForestSpecies.TurkeyOak => new ForestSpeciesProfile(
                200f, 35f, 1.15f, 0.055f, 0.35f, 0.35f, 0.20f, 0.30f, 0.35f, 1.35f, TreeCrownShape.Broad),
            // Shrubs: small, fast to mature, low biomass, many stems per tile.
            ForestSpecies.Hazel => new ForestSpeciesProfile(
                80f, 10f, 0.25f, 0.100f, 0.60f, 0.35f, 0.40f, 0.50f, 0.60f, 0.60f, TreeCrownShape.Rounded),
            ForestSpecies.Hawthorn => new ForestSpeciesProfile(
                150f, 15f, 0.30f, 0.070f, 0.45f, 0.40f, 0.40f, 0.50f, 0.40f, 0.70f, TreeCrownShape.Rounded),
            ForestSpecies.Blackthorn => new ForestSpeciesProfile(
                60f, 8f, 0.20f, 0.090f, 0.35f, 0.40f, 0.30f, 0.50f, 0.15f, 0.55f, TreeCrownShape.Rounded),
            ForestSpecies.Elder => new ForestSpeciesProfile(
                60f, 8f, 0.22f, 0.120f, 0.65f, 0.30f, 0.35f, 0.50f, 0.50f, 0.45f, TreeCrownShape.Rounded),
            ForestSpecies.Juniper => new ForestSpeciesProfile(
                200f, 20f, 0.25f, 0.030f, 0.35f, 0.45f, 0.45f, 0.55f, 0.20f, 0.70f, TreeCrownShape.Spire),
            _ => default
        };
    }
}
