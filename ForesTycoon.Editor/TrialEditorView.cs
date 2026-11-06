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
internal sealed class TrialEditorView(EditorPageContext context, MapEditorView map)
{
    internal TrialService Service { get; } = new();
    private TrialService trials => Service;
    private SceneSession scene => context.Scene;
    private ref int trialIntensity => ref context.Ui.Trial.Intensity;
    private ref int trialSeconds => ref context.Ui.Trial.Seconds;
    private bool Try(Action action) => context.Try(action);
    // ── Trial ────────────────────────────────────────────────────────────
    internal void Draw(GameWorld world)
    {
        EditorStyle.BeginPanel("trial-settings", new Vec2(300, 0));
        EditorStyle.Section("Esős próba");
        ImGui.TextWrapped("Két világ épül ugyanebből a pályából: az egyik a játék alapszabályaival, a másik a te szabályaiddal. Mindkettőre ugyanannyi eső esik.");
        ImGui.SetNextItemWidth(-90); ImGui.SliderInt("Eső", ref trialIntensity, 0, 40);
        if (scene.World.Springs.Count > 0) EditorStyle.Text(HudTheme.Muted, $"A pálya {scene.World.Springs.Count} forrása is folyik. 0 eső: csak a források.");
        ImGui.SetNextItemWidth(-90); ImGui.SliderInt("Időtartam (s)", ref trialSeconds, 10, 180);
        ImGui.TextWrapped($"{trialSeconds} játékmásodperc, kb. {trialSeconds / 900.0 * 12:0.#} erdőhónap.");
        bool busy = trials.IsBusy;
        if (EditorStyle.PrimaryButton(busy ? "Fut…" : "Próba indítása", !busy)) Try(() => {
            var project = context.Capture();
            var baseline = GameWorld.DefaultRuleProject(); baseline.World = project.World;
            trials.Start(baseline, project, trialIntensity, trialSeconds, context.Revision());
        });
        if (trials.Error != null) EditorStyle.Text(HudTheme.Bad, "A próba hibát jelzett: " + trials.Error);
        var (columns, rows) = world.TileGrid;
        var result = trials.ResultFor(columns, rows);
        if (result != null) { EditorStyle.Section("Eredmény"); ImGui.TextWrapped(result.Summary); }
        ImGui.EndChild(); ImGui.SameLine();

        EditorStyle.BeginPanel("trial-maps", new Vec2(0, 0), ImGuiWindowFlags.NoScrollbar);
        if (result == null) { ImGui.TextWrapped("Indítsd el a próbát: itt jelenik meg a járhatóság az eső végén, az alapszabályokkal és a tieiddel."); ImGui.EndChild(); return; }
        var r = result;
        float width = (ImGui.GetContentRegionAvail().X - 20) / 3;
        var mapSize = new Vec2(width, Math.Min(width * rows / Math.Max(1, columns), ImGui.GetContentRegionAvail().Y - 80));
        void Panel(string title, Func<int, Vec4> colour, string id) {
            ImGui.BeginGroup(); EditorStyle.Text(HudTheme.Parchment, title);
            map.DrawTiles(world, id, colour, false, mapSize); ImGui.EndGroup();
        }
        Panel("Felszíni víz (elöntés) a végén", t => world.IsWaterTile(t) ? new(0.25f, 0.50f, 0.90f, 1)
            : Mix(new(0.30f, 0.27f, 0.20f, 1), new(0.30f, 0.62f, 0.95f, 1), (float)Math.Sqrt(Math.Clamp((r.Water[t] - 0.5) / 100, 0, 1))), "trial-water"); ImGui.SameLine(0, 10);
        Panel("Járhatóság · alapszabály", t => r.Unpaved[t] ? TrafficColour(r.Baseline[t]) : new(0.45f, 0.45f, 0.45f, 1), "trial-base"); ImGui.SameLine(0, 10);
        Panel("Járhatóság · a te szabályaid", t => r.Unpaved[t] ? TrafficColour(r.Edited[t]) : new(0.45f, 0.45f, 0.45f, 1), "trial-edit");
        MapLegendTraffic();
        ImGui.EndChild();
    }

}
