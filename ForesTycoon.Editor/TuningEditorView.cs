using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The world pages: the generated and painted map, the types it is painted with (soil, species, surfaces), the vehicles'
/// data and game parameters, and the rain trial. <see cref="WorldTab"/> and <see cref="TypeTab"/> remain the entry points
/// other code uses to open a page.
/// </summary>
internal sealed class TuningEditorView(EditorPageContext context, CurrentRulesView currentRules)
{
    private bool Try(Action action) => context.Try(action);
    private void ApplyProject(GameWorld world, bool publish) => context.ApplyProject(world, publish);
    private string message { set => context.SetMessage(value); }
    private static bool IsVehicleRule(string rule) =>
        rule.StartsWith("vehicle.", StringComparison.Ordinal) || rule.StartsWith("machine.", StringComparison.Ordinal) || rule.StartsWith("upkeep.", StringComparison.Ordinal);

    internal void VehiclesTab(GameWorld world)
    {
        EditorStyle.BeginPanel("vehicles", Vec2.Zero);
        currentRules.DrawVehicleWorkspace(world, rule => IsVehicleRule(rule) && !rule.Contains("geometry", StringComparison.Ordinal) && rule != "vehicle.modelBinding",
            () => Try(() => ApplyProject(world, false)));
        EditorStyle.EndPanel();
    }

    internal void ParametersTab(GameWorld world)
    {
        EditorStyle.BeginPanel("parameters", Vec2.Zero);
        currentRules.DrawTuning(world, rule => !IsVehicleRule(rule));
        if (currentRules.UnappliedTuning(world) > 0 && EditorStyle.PrimaryButton("Paraméterek alkalmazása")) Try(() => ApplyProject(world, false));
        EditorStyle.EndPanel();
    }

    private static readonly (string Name, string Help)[] SurfaceNames = {
        ("Aszfalt", "Burkolt út: drága, alig kopik és romlik."), ("Makadám", "Zúzottkő út: olcsóbb, esőben gyorsan romlik."),
        ("Földút", "Épített földút: a járhatóságát a talaj és a talajvíz adja."), ("Közelítőnyom", "Ideiglenes nyom a gépeknek; a forgalom vájja, idővel benő.") };
    private static readonly (string Label, string Effect, string[] Keys)[] SurfaceRows = {
        ("Építési költség (eFt/csempe)", "Gazdaság: kiadás építéskor.", new[] { "AsphaltBuild", "MacadamBuild", "DirtBuild", "TrailCost" }),
        ("Forgalmi kopás szorzója", "Útkopás: mennyit veszít az állapotából áthaladásonként (nyomnál az útkopás többszöröse).", new[] { "AsphaltFactor", "MacadamFactor", null, "TrailLoadMultiplier" }),
        ("Romlás szárazon (állapot/év)", "Időjárási útromlás csapadék nélkül.", new[] { "AsphaltWeather", "MacadamWeather", "DirtWeather", null }),
        ("Romlás esőben (állapot/év/eső)", "Időjárási útromlás az eső erősségével.", new[] { "AsphaltRainWeather", "MacadamRainWeather", "DirtRainWeather", null }),
        ("Gördülési ellenállás (Crr)", "Jármű: menetellenállás és fogyasztás. A földút és a nyom közös értéke.", new[] { "RollingAsphalt", "RollingGravel", "RollingDirt", "RollingDirt" }),
        ("Javítás az építési ár arányában", "Gazdaság: útjavítás költsége teljes kárnál (minden épített útnál közös).", new[] { "RoadRepairShare", "RoadRepairShare", "RoadRepairShare", null }),
        ("Faanyag-elérési távolság (m)", "Vízszintes puffer: az elérhető lábonálló faanyag becslése, átfedésnél egyszer számolva.", new[] { "AsphaltBufferMetres", "MacadamBufferMetres", "DirtBufferMetres", "TrailBufferMetres" }),
        ("Benövési idő (erdőév)", "A használatlan nyom ennyi idő alatt nő be.", new[] { null, null, null, "SkidTrailOvergrowYears" }) };

    internal void SurfacesTab(GameWorld world)
    {
        EditorStyle.BeginPanel("surfaces", new Vec2(0, 0));
        EditorStyle.Title("Burkolatok");
        ImGui.TextWrapped("Az utak és nyomok paraméterei. A futó játékra is azonnal hatnak az alkalmazás után. A földút járhatóságát a Szabályok › Földút járhatósága képlet adja a talaj és a talajvíz alapján.");
        bool changed = false;
        if (ImGui.BeginTable("surface-table", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV)) {
            ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthFixed, 230);
            foreach (var (name, _) in SurfaceNames) ImGui.TableSetupColumn(name, ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Hol hat");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); EditorStyle.Text(HudTheme.Muted, "");
            for (int i = 0; i < SurfaceNames.Length; i++) {
                ImGui.TableSetColumnIndex(i + 1); EditorStyle.Title(SurfaceNames[i].Name);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(SurfaceNames[i].Help);
            }
            foreach (var (label, effect, keys) in SurfaceRows) {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
                for (int i = 0; i < keys.Length; i++) {
                    ImGui.TableSetColumnIndex(i + 1);
                    if (keys[i] == null) { ImGui.AlignTextToFramePadding(); ImGui.TextDisabled("—"); }
                    else if (currentRules.TuningField(keys[i], label + i)) changed = true;
                }
                ImGui.TableSetColumnIndex(5); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, effect); ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }
        if (changed) { if (Try(() => ApplyProject(world, false))) message = "Burkolat módosítva; a tesztvilágban már ez érvényes."; }
        ImGui.EndChild();
    }

}
