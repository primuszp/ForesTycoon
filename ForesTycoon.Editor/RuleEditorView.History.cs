using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

/// <summary>The view handles input and preview updates; the document owns snapshots, transactions and navigation.</summary>
internal sealed partial class RuleEditorView
{
    private EditorDocumentHistory history => document.History;

    private void TrackHistory(GameWorld world)
    {
        if (ImGui.IsAnyItemActive() || ImGui.IsMouseDown(ImGuiMouseButton.Left) || stroke.Count > 0) return;
        history.Checkpoint();
    }

    private void RecordHistory(GameWorld world) => history.Checkpoint(force: true);
    private static string MapPart(string scenarioJson) => EditorDocumentHistory.MapPart(scenarioJson);

    private void ApplyHistory(GameWorld world, EditorDocumentRestore restored, string verb)
    {
        if (restored == null) return;
        if (document.World != null) seedEdit = document.World.Seed;
        ruleWorkspace.ResetCalculation();
        MarkDocumentChanged();
        if (restored.RebuildWorld) ApplyScenario(world, verb + ": " + restored.Summary + ".");
        else if (Try(() => ApplyProject(world, false))) message = verb + ": " + restored.Summary + ".";
    }

    private void CompleteEdits()
    {
        ruleWorkspace.CommitEdits();
        history.CommitEdit();
    }

    internal WorldScenario EditedScenario => document.World;
    internal void SetTuningForTest(string key, double value)
        => history.Edit("szám módosítása", () => document.Tuning.Set(key, value));
    internal bool CanUndo => history.CanUndo;
    internal bool CanRedo => history.CanRedo;
    internal void Undo(GameWorld world)
    {
        if (stroke.Count > 0) { CancelStroke(); return; }
        CompleteEdits(); ApplyHistory(world, history.Undo(), "Visszavonva");
    }
    internal void Redo(GameWorld world)
    {
        if (stroke.Count > 0) { CancelStroke(); return; }
        CompleteEdits(); ApplyHistory(world, history.Redo(), "Újra");
    }

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
            ImGui.SetTooltip(CanUndo ? "Visszavonás · Ctrl+Z: " + history.UndoSummary : "Nincs visszavonható lépés.");
        ImGui.SameLine();
        ImGui.BeginDisabled(!CanRedo);
        if (ImGui.Button("Újra")) Redo(world);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(CanRedo ? "Újra · Ctrl+Y: " + history.RedoSummary : "Nincs újra végrehajtható lépés.");
    }
}
