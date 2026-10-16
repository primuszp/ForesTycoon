using System;
using System.Collections.Generic;
using System.Threading;

namespace ForesTycoon.Ecology
{
    /// <summary>Editable numbers of one species; null keeps the built-in value.</summary>
    internal sealed record ForestSpeciesEdit(
        string Name = null, string Description = null,
        float? MaximumAgeYears = null, float? MatureAgeYears = null, float? MaximumBiomass = null, float? AnnualGrowthRate = null,
        float? PreferredMoisture = null, float? MoistureTolerance = null, float? PreferredElevation = null, float? ElevationTolerance = null,
        float? ShadeTolerance = null, float? WoodDensity = null, float? DrySensitivity = null, float? MatureHeight = null,
        float? MaxHeight = null, int? Capacity = null, float? TimberPrice = null);

    /// <summary>
    /// The species parameters the running process uses: the built-in tables with the active project's edits on top.
    /// Swapped as one immutable set when a project is applied, so a world update never sees half an edit.
    /// </summary>
    internal static class ForestSpeciesBook
    {
        private sealed record Book(ForestSpeciesProfile[] Profiles, ForestSpeciesTraits[] Traits, float?[] Prices);
        private static volatile Book current;
        private sealed record WorkerContext(Book Book);
        private static readonly AsyncLocal<WorkerContext> workerContext = new();
        private static Book ActiveBook => workerContext.Value is { } context ? context.Book : current;
        internal static T WithContext<T>(object context, Func<T> compute)
        {
            var previous = workerContext.Value;
            try { workerContext.Value = new((Book)context); return compute(); }
            finally { workerContext.Value = previous; }
        }

        internal static object CaptureContext() => ActiveBook;
        internal static void RestoreContext(object context) => InstallBook((Book)context);
        private static void InstallBook(Book book)
        {
            if (workerContext.Value != null) workerContext.Value = new(book);
            else current = book;
        }

        internal static bool TryProfile(ForestSpecies species, out ForestSpeciesProfile profile)
        {
            var book = ActiveBook;
            if (book != null && (int)species < book.Profiles.Length) { profile = book.Profiles[(int)species]; return true; }
            profile = default; return false;
        }
        internal static bool TryTraits(ForestSpecies species, out ForestSpeciesTraits traits)
        {
            var book = ActiveBook;
            if (book != null && (int)species < book.Traits.Length && book.Traits[(int)species].Preset != null) { traits = book.Traits[(int)species]; return true; }
            traits = default; return false;
        }
        internal static float? TimberPrice(ForestSpecies species)
        {
            var book = ActiveBook;
            return book != null && (int)species < book.Prices.Length ? book.Prices[(int)species] : null;
        }

        /// <summary>Installs the edits; an empty set restores the built-in tables.</summary>
        internal static void Apply(IReadOnlyDictionary<ForestSpecies, ForestSpeciesEdit> edits)
        {
            if (edits == null || edits.Count == 0) { InstallBook(null); return; }
            int count = Enum.GetValues<ForestSpecies>().Length + 2;
            var profiles = new ForestSpeciesProfile[count]; var traits = new ForestSpeciesTraits[count]; var prices = new float?[count];
            foreach (ForestSpecies s in Enum.GetValues<ForestSpecies>()) {
                profiles[(int)s] = ForestSpeciesProfile.Builtin(s);
                traits[(int)s] = ForestSpeciesTraits.Builtin(s);
            }
            foreach (var (s, e) in edits) {
                var p = profiles[(int)s];
                profiles[(int)s] = p with {
                    MaximumAgeYears = e.MaximumAgeYears ?? p.MaximumAgeYears, MatureAgeYears = e.MatureAgeYears ?? p.MatureAgeYears,
                    MaximumBiomass = e.MaximumBiomass ?? p.MaximumBiomass, AnnualGrowthRate = e.AnnualGrowthRate ?? p.AnnualGrowthRate,
                    PreferredMoisture = e.PreferredMoisture ?? p.PreferredMoisture, MoistureTolerance = e.MoistureTolerance ?? p.MoistureTolerance,
                    PreferredElevation = e.PreferredElevation ?? p.PreferredElevation, ElevationTolerance = e.ElevationTolerance ?? p.ElevationTolerance,
                    ShadeTolerance = e.ShadeTolerance ?? p.ShadeTolerance, WoodDensity = e.WoodDensity ?? p.WoodDensity };
                var t = traits[(int)s];
                if (t.Preset != null)
                    traits[(int)s] = t with {
                        Name = e.Name ?? t.Name, Description = e.Description ?? t.Description, DrySensitivity = e.DrySensitivity ?? t.DrySensitivity,
                        MatureHeight = e.MatureHeight ?? t.MatureHeight, MaxHeight = e.MaxHeight ?? t.MaxHeight, Capacity = e.Capacity ?? t.Capacity };
                prices[(int)s] = e.TimberPrice;
            }
            InstallBook(new Book(profiles, traits, prices));
        }
    }
}
