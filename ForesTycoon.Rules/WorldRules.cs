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

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Description == null || string.IsNullOrWhiteSpace(Scope)
            || Array.IndexOf(Schedules, Schedule) < 0 || Condition == null || Effects == null || Effects.Count > 32)
            throw new InvalidDataException($"{Name}: a szabálynak azonosítója, neve, hatóköre, időzítése és legfeljebb 32 hatása legyen.");
        foreach (var e in Effects)
            if (e == null || string.IsNullOrWhiteSpace(e.Target) || Array.IndexOf(RuleEffect.Modes, e.Mode) < 0 || e.Formula == null)
                throw new InvalidDataException($"{Name}: minden hatásnak célja, módja (set, add, action) és képlete legyen.");
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
