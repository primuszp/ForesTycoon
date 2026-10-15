using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>
/// How a sentence names a field of the rule's scope: “nedvesség”, its unit, for a yes/no field its two states as
/// sentences (“az út makadám”), for a bool or enum field its options (“aszfalt”, “makadám”), for a parameter its value now.
/// </summary>
public sealed record RuleWord(string Name, string Unit = "", string True = null, string False = null, double? Current = null)
{
    public IReadOnlyList<(double Value, string Name)> Options { get; init; } = Array.Empty<(double, string)>();
}

/// <summary>
/// Reads a visual rule (<see cref="RuleInfluence"/>) aloud in Hungarian, for designers:
/// “Minden út esetén az állapot évente 0,05 értékkel csökken, a nedvesség arányában, ha az út aszfalt.”
/// The text is derived from the saved data only, so it always matches what the game runs.
/// </summary>
public sealed class RuleSentence
{
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");
    private readonly Func<string, RuleWord> word;
    private readonly string element;

    /// <param name="element">What one element of the scope is called (“út”).</param>
    /// <param name="word">Names a field, relation field (“tile.moisture”), action or parameter (“tuning.X”).</param>
    public RuleSentence(string element, Func<string, RuleWord> word)
    {
        this.element = string.IsNullOrWhiteSpace(element) ? "elem" : element;
        this.word = name => word(name) ?? new(name);
    }

    public static string Number(double v) =>
        Math.Abs(v) >= 100 ? v.ToString("#,0.#", Hu) : Math.Abs(v) >= 1 ? v.ToString("0.##", Hu) : v.ToString("0.####", Hu);

    /// <summary>“a” or “az”, as the following word needs.</summary>
    public static string Article(string next)
    {
        if (string.IsNullOrEmpty(next)) return "a";
        char c = char.ToLowerInvariant(next[0]);
        return "aáeéiíoóöőuúüű".IndexOf(c) >= 0 || next.StartsWith("1 ", StringComparison.Ordinal) || next == "1" || next.StartsWith('5') ? "az" : "a";
    }

    private static string Lower(string s) => string.IsNullOrEmpty(s) || s.Length > 1 && char.IsUpper(s[1]) ? s : char.ToLower(s[0], Hu) + s[1..];
    private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Hu) + s[1..];
    private string The(string field) { string n = Lower(word(field).Name); return Article(n) + " " + n; }
    private string Value(double v, string field) { string unit = word(field).Unit; return Number(v) + (string.IsNullOrEmpty(unit) || unit == "0–1" ? "" : " " + unit); }
    private string Parameter(string field)
    {
        var w = word(field);
        return $"„{w.Name}”" + (w.Current is double now ? $" (most {Number(now)}{(string.IsNullOrEmpty(w.Unit) ? "" : " " + w.Unit)})" : "");
    }

    /// <summary>Options of a bool/enum field where <paramref name="holds"/>: “a burkolat makadám”, “az út aszfalt”. Null for a number.</summary>
    private string Options(string field, Func<double, bool> holds)
    {
        var w = word(field);
        if (w.Options.Count == 0) return null;
        var match = w.Options.Where(o => holds(o.Value)).ToArray();
        if (match.Length == w.Options.Count) return "mindig";
        if (match.Length == 0) return "soha";
        if (w.True != null && w.False != null && match.Length == 1) return match[0].Value != 0 ? w.True : w.False;
        return $"{The(field)} {string.Join(" vagy ", match.Select(o => o.Name))}";
    }

    /// <summary>One condition: “az út aszfalt”, “az állásidő legalább 5”.</summary>
    public string Condition(InfluenceCondition c)
    {
        if (c.ValueField.Length == 0) {
            double v = c.Value;
            string named = Options(c.Field, x => c.Comparison switch {
                "less" => x < v, "atMost" => x <= v, "greater" => x > v, "equal" => x == v, "notEqual" => x != v, _ => x >= v });
            if (named != null) return named;
        }
        string op = c.Comparison switch { "less" => "kisebb, mint", "atMost" => "legfeljebb", "greater" => "nagyobb, mint",
            "equal" => "pontosan", "notEqual" => "nem", _ => "legalább" };
        string limit = c.ValueField.Length > 0 ? (c.ValueField.StartsWith("tuning.", StringComparison.Ordinal) ? Parameter(c.ValueField) : The(c.ValueField)) : Value(c.Value, c.Field);
        return $"{The(c.Field)} {op} {limit}";
    }

    /// <summary>How a source scales the connection: “a nedvesség arányában”, “minél nagyobb az átmérő, annál gyengébben”.</summary>
    public string Factor(InfluenceFactor f)
    {
        if (f.Shape == "step") {
            if (f.OutputLow == 0 && f.OutputHigh == 1 && Options(f.Source, x => x >= f.InputHigh) is { } above) return "ha " + above;
            if (f.OutputLow == 1 && f.OutputHigh == 0 && Options(f.Source, x => x < f.InputHigh) is { } below) return "ha " + below;
            if (f.OutputLow == 0 && f.OutputHigh == 1) return $"ha {The(f.Source)} eléri a(z) {Value(f.InputHigh, f.Source)} értéket";
            if (f.OutputLow == 1 && f.OutputHigh == 0) return $"ha {The(f.Source)} {Value(f.InputHigh, f.Source)} alatt van";
            return $"{The(f.Source)} {Value(f.InputHigh, f.Source)} alatt {Number(f.OutputLow)}×, felette {Number(f.OutputHigh)}× erővel";
        }
        var curve = f.ToCurve();
        string range = $" ({Value(curve.Points[0].X, f.Source)}: {Number(curve.Points[0].Y)}×, {Value(curve.Points[^1].X, f.Source)}: {Number(curve.Points[^1].Y)}×)";
        if (f.Shape == "points") return $"{The(f.Source)} görbéje szerint{range}";
        bool identity = curve.Points[0] == (0, 0) && curve.Points[^1] == (1, 1);
        int sign = curve.Sign;
        if (f.Shape == "linear" && sign > 0 && curve.Points[0].Y == 0) return $"{The(f.Source)} arányában" + (identity ? "" : range);
        if (sign > 0) return $"minél nagyobb {The(f.Source)}, annál erősebben" + range;
        if (sign < 0) return $"minél nagyobb {The(f.Source)}, annál gyengébben" + range;
        return $"{The(f.Source)} függvényében" + range;
    }

    private string Amount(InfluenceTerm t, string target) =>
        t.AmountField.Length > 0 ? Parameter(t.AmountField) : Value(t.Amount, target);

    /// <summary>The connection, without the “Minden út esetén” opening.</summary>
    public string Clause(RuleEffect effect, InfluenceTerm t, bool perYear)
    {
        var parts = new List<string>();
        string target = The(effect.Target);
        if (effect.Mode == "action") parts.Add($"megtörténik {target}");
        else if (effect.Mode == "set") parts.Add($"{target} értéke {Amount(t, effect.Target)} lesz");
        else parts.Add($"{target} {(perYear ? "évente" : "lépésenként")} {Amount(t, effect.Target)} értékkel {(t.Decrease ? "csökken" : "nő")}");
        // Conditions that gate the whole connection read last, as “ha …”; factors read as manners.
        var gates = t.Factors.Where(f => f.Shape == "step").Select(Factor).ToList();
        parts.AddRange(t.Factors.Where(f => f.Shape != "step").Select(Factor));
        var conditions = gates.Select(g => g.StartsWith("ha ", StringComparison.Ordinal) ? g[3..] : g).Concat(t.Conditions.Select(Condition)).ToList();
        string text = string.Join(", ", parts);
        if (conditions.Count > 0) text += ", ha " + string.Join(" és ", conditions);
        return text;
    }

    /// <summary>One connection as a full sentence.</summary>
    public string Term(RuleEffect effect, InfluenceTerm t) =>
        (t.Enabled ? "" : "(Kikapcsolva) ") + Capital($"minden {element} esetén {Clause(effect, t, effect.Influence?.PerYear ?? false)}.");

    /// <summary>The whole rule: one line per connection, then how they combine.</summary>
    public IReadOnlyList<string> Rule(WorldRule rule)
    {
        var lines = new List<string>();
        if (!rule.Enabled) lines.Add("A szabály ki van kapcsolva, a játék nem futtatja.");
        foreach (var effect in rule.Effects.Where(e => e.Influence != null)) {
            var terms = effect.Influence.Terms;
            if (terms.Count == 0) { lines.Add(Capital($"{The(effect.Target)}: még nincs kapcsolat, nem változik.")); continue; }
            foreach (var t in terms) lines.Add(Term(effect, t));
            if (effect.Mode == "add" && terms.Count(t => t.Enabled) > 1)
                lines.Add(Capital($"{The(effect.Target)} változása a(z) {terms.Count(t => t.Enabled)} kapcsolat összege."));
            if (effect.Mode == "action") lines.Add(Capital($"{The(effect.Target)} után az elem további hatásai nem futnak."));
        }
        return lines;
    }
}
