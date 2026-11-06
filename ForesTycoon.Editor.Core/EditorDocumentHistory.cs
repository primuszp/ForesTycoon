#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

internal sealed record EditorDocumentRestore(string Summary, bool RebuildWorld);

/// <summary>The scene undo scope: map, tuning, rule book, behaviors and dynamics. Authoring retains its local history.</summary>
internal sealed class EditorDocumentHistory
{
    private sealed record Snapshot(string Scenario, string Tuning, string Rules);
    private sealed record RuleBookState(List<WorldRule> Rules, List<RuleFieldDefinition> Fields, List<string> RemovedStarters,
        BehaviorModel Behaviors, DynamicsModel Dynamics);
    private static readonly JsonSerializerOptions mapOptions = MapOptions();
    private readonly EditorDocument document;
    private readonly EditorHistoryService<Snapshot> history;
    private long checkedVersion = -1;
    internal bool CanUndo => history.CanUndo;
    internal bool CanRedo => history.CanRedo;
    internal int Count => history.Count;
    internal bool IsEditing => history.IsEditing;
    internal string UndoSummary => history.UndoSummary;
    internal string RedoSummary => history.RedoSummary;

    internal EditorDocumentHistory(EditorDocument document, int limit = 100)
    {
        this.document = document;
        history = new(Capture, Restore, Describe, document.Invalidate, limit);
        history.EditFinished += _ => checkedVersion = document.Version;
    }

    internal void Reset() { history.Reset(); checkedVersion = document.Version; }
    internal bool Checkpoint(bool force = false)
    {
        if (history.IsEditing || !force && checkedVersion == document.Version) return false;
        bool recorded = history.Checkpoint();
        if (recorded) document.Invalidate();
        checkedVersion = document.Version; return recorded;
    }
    internal EditorEdit BeginEdit(string summary) => history.BeginEdit(summary);
    internal bool Edit(string summary, Action change) => history.Edit(summary, change);
    internal bool CommitEdit() => history.CommitEdit();
    internal void Execute(string summary, Action change) => history.Execute(summary, change);
    internal TResult Execute<TResult>(string summary, Func<TResult> change) => history.Execute(summary, change);
    internal bool CancelEdit() => history.CancelEdit();
    internal EditorDocumentRestore? Undo() => Navigate(false);
    internal EditorDocumentRestore? Redo() => Navigate(true);

    private EditorDocumentRestore? Navigate(bool redo)
    {
        bool moved = redo ? history.Redo() : history.Undo(); checkedVersion = document.Version;
        if (!moved) return null;
        var move = history.LastMove!;
        return new(move.Summary, MapPart(move.Before.Scenario) != MapPart(move.After.Scenario));
    }

    private Snapshot Capture() => new(
        JsonSerializer.Serialize(new RuleProject { World = document.World }, mapOptions),
        JsonSerializer.Serialize(new SortedDictionary<string, double>(document.Tuning.Capture(), StringComparer.Ordinal)),
        JsonSerializer.Serialize(new RuleBookState(document.Rules, document.Fields, document.RemovedStarters,
            document.Behaviors.Draft, document.Dynamics.Draft)));

    private void Restore(Snapshot snapshot)
    {
        var map = RuleProject.FromJson(snapshot.Scenario).World;
        if (map != null) map.InitialState = document.World?.InitialState;
        var tuning = JsonSerializer.Deserialize<Dictionary<string, double>>(snapshot.Tuning)!;
        var book = JsonSerializer.Deserialize<RuleBookState>(snapshot.Rules)!;
        _ = GameTuning.FromOverrides(tuning);
        document.World = map; document.Tuning.Replace(tuning);
        // Unrelated local histories and object references survive a map or tuning undo/cancel.
        bool Different<T>(T current, T saved) => JsonSerializer.Serialize(current) != JsonSerializer.Serialize(saved);
        if (Different(document.Rules, book.Rules)) document.Rules = book.Rules;
        if (Different(document.Fields, book.Fields)) document.Fields = book.Fields;
        if (Different(document.RemovedStarters, book.RemovedStarters)) document.RemovedStarters = book.RemovedStarters;
        if (Different(document.Behaviors.Draft, book.Behaviors)) document.Behaviors.LoadDraft(book.Behaviors);
        if (Different(document.Dynamics.Draft, book.Dynamics)) document.Dynamics.Load(book.Dynamics);
        document.Invalidate();
    }

    private static string Describe(Snapshot before, Snapshot after)
    {
        if (before.Tuning != after.Tuning) return "szám módosítása";
        if (before.Rules != after.Rules) return "szabálykönyv módosítása";
        var a = RuleProject.FromJson(before.Scenario).World; var b = RuleProject.FromJson(after.Scenario).World;
        if (a == null || b == null) return "pálya módosítása";
        string J(object o) => JsonSerializer.Serialize(o);
        return J(a.Missions) != J(b.Missions) ? "küldetés módosítása"
            : J(a.SoilPaint) != J(b.SoilPaint) ? "talajfestés"
            : J(a.HeightPaint) != J(b.HeightPaint) ? "domborzat vagy víz festése"
            : J(a.ForestPaint) != J(b.ForestPaint) ? "erdőfestés"
            : J(a.ForestFelling) != J(b.ForestFelling) ? "fakivágás"
            : J(a.Springs) != J(b.Springs) ? "forrás módosítása"
            : J(a.SoilTypes.Select(s => s.Horizons)) != J(b.SoilTypes.Select(s => s.Horizons)) || a.ProfileScale != b.ProfileScale ? "talajszelvény módosítása"
            : J(a.SoilTypes) != J(b.SoilTypes) ? "talajtípus módosítása"
            : J(a.ElevationZones) != J(b.ElevationZones) ? "magassági övek módosítása"
            : J(a.Biomes) != J(b.Biomes) ? "biomok módosítása"
            : J(a.GroundMaterials) != J(b.GroundMaterials) ? "felszínanyagok módosítása"
            : J(a.Species) != J(b.Species) ? "fafaj módosítása"
            : J(a.Roads) != J(b.Roads) || J(a.Sawmills) != J(b.Sawmills) || J(a.Depots) != J(b.Depots) ? "út vagy épület" : "pálya módosítása";
    }

    /// <summary>Scene changes that require rebuilding the preview; evaluated without changing the authored world.</summary>
    internal static string MapPart(string scenarioJson)
    {
        var p = RuleProject.FromJson(scenarioJson);
        if (p.World == null) return p.ToJson();
        p.World.Missions = new(); p.World.ElevationZones = new(); p.World.Biomes = new(); p.World.GroundMaterials = new(); p.World.ProfileScale = 0;
        foreach (var soil in p.World.SoilTypes) soil.Horizons = new();
        return p.ToJson();
    }

    private static JsonSerializerOptions MapOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type => {
            if (type.Type == typeof(WorldScenario))
                type.Properties.Single(p => p.Name == "initialState").Get = _ => null;
        });
        return new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            TypeInfoResolver = resolver, Converters = { new JsonStringEnumConverter() } };
    }
}
