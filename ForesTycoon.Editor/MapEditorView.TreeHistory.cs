using System;
using ForesTycoon.Ecology;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class MapEditorView
{
    private readonly ForestTreeHistory treeHistory = new();
    private bool inspectTrees;
    private ulong inspectedTree;
    private int monthsBack;
    private ForestSystem inspectedForest;
    internal void Observe(ForestSystem forest)
    {
        if (ReferenceEquals(inspectedForest, forest)) return;
        if (inspectedForest != null) inspectedForest.ObservationReset -= ResetTreeSelection;
        inspectedForest = forest;
        if (forest != null) forest.ObservationReset += ResetTreeSelection;
        ResetTreeSelection();
        treeHistory.Observe(forest);
    }

    private void ResetTreeSelection() { inspectedTree = 0; monthsBack = 0; previewTile = -1; }

    private void SelectNearestTree(GameWorld world, int tile, float u, float v)
    {
        inspectedTree = 0; monthsBack = 0;
        if (!world.Forest.IndividualTrees.TryGet(tile, out var patch)) return;
        var cell = world.Forest.Habitat.GetForestTileGeometry(tile);
        float best = float.PositiveInfinity;
        for (int i = 0; i < patch.Count; i++) {
            var tree = patch.Trees[i];
            float x = (tree.U - u) * cell.Width, y = (tree.V - v) * cell.Height;
            float distance = x * x + y * y;
            if (distance < best) { best = distance; inspectedTree = tree.Id; }
        }
    }

    private void DrawTreeHistory(GameWorld world)
    {
        ImGui.TextWrapped("Kattints egy fára az erdőtérképen, vagy válassz törzset a kijelölt csempéről.");
        bool hasTrees = world.Forest.IndividualTrees.TryGet(selectedTile, out var patch) && patch.Count > 0;
        if (hasTrees) {
            float size = Math.Min(160, ImGui.GetContentRegionAvail().X);
            var origin = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##törzstérkép", new Vec2(size));
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, origin + new Vec2(size), 0xff273527);
            for (int i = 0; i < patch.Count; i++) {
                var t = patch.Trees[i];
                draw.AddCircleFilled(origin + size * new Vec2(t.U, t.V), t.Id == inspectedTree ? 5 : 3,
                    t.Id == inspectedTree ? 0xff51c6ff : 0xffa2c89a);
            }
            if (ImGui.IsItemClicked()) {
                var position = (ImGui.GetMousePos() - origin) / size;
                SelectNearestTree(world, selectedTile, position.X, position.Y);
            }
        }
        if (hasTrees &&
            ImGui.BeginCombo("Törzs", inspectedTree == 0 ? "Válassz" : $"#{inspectedTree}")) {
            for (int i = 0; i < patch.Count; i++) {
                var t = patch.Trees[i];
                if (ImGui.Selectable($"#{t.Id} · {t.At(world.Forest.ForestYear).Diameter * 100:F1} cm", inspectedTree == t.Id))
                    { inspectedTree = t.Id; monthsBack = 0; }
            }
            ImGui.EndCombo();
        }
        if (inspectedTree == 0) return;
        Span<TreeMonthSample> rows = stackalloc TreeMonthSample[ForestTreeHistory.Months];
        int count = treeHistory.Read(inspectedTree, rows);
        if (count == 0) { ImGui.TextWrapped("Az első havi adat a következő hónapzáráskor jelenik meg."); return; }
        monthsBack = Math.Clamp(monthsBack, 0, count - 1);
        if (count > 1) {
            Span<float> values = stackalloc float[ForestTreeHistory.Months];
            for (int i = 0; i < count; i++) values[i] = rows[i].Resources.Water;
            ImGui.PlotLines("Vízválasz", ref values[0], count, 0, null, 0, 1, new Vec2(155, 38));
            for (int i = 0; i < count; i++) values[i] = rows[i].StressYears;
            ImGui.PlotLines("Stressz (év)", ref values[0], count, 0, null, 0, float.MaxValue, new Vec2(155, 38));
            ImGui.SliderInt("Hónap az utolsó adat előtt", ref monthsBack, 0, count - 1);
        }
        int index = count - 1 - monthsBack;
        var sample = rows[index];
        ImGui.TextUnformatted($"#{sample.Id} · {sample.Month}. erdőhónap{(sample.Dead ? " · elhalt" : "")}");
        ImGui.TextUnformatted($"Gyökérzónavíz {sample.RootWaterMm:F1} mm");
        ImGui.TextUnformatted($"Havi felvehető víz {sample.Supply:P0} · vízválasz {sample.Resources.Water:F2}");
        ImGui.TextUnformatted($"Relatív fény {sample.Resources.Light:F2} · tér {sample.Resources.Space:F2}");
        ImGui.TextUnformatted($"Egészség {sample.Health:F2}");
        ImGui.TextUnformatted($"Stressz {sample.StressYears:F2} év · átmérő {sample.DiameterMetres * 100:F1} cm");
        if (index > 0) ImGui.TextUnformatted($"Havi átmérőnövekedés {(sample.DiameterMetres - rows[index-1].DiameterMetres) * 1000:F2} mm");
    }
}
