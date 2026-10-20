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
    public static readonly string[] Schedules = { "step", "passage", "month", "growth", "wildlife", "population", "birth" };
    public string Id { get; set; } = "szabaly";
    public string Name { get; set; } = "Új szabály";
    public string Description { get; set; } = "";
    /// <summary>The author's group in the editor's rule list; empty groups the rule by its object. No effect on the game.</summary>
    public string Group { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>The starter rule this one was made from (its id), so it can be reset to it; empty for a rule written from scratch.</summary>
    public string Template { get; set; } = "";
    /// <summary>The entity set the rule runs on: <c>world</c>, <c>tile</c>, <c>trail</c>…</summary>
    public string Scope { get; set; } = "tile";
    public string Schedule { get; set; } = "step";
    /// <summary>A formula; the effects apply where it is not 0. Empty: always.</summary>
    public string Condition { get; set; } = "";
    public BehaviorGraph ConditionGraph { get; set; }
    /// <summary>Applied in order on each element; a later effect sees the earlier ones of the same element.</summary>
    public List<RuleEffect> Effects { get; set; } = new();
    public List<RuleEffect> EffectiveEffects() => Effects.Select(e => e.Executable()).ToList();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Description == null || string.IsNullOrWhiteSpace(Scope)
            || Array.IndexOf(Schedules, Schedule) < 0 || Condition == null || Effects == null || Effects.Count > 32)
            throw new InvalidDataException($"{Name}: a szabálynak azonosítója, neve, hatóköre, időzítése és legfeljebb 32 hatása legyen.");
        if (Schedule == "growth" && (Scope != "tree" || Effects.Any(e => e == null || e.Mode is not ("set" or "add") || e.Target is not ("diameterGrowth" or "heightGrowth" or "crownGrowth") || e.Influence is { } rate && (rate.PerYear || rate.PerHour))))
            throw new InvalidDataException("A növekedési rátafrissítés csak a fa növekedési rátáit írhatja.");
        if ((Schedule == "passage") != (Scope == "passage"))
            throw new InvalidDataException("Az áthaladási esemény hatóköre passage; ez az objektum csak áthaladáskor érhető el.");
        if (Schedule == "passage" && Effects.Any(e => e?.Influence is { } influence && (influence.PerYear || influence.PerHour)))
            throw new InvalidDataException("Az áthaladás hatása eseményenként értendő, nem időarányos.");
        foreach (var e in Effects)
            if (e == null || Array.IndexOf(RuleEffect.Modes, e.Mode) < 0 || e.Formula == null || e.To == null || e.FlowName == null || e.Level == null
                || (e.Mode == "flow" ? string.IsNullOrWhiteSpace(e.Target) && string.IsNullOrWhiteSpace(e.To) || e.Target == e.To : string.IsNullOrWhiteSpace(e.Target))
                || Array.IndexOf(RuleEffect.Spreads, e.Spread) < 0)
                throw new InvalidDataException($"{Name}: minden hatásnak célja, módja (set, add, approach, action, flow) és képlete legyen; egy áramlásnak forrása vagy célja, és a kettő különbözzön.");
        if (ConditionGraph != null && Condition.Length != 0) throw new InvalidDataException("A feltételnek egyetlen szerkesztett alakja legyen.");
        foreach (var e in Effects) {
            if (e.Computation != null && (e.Influence != null || e.Formula.Length != 0)) throw new InvalidDataException("A hatásnak egyetlen szerkesztett alakja legyen.");
            if (!double.IsFinite(e.ApproachPerYear) || e.ApproachPerYear < 0 || e.ApproachPerYear > 1e9)
                throw new InvalidDataException("A közelítés sebessége véges, nemnegatív szám legyen.");
            if (e.Mode == "approach" && e.Influence is { } target && (target.PerYear || target.PerHour))
                throw new InvalidDataException("A közelítés célérték, nem idővel szorzott változás.");
            if (e.Influence == null) continue;
            if (e.Formula.Length != 0) throw new InvalidDataException("A vizuális hatást külön képlet nélkül kell tárolni.");
            _ = e.Influence.Compile();
        }

    }
}

public sealed class RuleEffect
{
    public static readonly string[] Modes = { "set", "add", "action", "flow", "approach" };
    /// <summary>How a flow into a collection (“neighbours.surfaceWater”) is shared: evenly, or by how much lower each member's level is.</summary>
    public static readonly string[] Spreads = { "even", "downhill" };
    /// <summary>A field name (set, add), an action name of the scope (action), or the stock a flow drains (flow; empty: from outside the model).</summary>
    public string Target { get; set; } = "";
    /// <summary>
    /// Flow only: the stock that receives what <see cref="Target"/> loses — a field of the element or an attached object,
    /// a collection's field (“neighbours.surfaceWater”), or empty when it leaves the model (a sink).
    /// </summary>
    public string To { get; set; } = "";
    /// <summary>Flow only: its name in the flow ledger (“párolgás”); the game totals what each named flow has moved.</summary>
    public string FlowName { get; set; } = "";
    /// <summary>Flow into a collection: <c>even</c> or <c>downhill</c> by <see cref="Level"/>.</summary>
    public string Spread { get; set; } = "even";
    /// <summary>Downhill flow: the members' field compared (“waterLevel”); a member gets a share as much as it is lower.</summary>
    public string Level { get; set; } = "";
    public bool IsFlow => Mode == "flow";
    public string Mode { get; set; } = "set";
    /// <summary>Maximum absolute movement towards the computed target, per forest year.</summary>
    public double ApproachPerYear { get; set; } = .42;
    /// <summary>The new value, the increment, or for an action its trigger (not 0 = do it; empty = always).</summary>
    public string Formula { get; set; } = "";
    public RuleInfluence Influence { get; set; }
    public BehaviorGraph Computation { get; set; }
    public RuleEffect Executable() => Influence == null ? this : new() { Target = Target, Mode = Mode, To = To, FlowName = FlowName, Spread = Spread, Level = Level, ApproachPerYear = ApproachPerYear,
        Formula = Mode == "action" ? $"ha({Influence.Compile()} > 0; 1; 0)" : Influence.Compile() };
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
