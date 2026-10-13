using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ForesTycoon.Rules;

/// <summary>A built-in curve shape: how an input field turns into an effect. The formula language has it as <c>curve.id(…)</c>.</summary>
public sealed record RuleCurveFamily(string Id, string Name, string Help, int Arguments, string ArgumentHelp, bool OneX = false);

/// <summary>
/// A curve in a rule: <c>curve.family(input; parameters…)</c>, optionally followed by a modifier
/// (<c>* (1 - 0,5 * shadeTolerance) * lépés</c>). The editor edits the shape by its points; the text stays the formula,
/// so a curve and a hand-written formula are the same thing to the game. <see cref="Evaluate"/> matches the compiled graph.
/// </summary>
public sealed class RuleCurve
{
    public static readonly RuleCurveFamily[] Families = {
        new("linear", "Linear", "Straight between A and B, flat outside.", 4, "x0; y0; x1; y1"),
        new("step", "Step", "Below the threshold one value, above it another.", 3, "küszöb; alatta; felette", OneX: true),
        new("smooth", "S-curve", "Soft transition from A to B.", 4, "x0; y0; x1; y1"),
        new("saturate", "Saturating", "Rises fast from A, levels off at B.", 4, "x0; y0; x1; y1"),
        new("decay", "Decaying", "Falls fast from A towards B.", 4, "x0; y0; x1; y1"),
        new("bell", "Optimum", "Largest at A, falls to B's level at B's distance on both sides.", 4, "csúcs x; csúcs; szélesség x; alap"),
        new("points", "Points", "Straight lines through breakpoints, flat outside.", -1, "x1; y1; x2; y2; …"),
    };
    public static RuleCurveFamily Find(string id) => Array.Find(Families, f => f.Id == id);

    public string Family { get; set; } = "linear";
    public string Input { get; set; } = "";
    /// <summary>Breakpoints. Two for most families; one for a step (x, below) plus its value above in <see cref="Above"/>.</summary>
    public List<(double X, double Y)> Points { get; set; } = new() { (0, 0), (1, 1) };
    public double Above { get; set; }
    /// <summary>Text after the curve call, kept as written: “ * (1 - 0,5 * shadeTolerance) * lépés”.</summary>
    public string Tail { get; set; } = "";

    public double Evaluate(double x)
    {
        static double Clamp01(double t) => Math.Clamp(t, 0, 1);
        var p = Points;
        double T() => (x - p[0].X) / (p[1].X - p[0].X);
        double Lerp(double a, double b, double t) => a + (b - a) * t;
        switch (Family) {
            case "step": return x < p[0].X ? p[0].Y : Above;
            case "linear": return Lerp(p[0].Y, p[1].Y, Clamp01(T()));
            case "smooth": { double t = Clamp01(T()); return Lerp(p[0].Y, p[1].Y, t * t * (3 - 2 * t)); }
            case "saturate": return Lerp(p[0].Y, p[1].Y, (1 - Math.Exp(-3 * Math.Max(0, T()))) / (1 - Math.Exp(-3)));
            case "decay": return Lerp(p[1].Y, p[0].Y, Math.Exp(-3 * Math.Max(0, T())));
            case "bell": { double u = (x - p[0].X) / (0.5 * (p[1].X - p[0].X)); return Lerp(p[1].Y, p[0].Y, Math.Exp(-0.5 * u * u)); }
            default:
                if (x <= p[0].X) return p[0].Y;
                for (int i = 1; i < p.Count; i++)
                    if (x < p[i].X) return Lerp(p[i - 1].Y, p[i].Y, Clamp01((x - p[i - 1].X) / (p[i].X - p[i - 1].X)));
                return p[^1].Y;
        }
    }

    /// <summary>+1 when a larger input gives a larger effect, −1 when smaller, 0 when it goes both ways.</summary>
    public int Sign
    {
        get {
            if (Family == "step") return Math.Sign(Above - Points[0].Y);
            if (Family == "bell") return 0;
            int up = 0, down = 0;
            for (int i = 1; i < Points.Count; i++) { double d = Points[i].Y - Points[i - 1].Y; if (d > 0) up++; else if (d < 0) down++; }
            return up > 0 && down == 0 ? 1 : down > 0 && up == 0 ? -1 : 0;
        }
    }

    public string ToFormula()
    {
        static string N(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture).Replace('.', ',');
        IEnumerable<double> args = Family == "step" ? new[] { Points[0].X, Points[0].Y, Above } : Points.SelectMany(p => new[] { p.X, p.Y });
        return $"curve.{Family}({Input}; {string.Join("; ", args.Select(N))}){Tail}";
    }

    private static readonly Regex Call = new(@"^\s*curve\.(\w+)\(\s*([^;()]+?)\s*;([^()]*)\)(.*)$", RegexOptions.Singleline);

    /// <summary>A formula that starts with a curve whose parameters are plain numbers; anything after it is kept as the tail.</summary>
    public static bool TryParse(string formula, out RuleCurve curve)
    {
        curve = null;
        var m = Call.Match(formula ?? "");
        if (!m.Success || Find(m.Groups[1].Value) is not { } family) return false;
        var numbers = new List<double>();
        foreach (string part in m.Groups[3].Value.Split(';'))
            if (double.TryParse(part.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) numbers.Add(v);
            else return false;
        var c = new RuleCurve { Family = family.Id, Input = m.Groups[2].Value.Trim(), Tail = m.Groups[4].Value };
        if (family.Id == "step") { if (numbers.Count != 3) return false; c.Points = new() { (numbers[0], numbers[1]) }; c.Above = numbers[2]; }
        else {
            if (numbers.Count % 2 != 0 || numbers.Count < 4 || family.Id != "points" && numbers.Count != 4) return false;
            c.Points = Enumerable.Range(0, numbers.Count / 2).Select(i => (numbers[2 * i], numbers[2 * i + 1])).ToList();
        }
        curve = c; return true;
    }

    /// <summary>Changes the family and keeps the points that still make sense.</summary>
    public void SetFamily(string id)
    {
        if (id == Family) return;
        var a = Points[0]; (double X, double Y) b = Points.Count > 1 ? Points[^1] : (a.X + 1, Above);
        if (id == "step") { Points = new() { a }; Above = b.Y; }
        else if (Family == "step") Points = new() { (a.X - 0.5 * Math.Max(Math.Abs(a.X), 0.1), a.Y), (a.X + 0.5 * Math.Max(Math.Abs(a.X), 0.1), Above) };
        else if (id == "points") Points = new() { a, ((a.X + b.X) / 2, (a.Y + b.Y) / 2), b };
        else Points = new() { a, b };
        Family = id;
    }
}
