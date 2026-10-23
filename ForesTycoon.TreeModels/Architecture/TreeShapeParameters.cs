using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.TreeModels.Generation.Dendro.Params;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// The tree-generator parameters a designer tunes, in plain words and grouped by the part of the tree they shape, plus
    /// the rest of the generator's parameters for fine work. Authored values are kept within the generator's safe range,
    /// so a form can never stop a tree from being built.
    /// </summary>
    internal static class TreeShapeParameters
    {
        /// <param name="Options">Names of the values of a choice parameter (index = value); null for numbers.</param>
        internal sealed record Entry(string Name, string Label, string Group, double Min, double Max, bool Integer, string Help,
            string[] Options = null);

        internal static readonly string[] Groups = { "Korona", "Törzs", "Vázágak", "Mellékágak", "Lombozat" };

        private static readonly string[] Shapes =
            { "Kúpos", "Gömb", "Félgömb", "Henger", "Kúposodó henger", "Láng", "Fordított kúp", "Lángszerű", "Burok" };

        /// <summary>The tuned parameters, in display order.</summary>
        internal static readonly Entry[] Common =
        {
            new("Shape", "Koronaforma", "Korona", 0, 8, true, "Merre hosszabbak a vázágak: ez adja a korona körvonalát.", Shapes),
            new("BaseSize", "Ágmentes törzs aránya", "Korona", 0, .95, false,
                "A magasság mekkora része ágmentes. A játék a fénytől és a kortól függően ehhez igazítja; életfázisonként megadva az a végső érték."),
            new("AttractionUp", "Felfelé törekvés", "Korona", -2, 3, false, "Pozitív: felfelé ívelő ágak. Negatív: lehajló, csüngő ágak."),
            new("Levels", "Ágazási szintek", "Korona", 1, 4, true, "Hány szinten ágazik a fa (törzs, vázág, mellékág, gally)."),
            new("Ratio", "Törzsvastagság", "Törzs", .005, .06, false, "A törzs vastagsága a magassághoz képest."),
            new("RatioPower", "Ágak vékonyodása", "Törzs", .5, 3, false, "Mennyivel vékonyabb egy ág a szülőjénél."),
            new("Flare", "Tőterpesz", "Törzs", 0, 2, false, "A törzs kiszélesedése a talajnál."),
            new("0Taper", "Törzs elvékonyodása", "Törzs", 0, 2, false, "0: hengeres törzs, 1: a csúcsig egyenletesen vékonyodik."),
            new("0BaseSplits", "Tőből induló törzsek", "Törzs", 0, 8, true, "Hány törzsre válik szét a fa már a tőnél."),
            new("0SegSplits", "Törzs villásodása", "Törzs", 0, 1.5, false, "Milyen gyakran ágazik kétfelé a törzs."),
            new("0SplitAngle", "Villásodás szöge", "Törzs", 0, 60, false, "Mennyire nyílnak szét a villás törzsrészek (fok)."),
            new("0CurveV", "Törzs kanyargóssága", "Törzs", 0, 150, false, "Mennyire görbe, rendezetlen a törzs vonala (fok)."),
            new("1Branches", "Vázágak száma", "Vázágak", 1, 150, true, "A törzsből induló főágak száma."),
            new("1Length", "Vázágak hossza", "Vázágak", .02, 1.2, false, "A főágak hossza a törzshöz képest."),
            new("1LengthV", "Hosszuk szórása", "Vázágak", 0, .6, false, "Mennyire különböznek egymástól a főágak hosszai."),
            new("1DownAngle", "Kiágazási szög", "Vázágak", 0, 150, false, "A főágak szöge a törzshöz képest (fok): kicsi = meredek, 90 = vízszintes."),
            new("1DownAngleV", "Szög változása felfelé", "Vázágak", -90, 90, false, "Negatív: a felső ágak meredekebbek, mint az alsók."),
            new("1Rotate", "Elfordulás ágak között", "Vázágak", -360, 360, false, "Az egymást követő főágak elfordulása a törzs körül (fok)."),
            new("1Curve", "Ágak ívelése", "Vázágak", -150, 150, false, "Pozitív: felfelé, negatív: lefelé hajlanak a főágak (fok)."),
            new("1CurveV", "Ágak kanyargóssága", "Vázágak", 0, 250, false, "Mennyire szabálytalan a főágak vonala (fok)."),
            new("1SegSplits", "Vázágak villásodása", "Vázágak", 0, 1.5, false, "Milyen gyakran ágaznak kétfelé a főágak."),
            new("2Branches", "Mellékágak száma", "Mellékágak", 0, 80, true, "Egy főágon növő mellékágak száma."),
            new("2Length", "Mellékágak hossza", "Mellékágak", .02, 1.2, false, "A mellékágak hossza a főághoz képest."),
            new("2DownAngle", "Kiágazási szög", "Mellékágak", 0, 150, false, "A mellékágak szöge a főághoz képest (fok)."),
            new("2Curve", "Ágak ívelése", "Mellékágak", -150, 150, false, "Pozitív: felfelé, negatív: lefelé hajlanak (fok)."),
            new("2CurveV", "Ágak kanyargóssága", "Mellékágak", 0, 250, false, "Mennyire szabálytalan a mellékágak vonala (fok)."),
            new("Leaves", "Lombminták ágvégenként", "Lombozat", 0, 40, true, "Hány lombpont ül egy ágvégen; a korona burka ezekből épül."),
            new("LeafScale", "Lombfoltok mérete", "Lombozat", .02, 1, false, "A lombpontok mérete; nagyobb értéknél tömörebb a korona."),
        };

        // The generator's own description of its parameters; read only.
        private static readonly IReadOnlyDictionary<string, AbstractParam> Database = new TreeParams().ParamDb;

        /// <summary>Every numeric generator parameter the tuned list does not cover, by level, with its own description.</summary>
        internal static readonly Entry[] Advanced = Database.Values
            .Where(p => p is IntParam or FloatParam && !Common.Any(c => c.Name == p.Name) && p.Name is not ("Scale" or "ScaleV" or "0Scale" or "0ScaleV"))
            .OrderBy(p => p.Level == AbstractParam.General ? -1 : p.Level).ThenBy(p => p.Order)
            .Select(p => {
                var range = ParameterLimits.GetEffectiveRange(p);
                // Counts and resolutions multiply the generator's work; keep them where a game tree stays cheap.
                double cap = p.Name.EndsWith("Branches", StringComparison.Ordinal) ? 150 : p.Name.EndsWith("CurveRes", StringComparison.Ordinal) ? 30 : 1e4;
                return new Entry(p.Name, p.Name, p.Level == AbstractParam.General ? "Általános" : p.Level + ". szint",
                    Math.Max(range.Min, -1e4), Math.Min(range.Max, cap), p is IntParam, p.ShortDesc);
            }).ToArray();

        internal static Entry Find(string name) => Common.FirstOrDefault(e => e.Name == name) ?? Advanced.FirstOrDefault(e => e.Name == name);
        internal static bool Known(string name) => Find(name) != null;

        /// <summary>The value of a parameter in <paramref name="values"/>, or the generator's default when the set leaves it out.</summary>
        internal static double ValueIn(IReadOnlyDictionary<string, double> values, string name) =>
            values.TryGetValue(name, out double v) ? v
            : Database.TryGetValue(name, out var p) && double.TryParse(p.GetDefaultValue(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;

        /// <summary>
        /// Puts authored values over <paramref name="values"/>, each clamped to the generator's safe range and to its practical
        /// range: the generator accepts nine levels or thousands of limbs, a game tree does not.
        /// </summary>
        internal static void Apply(Dictionary<string, double> values, IReadOnlyDictionary<string, double> authored)
        {
            foreach (var (name, value) in authored) {
                if (!Database.TryGetValue(name, out var param) || !double.IsFinite(value)) continue;
                var range = ParameterLimits.GetEffectiveRange(param);
                double v = Math.Clamp(value, range.Min, range.Max);
                if (Find(name) is { } entry) v = Math.Clamp(v, entry.Min, entry.Max);
                values[name] = param is IntParam ? Math.Round(v) : v;
            }
        }
    }
}
