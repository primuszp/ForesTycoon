using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Composable, formula-free effect. The compiler and the editor share this saved representation.</summary>
public sealed class RuleInfluence
{
    public bool PerYear { get; set; } = true;
    public List<InfluenceTerm> Terms { get; set; } = new();
    internal static string Number(double v) => v.ToString("0.###############################", CultureInfo.InvariantCulture);
    internal static string Field(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]) || name.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '_'))
            throw new InvalidDataException("Válassz egy elérhető mezőt.");
        return name;
    }
    internal static void Finite(double v) { if (!double.IsFinite(v) || Math.Abs(v) > 1e9) throw new InvalidDataException("A hatás értéke véges, legfeljebb egymilliárd nagyságú szám lehet."); }
    public string Compile()
    {
        if (Terms == null || Terms.Count > 16 || Terms.Any(t => t == null)) throw new InvalidDataException("Egy hatáshoz legfeljebb 16 összetevő adható.");
        return "(" + (Terms.Count == 0 ? "0" : string.Join(" + ", Terms.Select(t => t.Compile()))) + ")" + (PerYear ? " * deltaYears" : "");
    }
}

public sealed class InfluenceTerm
{
    public string Name { get; set; } = "Új kapcsolat";
    public bool Enabled { get; set; } = true;
    public bool Decrease { get; set; } = true;
    public double Amount { get; set; } = .1;
    public string AmountField { get; set; } = "";
    public List<InfluenceFactor> Factors { get; set; } = new();
    public string WhenField { get; set; } = "";
    public string Comparison { get; set; } = "greater";
    public double Threshold { get; set; } = .5;
    public List<InfluenceCondition> Conditions { get; set; } = new();

    public void UpgradeConditions()
    {
        if (string.IsNullOrEmpty(WhenField) || Conditions == null) return;
        Conditions.Insert(0, new() { Field = WhenField, Comparison = Comparison, Value = Threshold });
        WhenField = "";
    }

    internal string Compile()
    {
        RuleInfluence.Finite(Amount); RuleInfluence.Finite(Threshold);
        if (Amount < 0 || Name == null || AmountField == null || WhenField == null || Comparison is not ("greater" or "less")
            || Factors == null || Factors.Count > 8 || Factors.Any(f => f == null)
            || Conditions == null || Conditions.Count > 8 || Conditions.Any(c => c == null)) throw new InvalidDataException("Hibás hatásösszetevő.");
        string amount = AmountField.Length == 0 ? RuleInfluence.Number(Amount) : RuleInfluence.Field(AmountField);
        string value = (Decrease ? "-" : "") + "(" + amount + string.Concat(Factors.Select(f => " * " + f.Compile())) + ")";
        if (WhenField.Length > 0) value = $"ha({RuleInfluence.Field(WhenField)} {(Comparison == "less" ? "<" : ">")} {RuleInfluence.Number(Threshold)}; {value}; 0)";
        foreach (var condition in Conditions) value = $"ha({condition.Compile()}; {value}; 0)";
        return Enabled ? value : "0";
    }
}

/// <summary>A visual comparison. Conditions on the same term must all hold.</summary>
public sealed class InfluenceCondition
{
    public string Field { get; set; } = "";
    public string Comparison { get; set; } = "atLeast";
    public double Value { get; set; } = 1;
    public string ValueField { get; set; } = "";
    internal string Compile()
    {
        RuleInfluence.Finite(Value);
        if (ValueField == null) throw new InvalidDataException("Hiányzó határértékforrás.");
        if (Comparison is "equal" or "notEqual") {
            // For bool and enum fields: exactly this option (or any other one).
            string field = RuleInfluence.Field(Field), value = ValueField.Length == 0 ? RuleInfluence.Number(Value) : RuleInfluence.Field(ValueField);
            return Comparison == "equal" ? $"ha({field} < {value}; 0; ha({field} > {value}; 0; 1))" : $"ha({field} < {value}; 1; ha({field} > {value}; 1; 0))";
        }
        string op = Comparison switch { "less" or "atLeast" => "<", "greater" or "atMost" => ">",
            _ => throw new InvalidDataException("Válassz érvényes összehasonlítást.") };
        if (ValueField == null) throw new InvalidDataException("Hiányzó határértékforrás.");
        string test = $"{RuleInfluence.Field(Field)} {op} {(ValueField.Length == 0 ? RuleInfluence.Number(Value) : RuleInfluence.Field(ValueField))}";
        return Comparison is "atLeast" or "atMost" ? $"ha({test}; 0; 1)" : test;
    }
}

/// <summary>A source field mapped to a multiplier. No arbitrary expression text is accepted.</summary>
public sealed class InfluenceFactor
{
    public string Source { get; set; } = "rainIntensity";
    public string Shape { get; set; } = "linear";
    public double InputLow { get; set; }
    public double InputHigh { get; set; } = 1;
    public double OutputLow { get; set; }
    public double OutputHigh { get; set; } = 1;
    // Only the variable-length family needs extra coordinates; old projects keep their existing fields.
    public List<double> Breakpoints { get; set; }
    public RuleCurve ToCurve() => new() {
        Family = Shape, Input = Source, Above = OutputHigh,
        Points = Shape == "points" && Breakpoints != null
            ? Enumerable.Range(0, Breakpoints.Count / 2).Select(i => (Breakpoints[i * 2], Breakpoints[i * 2 + 1])).ToList()
            : Shape == "step" ? new() { (InputHigh, OutputLow) } : new() { (InputLow, OutputLow), (InputHigh, OutputHigh) }
    };
    public void SetCurve(RuleCurve curve)
    {
        Shape = curve.Family; Source = curve.Input;
        InputLow = curve.Points[0].X; OutputLow = curve.Points[0].Y;
        InputHigh = curve.Points[^1].X; OutputHigh = Shape == "step" ? curve.Above : curve.Points[^1].Y;
        Breakpoints = Shape == "points" ? curve.Points.SelectMany(p => new[] { p.X, p.Y }).ToList() : null;
    }
    internal string Compile()
    {
        RuleInfluence.Field(Source);
        if (Breakpoints != null && (Breakpoints.Count < 4 || Breakpoints.Count > 32 || Breakpoints.Count % 2 != 0))
            throw new InvalidDataException("A görbéhez 2–16 pont szükséges.");
        var curve = ToCurve();
        curve.Validate();
        return curve.ToFormula();
    }
}
