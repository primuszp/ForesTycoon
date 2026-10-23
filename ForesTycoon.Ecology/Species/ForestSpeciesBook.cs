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
        float? MaxHeight = null, int? Capacity = null, float? TimberPrice = null, TreeShapeOverride Shape = null);

    /// <summary>
    /// Tree-generator parameters authored for a species: <see cref="All"/> in every life phase, then a phase's own set.
    /// <see cref="Revision"/> names the content, so caches keyed by it rebuild when the form changes.
    /// </summary>
    internal sealed record TreeShapeOverride(IReadOnlyDictionary<string, double> All,
        IReadOnlyDictionary<TreeLifePhase, IReadOnlyDictionary<string, double>> Phases, string Revision)
    {
        /// <summary>A short, stable code of the revision for packed shape keys; 0 is the built-in form.</summary>
        internal ushort Code
        {
            get {
                uint hash = 2166136261;
                foreach (char c in Revision) hash = (hash ^ c) * 16777619;
                return (ushort)Math.Max(1, hash & 0xFFFF);
            }
        }

        internal static TreeShapeOverride Of(IReadOnlyDictionary<string, double> all,
            IReadOnlyDictionary<TreeLifePhase, IReadOnlyDictionary<string, double>> phases)
        {
            string Text(IReadOnlyDictionary<string, double> values) => string.Join(";", System.Linq.Enumerable.Select(
                System.Linq.Enumerable.OrderBy(values, v => v.Key, StringComparer.Ordinal),
                v => v.Key + "=" + v.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            var revision = new System.Text.StringBuilder(Text(all));
            foreach (var (phase, values) in System.Linq.Enumerable.OrderBy(phases, p => p.Key)) revision.Append('|').Append(phase).Append(':').Append(Text(values));
            return new(all, phases, revision.ToString());
        }
    }

    /// <summary>
    /// The species parameters the running process uses: the built-in tables with the active project's edits on top.
    /// Swapped as one immutable set when a project is applied, so a world update never sees half an edit.
    /// </summary>
    internal static class ForestSpeciesBook
    {
        private sealed record Book(ForestSpeciesProfile[] Profiles, ForestSpeciesTraits[] Traits, float?[] Prices, TreeShapeOverride[] Shapes);
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
        /// <summary>The species' authored tree form, or null for the built-in architecture.</summary>
        internal static TreeShapeOverride Shape(ForestSpecies species)
        {
            var book = ActiveBook;
            return book != null && (int)species < book.Shapes.Length ? book.Shapes[(int)species] : null;
        }
        internal static float? TimberPrice(ForestSpecies species)
        {
            var book = ActiveBook;
            return book != null && (int)species < book.Prices.Length ? book.Prices[(int)species] : null;
        }

        /// <summary>Installs the edits; an empty set restores the built-in tables.</summary>
        internal static void Apply(IReadOnlyDictionary<ForestSpecies, ForestSpeciesEdit> edits)
        {
            var next = (Book)Compose(edits);
            var previous = ActiveBook;
            if (next != null && previous != null && next.Profiles.AsSpan().SequenceEqual(previous.Profiles)
                && next.Traits.AsSpan().SequenceEqual(previous.Traits) && next.Prices.AsSpan().SequenceEqual(previous.Prices)
                && System.Linq.Enumerable.SequenceEqual(System.Linq.Enumerable.Select(next.Shapes, x => x?.Revision),
                    System.Linq.Enumerable.Select(previous.Shapes, x => x?.Revision))) return;
            InstallBook(next);
        }

        /// <summary>
        /// The species set of the edits without installing it: a context for <see cref="WithContext"/>, as the editor's
        /// previews use it. Null means the built-in tables.
        /// </summary>
        internal static object Compose(IReadOnlyDictionary<ForestSpecies, ForestSpeciesEdit> edits)
        {
            if (edits == null || edits.Count == 0) return null;
            int count = Enum.GetValues<ForestSpecies>().Length + 2;
            var profiles = new ForestSpeciesProfile[count]; var traits = new ForestSpeciesTraits[count]; var prices = new float?[count];
            var shapes = new TreeShapeOverride[count];
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
                shapes[(int)s] = e.Shape;
            }
            return new Book(profiles, traits, prices, shapes);
        }
    }
}
