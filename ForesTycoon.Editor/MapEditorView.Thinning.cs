using System;
using System.Linq;
using ForesTycoon.Ecology;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class MapEditorView
{
    private int previewTile = -1;
    private double previewYear;
    private ThinningPrescription previewSurvey;
    private bool previewOccupied;
    private (Vec2 Position, bool Marked)[] previewStems = Array.Empty<(Vec2, bool)>();

    private void DrawThinningTrial(GameWorld world)
    {
        ImGui.TextWrapped("Válassz csempét a térképen. A kijelölt fákat a processzor termeli ki.");
        ImGui.SliderInt("Körlapösszeg", ref context.Ui.Map.ThinningPercent, 10, 50, "%d%%");
        int percent = context.Ui.Map.ThinningPercent;
        bool valid = context.WorldCurrent() && selectedTile >= 0 && selectedTile < world.Map.Tiles.Count;
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("Előnézet")) {
            var preview = world.Logistics.PreviewThinning(selectedTile, percent);
            previewSurvey = preview.Survey; previewOccupied = preview.Occupied;
            previewTile = selectedTile; previewYear = world.Forest.ForestYear;
            previewStems = world.Forest.IndividualTrees.TryGet(selectedTile, out var patch)
                ? patch.Trees.Take(patch.Count).Select(t => (new Vec2(t.U, t.V), preview.Marks.Contains(t.Id))).ToArray()
                : Array.Empty<(Vec2, bool)>();
        }
        ImGui.SameLine();
        if (ImGui.Button("Gyérítés kijelölése")) {
            world.QueueThinForestArea(selectedTile, selectedTile, percent);
            previewTile = -1;
        }
        ImGui.EndDisabled();
        if (valid && previewTile == selectedTile && previewSurvey.TargetPercent == percent) {
            ImGui.TextUnformatted($"Előnézet · #{selectedTile} · {previewYear:F2}. erdőév");
            if (previewOccupied) ImGui.TextWrapped("A csempéhez már tartozik aktív kitermelési terület.");
            else if (previewSurvey.SurveyedTrees == 0) ImGui.TextWrapped("A csempén nincs gyérítésre kijelölhető állomány.");
            else {
                ImGui.TextWrapped(previewSurvey.Description);
                ImGui.TextUnformatted($"Kijelölt körlapösszeg {previewSurvey.MarkedBasalArea:F2} m²");
                float size = Math.Min(150, ImGui.GetContentRegionAvail().X);
                var origin = ImGui.GetCursorScreenPos();
                ImGui.Dummy(new Vec2(size));
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(origin, origin + new Vec2(size), 0xff273527);
                foreach (var stem in previewStems) {
                    var at = origin + stem.Position * size;
                    draw.AddCircleFilled(at, 3, 0xffa2c89a);
                    if (stem.Marked) draw.AddCircle(at, 5, 0xff51c6ff, 12, 2);
                }
                ImGui.TextUnformatted("Sárga gyűrű: kivágásra javasolt törzs");
            }
        }
        ImGui.TextWrapped(world.Logistics.Status);
    }
}
