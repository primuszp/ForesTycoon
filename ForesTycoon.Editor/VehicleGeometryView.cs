using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The vehicle page, in the order a designer works: lay out the axles, tie them to the model, then watch the rig drive.
/// Every field here is the same tunable the rule project saves; nothing reaches the world before it is applied.
/// </summary>
internal sealed partial class VehicleGeometryView
{
    internal const int GeometryLayoutTab = 0, GeometryBindingTab = 1, GeometryTrialTab = 2, GeometryOperatingTab = 3;
    private static readonly string[] GeometryKinds = { "Tehergépkocsi", "Forwarder", "Harvester / processzor" };
    private static readonly string[] GeometryTabs = { "1 · Tengelyelrendezés", "2 · Modellkötés", "3 · Pályapróba", "Üzemi adatok" };
    private ref int geometryPreviewKind => ref context.Ui.Kind;
    private ref int geometryTab => ref context.Ui.Tab;
    private static readonly Vec4[] GeometryColours = {
        new(.42f,.76f,1,1), new(1,.72f,.28f,1), new(.62f,.88f,.47f,1), new(.85f,.58f,.95f,1),
        new(1,.5f,.44f,1), new(.45f,.9f,.85f,1), new(.91f,.83f,.58f,1), new(.72f,.75f,1,1) };

    internal void SelectGeometryTabForTest(int kind, int tab) { geometryPreviewKind = kind; geometryTab = tab; geometrySurveys[kind] = null; geometrySurveyed[kind] = false; }

    internal void DrawVehicleGeometryPreview(GameWorld world) => DrawVehicleWorkspace(world, _ => true, null);

    internal void DrawVehicleWorkspace(GameWorld world, Func<string, bool> operatingRules, Action apply)
    {
        context.EnsureCatalog(world);
        DrawGeometryHeader(world, apply);
        if (geometryTab == GeometryOperatingTab) { context.DrawTuning(world, operatingRules); return; }
        var tuning = GeometryTuning(out string problem);
        RigProfile profile = null;
        if (tuning != null) try { profile = GeometryProfile(tuning); profile.Validate(); }
            catch (ArgumentException ex) { problem = ex.Message; profile = null; }
        if (geometryTab == GeometryLayoutTab) DrawGeometryLayout(profile, problem);
        else if (profile == null) { ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Bad, problem); ImGui.PopTextWrapPos(); }
        else if (geometryTab == GeometryBindingTab) DrawGeometryBinding(profile, tuning);
        else DrawGeometryTrial(profile);
    }

    private void DrawGeometryHeader(GameWorld world, Action apply)
    {
        for (int i = 0; i < GeometryKinds.Length; i++) {
            if (i > 0) ImGui.SameLine(0, 4);
            bool active = i == geometryPreviewKind;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, EditorStyle.Fade(HudTheme.AmberAccent, .3f));
            if (ImGui.Button(GeometryKinds[i] + "##kind")) geometryPreviewKind = i;
            if (active) ImGui.PopStyleColor();
        }
        int unapplied = context.Unapplied(world);
        if (unapplied > 0) {
            ImGui.SameLine(0, 18); ImGui.AlignTextToFramePadding();
            EditorStyle.Text(HudTheme.Info, $"{unapplied} módosítás még nincs alkalmazva");
            if (apply != null) { ImGui.SameLine(0, 10); if (EditorStyle.PrimaryButton("Alkalmazás a tesztvilágban")) apply(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A modellkötés azonnal látszik. Az új tengelyelrendezést a jármű\na következő szolgálatba álláskor kapja meg; a mozgó szerelvény nem alakul át menet közben.");
        }
        EditorStyle.Tabs("geometry-tab", GeometryTabs, ref geometryTab);
    }

    private GameTuning GeometryTuning(out string problem)
    {
        problem = null;
        try { return GameTuning.FromOverrides(context.Tuning.Capture()); }
        catch (ArgumentException ex) { problem = ex.Message; return null; }
    }

    private RigProfile GeometryProfile(GameTuning tuning) => geometryPreviewKind == 0 ? VehicleGeometryProfiles.Truck(tuning) :
        VehicleGeometryProfiles.Machine(tuning, geometryPreviewKind == 1 ? ForestMachineKind.Forwarder : ForestMachineKind.Harvester);

    private string GeometryMachine => geometryPreviewKind == 1 ? "Forwarder" : "Processor";

    // ── Editing a tunable by its key ─────────────────────────────────────
    private bool FindGeometry(string key, out GameRuleDefinition rule, out int index)
    {
        foreach (var candidate in context.Catalog().Rules) {
            if (candidate.Id is not ("vehicle.geometry" or "machine.geometry.forwarder" or "machine.geometry.processor" or "vehicle.modelBinding")) continue;
            index = candidate.Parameters.FindIndex(p => p.Key == key);
            if (index >= 0) { rule = candidate; return true; }
        }
        rule = null; index = -1; return false;
    }

    internal double GetGeometry(string key) => FindGeometry(key, out var rule, out int index) ? rule.Parameters[index].Value : 0;

    internal void SetGeometry(string key, double value)
    {
        if (!FindGeometry(key, out var rule, out int index)) return;
        var p = rule.Parameters[index];
        context.SetValue(p.Key, Math.Clamp(value, p.Min, p.Max));
    }

    private void GeometryFieldLabel(string key, string label, string help, out RuleParameter parameter)
    {
        FindGeometry(key, out var rule, out int index); parameter = rule.Parameters[index];
        var at = ImGui.GetCursorScreenPos();
        if (parameter.Changed) ImGui.GetWindowDrawList().AddCircleFilled(at + new Vec2(-6, ImGui.GetTextLineHeight() / 2 + 3), 2.5f, EditorStyle.Selection);
        ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{help}\nAlapérték: {parameter.Default:G6} {parameter.Unit} · tartomány: {parameter.Min:G6} – {parameter.Max:G6}");
    }

    private void GeometryNumber(string key, string label, string help, float speed = .02f)
    {
        GeometryFieldLabel(key, label, help, out var p);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X * .62f); ImGui.SetNextItemWidth(-1);
        float value = (float)p.Value;
        if (ImGui.DragFloat("##" + key, ref value, speed, (float)p.Min, (float)p.Max, "%.2f " + p.Unit.Replace("%", "%%"), ImGuiSliderFlags.AlwaysClamp)) SetGeometry(key, Math.Round(value, 2));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(help + "\nHúzás: hangolás · Ctrl+kattintás: beírás");
    }

    private void GeometryCount(string key, string label, string help)
    {
        GeometryFieldLabel(key, label, help, out var p);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X * .62f);
        int count = (int)Math.Round(p.Value);
        for (int i = (int)p.Min; i <= (int)p.Max; i++) {
            if (i > (int)p.Min) ImGui.SameLine(0, 6);
            if (ImGui.RadioButton(i + "##" + key, count == i)) SetGeometry(key, i);
        }
    }

    private void GeometryChoice(string key, string help, params string[] options)
    {
        FindGeometry(key, out var rule, out int index);
        int current = (int)Math.Round(rule.Parameters[index].Value);
        for (int i = 0; i < options.Length; i++) {
            if (ImGui.RadioButton(options[i] + "##" + key, current == i)) SetGeometry(key, i);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        }
    }

    private static string GeometryUnitName(RigProfile profile, int unit)
    {
        int active = profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
        if (unit < active) return active == 1 ? "Jármű" : unit == 0 ? "Első váz" : "Hátsó váz";
        return unit == active + profile.Trailers.Length - 1 ? "Pótkocsi" : "Forgózsámoly";
    }

    private static Vec4 GeometryGroupColour(RigProfile profile, string axleId)
    {
        var groups = profile.Axles.Select(a => VehiclePathStudy.AxleGroup(a.Id)).Distinct().ToList();
        return GeometryColours[Math.Max(0, groups.IndexOf(VehiclePathStudy.AxleGroup(axleId))) % GeometryColours.Length];
    }
}
