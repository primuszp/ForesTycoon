using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
/// <summary>Composes catalog navigation, map, inspector, tuning and vehicle components.</summary>
internal sealed partial class CurrentRulesView : IDisposable
{
    private readonly EditorTuning tuning;
    private readonly bool ownsTuning;
    private readonly RuleCatalogSession session;
    private readonly RuleCatalogLayout layout = new();
    private readonly RuleCatalogNavigation navigation;
    private readonly RuleCatalogSidebarView sidebar;
    private readonly RuleCatalogMapView map;
    private readonly RuleCatalogInspectorView inspector;
    private readonly RuleTuningView tuningView;
    internal CurrentRulesView(EditorTuning tuning = null, Action<string, double> setValue = null, CurrentRulesUiState ui = null, VehicleGeometryUiState vehicleUi = null,
        Func<string, string> readNotes = null, Action<string, string> writeNotes = null)
    {
        ownsTuning = tuning == null; this.tuning = tuning ?? new();
        var set = setValue ?? this.tuning.Set;
        session = new(this.tuning, ownsTuning);
        navigation = new(session, layout, ui ?? new());
        sidebar = new(navigation); map = new(navigation);
        tuningView = new(navigation, set, EnsureCatalog, rule => RuleTarget?.Invoke(rule));
        inspector = new(navigation, tuningView, readNotes, writeNotes, rule => RuleTarget?.Invoke(rule));
        vehicles = new(new(this.tuning, vehicleUi ?? new(), () => Catalog, set, EnsureCatalog, UnappliedTuning, DrawTuning));
    }
    internal string Message { get; set; } = "";
    internal GameRuleCatalog Catalog => session.Catalog;
    internal GameRuleIndex Index => session.Index;
    internal Func<GameRuleDefinition, Action> RuleTarget { get; set; }
    internal void EnsureCatalog(GameWorld world) { if (Catalog == null) Load(world.DescribeCurrentRules()); }
    internal void Load(GameRuleCatalog loaded, string file = null, GameRuleCatalog current = null)
    {
        session.Load(loaded, file, current); layout.Load(Index); navigation.Reset(); inspector.Reset();
    }
    internal void SelectModule(string value) => navigation.SelectModule(value);
    internal void SetTuning(IReadOnlyDictionary<string, double> overrides) => tuning.Replace(overrides);
    internal Dictionary<string, double> TuningOverrides(GameWorld world)
    {
        if (Catalog == null && ownsTuning && world != null) EnsureCatalog(world);
        return session.CaptureTuning();
    }
    internal int UnappliedTuning(GameWorld world) => session.UnappliedTuning(world.Tuning);
    internal bool TuningField(string key, string id) => tuningView.Field(key, id);
    internal void DrawTuning(GameWorld world, Func<string, bool> include = null) => tuningView.Draw(world, include);
    public void Dispose() => session.Dispose();
    internal void Draw(GameWorld world, Action<BehaviorHook> editBehavior = null, Action<string> editController = null)
    {
        EnsureCatalog(world);
        float height = Math.Max(260, ImGui.GetContentRegionAvail().Y);
        ImGui.BeginChild("rules-sidebar", new Vec2(250, height), ImGuiChildFlags.Borders);
        sidebar.Draw();
        ImGui.EndChild(); ImGui.SameLine();
        float canvasWidth = Math.Max(200, ImGui.GetContentRegionAvail().X - 370);
        ImGui.BeginChild("rules-map", new Vec2(canvasWidth, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (navigation.State.Module == "gaps") RuleCatalogGapsView.Draw(Catalog); else map.Draw();
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("rules-inspector", new Vec2(0, height), ImGuiChildFlags.Borders);
        inspector.Draw(world, editBehavior, editController);
        ImGui.EndChild();
    }

}
