using System;
using System.Drawing;

namespace ForesTycoon
{
    /// <summary>Growth form of a species in the forest simulation and the generator.</summary>
    internal enum ForestGrowthForm : byte { Tree, Shrub }

    /// <summary>
    /// Everything that distinguishes one species besides its ecology (<see cref="ForestSpeciesProfile"/>):
    /// names, physical size envelope, crown proportions, appearance and the Arbaro parameter file the
    /// generator builds it from. One row per species replaces scattered switches.
    /// </summary>
    /// <param name="MatureHeight">Height (m) a well-grown mature individual approaches.</param>
    /// <param name="MatureDiameter">Breast-height diameter (m) at maturity.</param>
    /// <param name="MaxHeight">Height envelope (m) the growth rate falls to zero at.</param>
    /// <param name="CrownRatio">Crown radius as a share of height.</param>
    /// <param name="CrownSpread">Crown radius per metre of trunk diameter once thickening dominates.</param>
    /// <param name="RadialRate">Radius growth per growth unit (m).</param>
    /// <param name="HeightRate">Height growth per growth unit (m).</param>
    /// <param name="Capacity">Most individuals a fully stocked tile carries.</param>
    /// <param name="DrySensitivity">Exponent on water supply: lower is more drought tolerant.</param>
    /// <param name="Pattern">Surface shader family (1..5) the bark and foliage borrow their texture from.</param>
    internal readonly record struct ForestSpeciesTraits(
        string Name, string Latin, string Preset, ForestGrowthForm Form, bool Conifer, bool Evergreen,
        float MatureHeight, float MatureDiameter, float MaxHeight, float CrownRatio, float CrownSpread,
        float RadialRate, float HeightRate, int Capacity, float DrySensitivity, int Pattern,
        Color Bark, Color Crown, string Description)
    {
        internal bool Shrub => Form == ForestGrowthForm.Shrub;

        private static readonly ForestSpeciesTraits[] Table = Build();

        internal static ForestSpeciesTraits For(ForestSpecies species) =>
            (int)species < Table.Length && Table[(int)species].Preset != null ? Table[(int)species] : Table[(int)ForestSpecies.Beech];

        /// <summary>Real species in menu order: classic four, other trees, shrubs.</summary>
        internal static readonly ForestSpecies[] Playable =
        {
            ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech,
            ForestSpecies.Maple, ForestSpecies.Ash, ForestSpecies.SessileOak, ForestSpecies.TurkeyOak,
            ForestSpecies.Pine, ForestSpecies.Larch, ForestSpecies.Fir,
            ForestSpecies.Hazel, ForestSpecies.Hawthorn, ForestSpecies.Blackthorn, ForestSpecies.Elder, ForestSpecies.Juniper
        };

        private static ForestSpeciesTraits[] Build()
        {
            var t = new ForestSpeciesTraits[18];
            void Add(ForestSpecies s, string name, string latin, string preset, ForestGrowthForm form, bool conifer, bool evergreen,
                float height, float diameter, float maxHeight, float crownRatio, float spread, float radial, float heightRate,
                int capacity, float dry, int pattern, Color bark, Color crown, string description) =>
                t[(int)s] = new(name, latin, preset, form, conifer, evergreen, height, diameter, maxHeight, crownRatio, spread,
                    radial, heightRate, capacity, dry, pattern, bark, crown, description);
            const ForestGrowthForm Tree = ForestGrowthForm.Tree, Shrub = ForestGrowthForm.Shrub;
            // The four original species keep exactly the numbers they had before the catalog existed.
            Add(ForestSpecies.Spruce, "Lucfenyő", "Picea abies", "picea_abies", Tree, true, true, 34, 0.52f, 40, 0.26f, 6, 0.0050f, 0.9f, 14, 1.0f, 2,
                Color.FromArgb(74, 54, 38), Color.FromArgb(48, 79, 43), "Gyorsan nő, árnyéktűrő, sűrű állomány. Nedves, hűvös termőhely.");
            Add(ForestSpecies.Birch, "Nyír", "Betula pendula", "betula_pendula", Tree, false, false, 24, 0.30f, 28, 0.16f, 6, 0.0055f, 0.9f, 12, 0.85f, 3,
                Color.FromArgb(208, 208, 196), Color.FromArgb(119, 150, 57), "Úttörő fafaj: gyors kezdés, rövidebb élet, fényigényes.");
            Add(ForestSpecies.Oak, "Tölgy", "Quercus robur", "quercus_robur", Tree, false, false, 27, 0.70f, 35, 0.28f, 9, 0.0045f, 0.9f, 4, 0.6f, 4,
                Color.FromArgb(110, 84, 54), Color.FromArgb(78, 110, 46), "Lassú, hosszú életű, értékes faanyag. Szárazságtűrő.");
            Add(ForestSpecies.Beech, "Bükk", "Fagus sylvatica", "fagus_sylvatica", Tree, false, false, 31, 0.56f, 38, 0.22f, 6, 0.0050f, 0.9f, 7, 0.85f, 5,
                Color.FromArgb(146, 134, 116), Color.FromArgb(92, 126, 50), "Árnyéktűrő, zárt lombkorona, közepes növekedés.");
            Add(ForestSpecies.Maple, "Hegyi juhar", "Acer pseudoplatanus", "acer_pseudoplatanus", Tree, false, false, 32, 0.60f, 36, 0.24f, 7, 0.0050f, 0.9f, 6, 0.85f, 5,
                Color.FromArgb(128, 116, 100), Color.FromArgb(84, 130, 52), "Gyors, mérsékelten árnyéktűrő; friss, hegyvidéki talajok. Értékes bútorfa.");
            Add(ForestSpecies.Ash, "Magas kőris", "Fraxinus excelsior", "fraxinus_excelsior", Tree, false, false, 32, 0.55f, 38, 0.20f, 6, 0.0050f, 0.9f, 6, 0.85f, 4,
                Color.FromArgb(120, 112, 98), Color.FromArgb(104, 140, 56), "Fényigényes, egyenes, gyors; friss, tápanyagdús talajt kíván.");
            Add(ForestSpecies.SessileOak, "Kocsánytalan tölgy", "Quercus petraea", "quercus_petraea", Tree, false, false, 30, 0.65f, 36, 0.26f, 8, 0.0045f, 0.9f, 5, 0.6f, 4,
                Color.FromArgb(104, 82, 56), Color.FromArgb(74, 108, 46), "Egyenesebb törzsű, szárazabb lejtők tölgye; lassú, értékes.");
            Add(ForestSpecies.TurkeyOak, "Csertölgy", "Quercus cerris", "quercus_cerris", Tree, false, false, 28, 0.55f, 32, 0.27f, 8, 0.0050f, 0.9f, 5, 0.6f, 4,
                Color.FromArgb(92, 74, 56), Color.FromArgb(88, 116, 48), "Meleg, száraz dombvidék fája; gyorsabb, rövidebb életű tölgy.");
            Add(ForestSpecies.Pine, "Erdeifenyő", "Pinus sylvestris", "pinus_sylvestris", Tree, true, true, 30, 0.45f, 34, 0.20f, 6, 0.0045f, 0.9f, 10, 0.5f, 2,
                Color.FromArgb(132, 84, 52), Color.FromArgb(62, 96, 52), "Fényigényes úttörő szegény, száraz talajon; lapos, magas korona.");
            Add(ForestSpecies.Larch, "Vörösfenyő", "Larix decidua", "larix_decidua", Tree, true, false, 36, 0.50f, 42, 0.15f, 5, 0.0050f, 0.9f, 10, 0.85f, 2,
                Color.FromArgb(102, 76, 56), Color.FromArgb(104, 140, 66), "Lombhullató fenyő: ősszel aranysárgára vált, télen csupasz. Magasabb hegyvidék.");
            Add(ForestSpecies.Fir, "Jegenyefenyő", "Abies alba", "abies_alba", Tree, true, true, 42, 0.70f, 48, 0.20f, 6, 0.0055f, 0.9f, 10, 1.0f, 5,
                Color.FromArgb(122, 116, 106), Color.FromArgb(38, 74, 48), "Rendkívül árnyéktűrő, lassú, hosszú életű; nedves, hűvös lejtők.");
            Add(ForestSpecies.Hazel, "Mogyoró", "Corylus avellana", "corylus_avellana", Shrub, false, false, 5, 0.09f, 6.5f, 0.38f, 6, 0.0012f, 0.35f, 20, 0.85f, 5,
                Color.FromArgb(128, 98, 72), Color.FromArgb(104, 146, 62), "Tőből sarjadó cserje; árnyéktűrő aljnövényzet, gyorsan érik.");
            Add(ForestSpecies.Hawthorn, "Galagonya", "Crataegus monogyna", "crataegus_monogyna", Shrub, false, false, 6, 0.14f, 7.5f, 0.42f, 6, 0.0015f, 0.35f, 16, 0.7f, 4,
                Color.FromArgb(104, 96, 86), Color.FromArgb(66, 104, 50), "Tövises, sűrű bokor; erdőszéleken és legelőkön.");
            Add(ForestSpecies.Blackthorn, "Kökény", "Prunus spinosa", "prunus_spinosa", Shrub, false, false, 3.5f, 0.06f, 4.5f, 0.45f, 6, 0.0010f, 0.30f, 24, 0.6f, 4,
                Color.FromArgb(70, 60, 52), Color.FromArgb(70, 112, 56), "Sűrű, tövises bozót; fényigényes, száraz talajt is elvisel.");
            Add(ForestSpecies.Elder, "Fekete bodza", "Sambucus nigra", "sambucus_nigra", Shrub, false, false, 5, 0.12f, 6.5f, 0.40f, 6, 0.0020f, 0.40f, 18, 0.9f, 4,
                Color.FromArgb(124, 112, 96), Color.FromArgb(92, 132, 58), "Gyors, rövid életű cserje; nitrogénben gazdag, nedves talajon.");
            Add(ForestSpecies.Juniper, "Boróka", "Juniperus communis", "juniperus_communis", Shrub, true, true, 4, 0.10f, 6, 0.30f, 4, 0.0010f, 0.25f, 18, 0.5f, 2,
                Color.FromArgb(110, 82, 62), Color.FromArgb(70, 98, 70), "Örökzöld, lassú cserje; szegény, száraz legelők és erdőszélek.");
            return t;
        }
    }
}
