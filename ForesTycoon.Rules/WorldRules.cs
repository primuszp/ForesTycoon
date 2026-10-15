using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>
/// A game rule as data: when it runs (schedule), where (scope: every element of an entity set), if what holds (condition)
/// and what changes (effects). The game runs it; no C# is needed to add or change one. See docs/rule-building-blocks.md.
/// </summary>
public sealed class WorldRule
{
    public static readonly string[] Schedules = { "step" };
    public string Id { get; set; } = "szabaly";
    public string Name { get; set; } = "Új szabály";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>A rule the game ships; it can be edited or switched off, not deleted, and can be reset to the original.</summary>
    public bool BuiltIn { get; set; }
    /// <summary>The entity set the rule runs on: <c>world</c>, <c>tile</c>, <c>trail</c>…</summary>
    public string Scope { get; set; } = "tile";
    public string Schedule { get; set; } = "step";
    /// <summary>A formula; the effects apply where it is not 0. Empty: always.</summary>
    public string Condition { get; set; } = "";
    /// <summary>Applied in order on each element; a later effect sees the earlier ones of the same element.</summary>
    public List<RuleEffect> Effects { get; set; } = new();
    public RoadWeatherCard RoadWeather { get; set; }
    public bool UseLegacyRoadOverride { get; set; }
    public List<RuleEffect> EffectiveEffects() => RoadWeather?.CompileEffects() ?? Effects.Select(e => e.Executable()).ToList();

    public void UpgradeAuthoring()
    {
        TrailAgeRule.Upgrade(this);
        if (Scope == "stump" && Condition == "" && Effects != null && Effects.Count == 1 && Effects[0].Influence == null
            && Effects[0].Target == "remove" && Effects[0].Mode == "action"
            && RuleCurve.TryParse(Effects[0].Formula, out var curve) && curve.Family == "step" && curve.Input == "age"
            && curve.Tail.Length == 0 && curve.Points[0].X > 0 && curve.Points[0].Y == 0 && curve.Above == 1) {
            double lifetime = curve.Points[0].X;
            Effects = new() {
                new() { Target = "decayProgress", Mode = "set", Influence = new() { PerYear = false, Terms = new() {
                    new() { Name = "Lebomlás az életkor alapján", Decrease = false, Amount = 1, Factors = new() {
                        new() { Source = "age", InputHigh = lifetime } } } } } },
                new() { Target = "remove", Mode = "action", Influence = new() { PerYear = false, Terms = new() {
                    new() { Name = "Eltűnés teljes lebomláskor", Decrease = false, Amount = 1, Factors = new() {
                        new() { Source = "decayProgress", Shape = "step", InputHigh = 1 } } } } } }
            };
        }
        if (RoadWeather != null) {
            Validate();
            Effects = RoadWeather.ToInfluences();
            UseLegacyRoadOverride = RoadWeather.UseLegacyOverride;
            RoadWeather = null;
        }
        if (Effects != null) foreach (var term in Effects.Where(e => e?.Influence?.Terms != null).SelectMany(e => e.Influence.Terms)) term?.UpgradeConditions();
    }

    /// <summary>Renames whole field and action names everywhere the rule mentions them (formulas, targets, visual effects).</summary>
    public void RenameFields(IReadOnlyDictionary<string, string> names)
    {
        if (names.Count == 0 || Effects == null || Effects.Any(e => e == null)) return;
        string Map(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            foreach (var (oldName, newName) in names)
                if (value.Contains(oldName, StringComparison.Ordinal))
                    value = System.Text.RegularExpressions.Regex.Replace(value,
                        @"(?<![\w.])" + System.Text.RegularExpressions.Regex.Escape(oldName) + @"(?![\w.])", newName.Replace("$", "$$"));
            return value;
        }
        Condition = Map(Condition);
        foreach (var effect in Effects) {
            effect.Target = Map(effect.Target); effect.Formula = Map(effect.Formula);
            if (effect.Influence?.Terms == null) continue;
            foreach (var term in effect.Influence.Terms.Where(t => t != null)) {
                term.WhenField = Map(term.WhenField); term.AmountField = Map(term.AmountField);
                foreach (var factor in term.Factors ?? new()) if (factor != null) factor.Source = Map(factor.Source);
                foreach (var condition in term.Conditions ?? new()) if (condition != null) { condition.Field = Map(condition.Field); condition.ValueField = Map(condition.ValueField); }
            }
        }
    }

    public void Validate()
    {
        if (RoadWeather != null) {
            RoadWeather.Validate();
            if (Scope != "road" || Schedule != "step" || Condition != "" || Effects == null || Effects.Count != 0)
                throw new InvalidDataException("Az útromlási kártya road/step szabály; külön feltétel és hatás nélkül tárolandó.");
        }
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Description == null || string.IsNullOrWhiteSpace(Scope)
            || Array.IndexOf(Schedules, Schedule) < 0 || Condition == null || Effects == null || Effects.Count > 32)
            throw new InvalidDataException($"{Name}: a szabálynak azonosítója, neve, hatóköre, időzítése és legfeljebb 32 hatása legyen.");
        foreach (var e in Effects)
            if (e == null || string.IsNullOrWhiteSpace(e.Target) || Array.IndexOf(RuleEffect.Modes, e.Mode) < 0 || e.Formula == null)
                throw new InvalidDataException($"{Name}: minden hatásnak célja, módja (set, add, action) és képlete legyen.");
        foreach (var e in Effects) {
            if (e.Influence == null) continue;
            if (e.Formula.Length != 0 || e.Mode is not ("add" or "set" or "action")) throw new InvalidDataException("A vizuális hatást külön képlet nélkül kell tárolni.");
            _ = e.Influence.Compile();
        }
        if (UseLegacyRoadOverride && (Scope != "road" || Effects.Count != 1 || Effects[0].Target != "condition" || Effects[0].Mode != "add"))
            throw new InvalidDataException("A régi útromlási felülírás csak egyetlen condition hatással használható. Kapcsold ki a régi felülírást.");
    }
}

public sealed class RuleEffect
{
    public static readonly string[] Modes = { "set", "add", "action" };
    /// <summary>A field name (set, add) or an action name of the scope (action).</summary>
    public string Target { get; set; } = "";
    public string Mode { get; set; } = "set";
    /// <summary>The new value, the increment, or for an action its trigger (not 0 = do it; empty = always).</summary>
    public string Formula { get; set; } = "";
    public RuleInfluence Influence { get; set; }
    public RuleEffect Executable() => Influence == null ? this : new() { Target = Target, Mode = Mode, Formula = Mode == "action" ? $"ha({Influence.Compile()} > 0; 1; 0)" : Influence.Compile() };
}

/// <summary>A state variable the designer adds to a scope; the game keeps one value per element and saves it.</summary>
public sealed class RuleFieldDefinition
{
    public string Name { get; set; } = "saját.érték";
    public string Scope { get; set; } = "tile";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Initial { get; set; }
    public double Min { get; set; } = -1e9;
    public double Max { get; set; } = 1e9;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Name.StartsWith("saját.", StringComparison.Ordinal) || Name.Length > 64
            || Name.Any(c => char.IsWhiteSpace(c) || c is '(' or ')' or ';' or '+' or '-' or '*' or '/' or '<' or '>' or '='))
            throw new InvalidDataException($"{Name}: a saját mező neve „saját.” kezdetű, szóköz és műveleti jel nélküli legyen.");
        if (string.IsNullOrWhiteSpace(Scope) || Description == null || Unit == null || !(Min <= Initial && Initial <= Max) || !double.IsFinite(Min) || !double.IsFinite(Max))
            throw new InvalidDataException($"{Name}: érvénytelen hatókör vagy korlát (alsó ≤ kezdő ≤ felső).");
    }
}
