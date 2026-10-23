using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ForesTycoon.Ecology
{
    /// <summary>Editable numbers of one species; null keeps the built-in value.</summary>
    internal sealed record ForestSpeciesEdit(
        string Name = null, string Description = null,
        float? MaximumAgeYears = null, float? MatureAgeYears = null, float? MaximumBiomass = null, float? AnnualGrowthRate = null,
        float? PreferredMoisture = null, float? MoistureTolerance = null, float? PreferredElevation = null, float? ElevationTolerance = null,
        float? ShadeTolerance = null, float? WoodDensity = null, float? DrySensitivity = null, float? MatureHeight = null,
        float? MaxHeight = null, int? Capacity = null, float? TimberPrice = null, TreeShapeOverride Shape = null, TreeLook Look = null);

    /// <summary>
    /// Authored colours of a species (null keeps the built-in one) and its bark pattern (the surface shader's family code).
    /// Summer, bark and pattern replace the species traits; spring and autumn are the colours the crown reaches.
    /// </summary>
    internal sealed record TreeLook(System.Drawing.Color? Spring, System.Drawing.Color? Summer, System.Drawing.Color? Autumn,
        System.Drawing.Color? Bark, System.Drawing.Color? Dead, int? Pattern)
    {
        internal string Revision => string.Join(",", new[] { Spring, Summer, Autumn, Bark, Dead }.Select(c => c?.ToArgb().ToString("x") ?? "-")) + "," + Pattern;
    }

    /// <summary>
    /// Tree-generator parameters authored for a species: <see cref="All"/> in every life phase, then a phase's own set, then
    /// <see cref="Dead"/> for a dead tree. <see cref="Revision"/> names the content, so caches keyed by it rebuild when the
    /// form changes.
    /// </summary>
    internal sealed record TreeShapeOverride(IReadOnlyDictionary<string, double> All,
        IReadOnlyDictionary<TreeLifePhase, IReadOnlyDictionary<string, double>> Phases, IReadOnlyDictionary<string, double> Dead, string Revision)
    {
        internal static TreeShapeOverride Of(IReadOnlyDictionary<string, double> all,
            IReadOnlyDictionary<TreeLifePhase, IReadOnlyDictionary<string, double>> phases, IReadOnlyDictionary<string, double> dead = null)
        {
            string Text(IReadOnlyDictionary<string, double> values) => string.Join(";", System.Linq.Enumerable.Select(
                System.Linq.Enumerable.OrderBy(values, v => v.Key, StringComparer.Ordinal),
                v => v.Key + "=" + v.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            var revision = new System.Text.StringBuilder(Text(all));
            foreach (var (phase, values) in System.Linq.Enumerable.OrderBy(phases, p => p.Key)) revision.Append('|').Append(phase).Append(':').Append(Text(values));
            if (dead != null) revision.Append("|Dead:").Append(Text(dead));
            return new(all, phases, dead, revision.ToString());
        }
    }

    /// <summary>
    /// The species parameters the running process uses: the built-in tables with the active project's edits on top.
    /// Swapped as one immutable set when a project is applied, so a world update never sees half an edit.
    /// </summary>
    internal static class ForestSpeciesBook
    {
        private sealed record Book(ForestSpeciesProfile[] Profiles, ForestSpeciesTraits[] Traits, float?[] Prices, TreeShapeOverride[] Shapes, TreeLook[] Looks);
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
        /// <summary>The species' authored colours, or null for the built-in look.</summary>
        internal static TreeLook Look(ForestSpecies species)
        {
            var book = ActiveBook;
            return book != null && (int)species < book.Looks.Length ? book.Looks[(int)species] : null;
        }

        /// <summary>A short code of the species' authored form and colours for packed shape keys; 0 is the built-in tree.</summary>
        internal static ushort FormCode(ForestSpecies species)
        {
            var shape = Shape(species); var look = Look(species);
            if (shape == null && look == null) return 0;
            uint hash = 2166136261;
            foreach (char c in (shape?.Revision ?? "") + "#" + (look?.Revision ?? "")) hash = (hash ^ c) * 16777619;
            return (ushort)Math.Max(1, hash & 0xFFFF);
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
                    System.Linq.Enumerable.Select(previous.Shapes, x => x?.Revision))
                && System.Linq.Enumerable.SequenceEqual(next.Looks, previous.Looks)) return;
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
            var shapes = new TreeShapeOverride[count]; var looks = new TreeLook[count];
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
                        MatureHeight = e.MatureHeight ?? t.MatureHeight, MaxHeight = e.MaxHeight ?? t.MaxHeight, Capacity = e.Capacity ?? t.Capacity,
                        Crown = e.Look?.Summer ?? t.Crown, Bark = e.Look?.Bark ?? t.Bark, Pattern = e.Look?.Pattern ?? t.Pattern };
                prices[(int)s] = e.TimberPrice;
                shapes[(int)s] = e.Shape; looks[(int)s] = e.Look;
            }
            return new Book(profiles, traits, prices, shapes, looks);
        }
    }
}
