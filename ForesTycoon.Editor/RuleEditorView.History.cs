using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

/// <summary>
/// Undo and redo for the scene: map paint, types, species, missions and the game's numbers. A step is recorded when an
/// edit is finished (no field active, no brush stroke), by comparing snapshots, so every editor part is covered alike.
/// </summary>
internal sealed partial class RuleEditorView
{
    private sealed record Snapshot(string Scenario, string Tuning, string Summary, string Rules = "");
    private readonly List<Snapshot> history = new();
    private int historyIndex = -1, historyFrame;
    private const int HistoryLimit = 100;

    private Snapshot TakeSnapshot(GameWorld world)
    {
        // The captured world state is large and never edited here: it stays out of the snapshot.
        string initial = scenario.InitialState; scenario.InitialState = null;
        string map;
        try { map = new RuleProject { World = scenario }.ToJson(); } finally { scenario.InitialState = initial; }
        var tuning = currentRules.TuningOverrides(world);
        return new(map, JsonSerializer.Serialize(new SortedDictionary<string, double>(tuning, StringComparer.Ordinal)), null,
            JsonSerializer.Serialize(new { Rules = RuleBookRules(world), Fields = RuleBookFields(world), RemovedStarters = RemovedStarters(world), Behaviors = behaviors.Document, Dynamics = dynamicsEditor.Document }));
    }

    private void ClearHistory() { history.Clear(); historyIndex = -1; }

    /// <summary>Records a step when the scene differs from the current step and no edit is in progress.</summary>
    private void TrackHistory(GameWorld world)
    {
        if (scenario == null || ImGui.IsAnyItemActive() || ImGui.IsMouseDown(ImGuiMouseButton.Left) || stroke.Count > 0) return;
        if (++historyFrame % 10 != 0 && historyIndex >= 0) return;
        var now = TakeSnapshot(world);
        if (historyIndex >= 0 && history[historyIndex].Scenario == now.Scenario && history[historyIndex].Tuning == now.Tuning && history[historyIndex].Rules == now.Rules) return;
        string summary = historyIndex < 0 ? "Megnyitás" : Describe(history[historyIndex], now);
        history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        history.Add(now with { Summary = summary });
        if (history.Count > HistoryLimit) history.RemoveAt(0);
        historyIndex = history.Count - 1;
    }

    private static string Describe(Snapshot before, Snapshot after)
    {
        if (before.Tuning != after.Tuning) return "szám módosítása";
        if (before.Rules != after.Rules) return "szabálykönyv módosítása";
        var a = RuleProject.FromJson(before.Scenario).World; var b = RuleProject.FromJson(after.Scenario).World;
        string J(object o) => JsonSerializer.Serialize(o);
        return J(a.Missions) != J(b.Missions) ? "küldetés módosítása"
            : J(a.SoilPaint) != J(b.SoilPaint) ? "talajfestés"
            : J(a.HeightPaint) != J(b.HeightPaint) ? "domborzat vagy víz festése"
            : J(a.ForestPaint) != J(b.ForestPaint) ? "erdőfestés"
            : J(a.Springs) != J(b.Springs) ? "forrás módosítása"
            : J(a.SoilTypes.Select(s => s.Horizons)) != J(b.SoilTypes.Select(s => s.Horizons)) || a.ProfileScale != b.ProfileScale ? "talajszelvény módosítása"
            : J(a.SoilTypes) != J(b.SoilTypes) ? "talajtípus módosítása"
            : J(a.ElevationZones) != J(b.ElevationZones) ? "magassági övek módosítása"
            : J(a.Biomes) != J(b.Biomes) ? "biomok módosítása"
            : J(a.GroundMaterials) != J(b.GroundMaterials) ? "felszínanyagok módosítása"
            : J(a.Species) != J(b.Species) ? "fafaj módosítása"
            : J(a.Roads) != J(b.Roads) || J(a.Sawmills) != J(b.Sawmills) || J(a.Depots) != J(b.Depots) ? "út vagy épület" : "pálya módosítása";
    }

    private void Restore(GameWorld world, int index, string verb)
    {
        var from = history[historyIndex]; var to = history[index];
        string summary = index < historyIndex ? from.Summary : to.Summary;
        var map = RuleProject.FromJson(to.Scenario).World;
        map.InitialState = scenario.InitialState;
        bool rebuild = MapPart(from.Scenario) != MapPart(to.Scenario);
        scenario = map; seedEdit = scenario.Seed;
        currentRules.SetTuning(JsonSerializer.Deserialize<Dictionary<string, double>>(to.Tuning));
        var book = JsonSerializer.Deserialize<RuleBookState>(to.Rules);
        if (book?.Rules != null) { ruleBook = book.Rules; ruleFields = book.Fields ?? new(); removedStarters = book.RemovedStarters ?? new(); ruleBookError = null; }
        if (book?.Behaviors != null) behaviors.LoadDraft(book.Behaviors);
        if (book?.Dynamics != null) dynamicsEditor.Load(book.Dynamics);
        expressionKey = activeCommonKey = null;
        historyIndex = index; statusFrame = -1000;
        if (rebuild) ApplyScenario(world, verb + ": " + summary + ".");
        else { Try(() => ApplyProject(world, false)); message = verb + ": " + summary + "."; }
    }

    /// <summary>The part of a snapshot that shapes the test world (missions do not).</summary>
    /// <summary>
    /// The part of a scene the test world is built from. Missions are checked by the running game and elevation zones only
    /// classify the built terrain (and biomes the zones), soil horizons only draw the diorama's sides: editing them does not rebuild the test world.
    /// </summary>
    private static string MapPart(string scenarioJson)
    {
        var p = RuleProject.FromJson(scenarioJson); p.World.Missions = new(); p.World.ElevationZones = new(); p.World.Biomes = new(); p.World.GroundMaterials = new(); p.World.ProfileScale = 0;
        foreach (var soil in p.World.SoilTypes) soil.Horizons = new();
        return p.ToJson();
    }

    private sealed record RuleBookState(List<WorldRule> Rules, List<RuleFieldDefinition> Fields, List<string> RemovedStarters = null, BehaviorModel Behaviors = null, DynamicsModel Dynamics = null);
    internal WorldScenario EditedScenario => scenario;
    internal void SetTuningForTest(string key, double value)
    {
        var overrides = currentRules.TuningOverrides(null); overrides[key] = value; currentRules.SetTuning(overrides);
    }
    internal bool CanUndo => historyIndex > 0;
    internal bool CanRedo => historyIndex >= 0 && historyIndex < history.Count - 1;
    internal void Undo(GameWorld world) { if (CanUndo) Restore(world, historyIndex - 1, "Visszavonva"); }
    internal void Redo(GameWorld world) { if (CanRedo) Restore(world, historyIndex + 1, "Újra"); }

    private void UndoButtons(GameWorld world)
    {
        TrackHistory(world);
        var io = ImGui.GetIO();
        bool typing = io.WantTextInput;
        if (!typing && io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) Redo(world); else Undo(world); }
        if (!typing && io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Y)) Redo(world);
        ImGui.BeginDisabled(!CanUndo);
        if (ImGui.Button("Vissza")) Undo(world);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(CanUndo ? "Visszavonás · Ctrl+Z: " + history[historyIndex].Summary : "Nincs visszavonható lépés.");
        ImGui.SameLine();
        ImGui.BeginDisabled(!CanRedo);
        if (ImGui.Button("Újra")) Redo(world);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(CanRedo ? "Újra · Ctrl+Y: " + history[historyIndex + 1].Summary : "Nincs újra végrehajtható lépés.");
    }
}
