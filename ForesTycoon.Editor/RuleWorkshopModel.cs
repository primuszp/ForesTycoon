#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

internal enum WorkshopPhase { Understand, Edit, Trial, Apply }
internal sealed record WorkshopTopic(string Name, string[] Modules);

/// <summary>One thematic identity per rule. Other entry points navigate to that identity.</summary>
internal static class RuleWorkshopModel
{
    internal static readonly WorkshopTopic[] Topics = {
        new("Erdő és élővilág", new[] { "Erdő", "Erdészeti beavatkozás", "Vadak" }),
        new("Időjárás, talaj és víz", new[] { "Talaj és klíma", "Vízháztartás" }),
        new("Kitermelés és szállítás", new[] { "Faanyag és logisztika" }),
        new("Utak és járművek", new[] { "Utak és nyomok", "Járművek" }),
        new("Gazdaság és fenntartás", new[] { "Gazdaság", "Fenntartás" }),
        new("Világ és idő", new[] { "Idő és vezérlés", "Terep" }),
        new("Látvány", new[] { "Megjelenítés" })
    };
    internal static int Topic(GameRuleDefinition rule) => Math.Max(0, Array.FindIndex(Topics, t => t.Modules.Contains(rule.Module)));
    internal static GameRuleDefinition[] Search(GameRuleIndex index, int topic, string query) => index.Search("", query.Trim())
        .Where(r => query.Trim().Length > 0 || Topic(r) == topic).ToArray();
    internal static string PublishedPath(string file) {
        string full = Path.GetFullPath(file);
        return full.EndsWith(".draft.json", StringComparison.OrdinalIgnoreCase) ? full[..^11] : full;
    }
    internal static string DraftPath(string published) => PublishedPath(published) + ".draft.json";
    internal static string KindName(string kind) => kind switch { "tree" => "Fa", "truck" => "Teherautó", "forwarder" => "Forwarder",
        "processor" => "Processzor", "road" => "Út", "tile" => "Csempe", "mill" => "Malom", "world" => "Világ", "wildlife" => "Vad", "depot" => "Telephely", "stack" => "Rakat", _ => kind };
    internal static string FieldName(string field) => field switch {
        "forest.fitness" => "Termőhelyi alkalmasság", "forest.resources" => "Fény és tér", "water.growthFactor" => "Növekedési vízválasz",
        "forest.health" => "Faegészség", "calendar.time" => "Naptár és évszak", "forest.species" => "Fafaj", "forest.trees" => "Faállomány",
        "forest.volume" => "Fatérfogat / növedék", "forest.stress" => "Tartós stressz", "water.root" => "Gyökérzónavíz",
        "water.surface" => "Felszíni víz", "soil.profile" => "Talajprofil", "weather.forcing" => "Helyi időjárás", "weather.rain" => "Csapadék",
        "road.condition" => "Útállapot", "road.network" => "Úthálózat", "vehicle.mass" => "Jármű össztömege", "vehicle.cargo" => "Rakomány",
        "machine.controllerState" => "Munkaciklus állapota", "machine.state" => "Gépállapot", "vehicle.route" => "Útvonal",
        "economy.income" => "Bevétel", "economy.expenses" => "Kiadások", "timber.stacks" => "Rakatok", "commands" => "Játékosparancsok",
        "world.time" => "Erdőév", "world.treeCount" => "Élő fák", "world.roadCount" => "Útcsempék", "world.water" => "Tárolt víz",
        "world.rain" => "Csapadékráta", "world.temperature" => "Hőmérséklet", "world.snow" => "Hókészlet", "world.balance" => "Egyenleg",
        "world.income" => "Bevétel", "world.expenses" => "Kiadások", "world.runningCosts" => "Üzemeltetés", _ => field };
}

/// <summary>Trial and publish results belong to the captured draft, never to whatever is open on completion.</summary>
internal sealed class WorkshopVersions
{
    internal string? Current { get; private set; }
    internal string? Saved { get; private set; }
    internal string? Tested { get; private set; }
    internal string? Published { get; private set; }
    internal string? Running { get; private set; }
    internal bool Unsaved => Current != Saved;
    internal bool TestedCurrent => Tested != null && Tested == Current;
    internal void Observe(string revision) { if (Current == null) Saved = revision; Current = revision; }
    internal void MarkSaved() => Saved = Current;
    internal void CompleteTrial(string capturedRevision) => Tested = capturedRevision;
    internal void MarkPublished(string capturedRevision) => Published = capturedRevision;
    internal void Acknowledge(string? runtimeRevision) => Running = runtimeRevision;
    internal void Reset() { Current = Saved = Tested = Published = Running = null; }
}
