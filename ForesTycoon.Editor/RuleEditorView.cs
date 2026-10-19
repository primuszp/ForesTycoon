using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using ForesTycoon.Rules;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor
{
    /// <summary>
    /// Shared rule workshop host; all world rules use the rule book and influence graph partials.
    /// </summary>
    internal sealed partial class RuleEditorView
    {

        private string message = "";
        private string projectLoadError;
        internal string ProjectLoadError => projectLoadError;
        private readonly CurrentRulesView currentRules = new();
        private readonly BehaviorEditorView behaviors = new();
        internal void AdvanceSandbox(GameWorld world, double elapsed) => behaviors.Advance(world, elapsed);

        internal RuleEditorView(string modelPath = null)
        {
            foreach (var editor in new[] { behaviors, expressionEditor, dynamicsEditor.EquationEditor }) {
                editor.EditCurve = (id, curve) => {
                    bool finished = false;
                    return CurveEditor(id, curve, "world", Array.Empty<string>(), ref finished, bindings: false);
                };
                editor.EditNumber = (label, value, set) => {
                    bool finished = false;
                    if (CurveNumber(label, ref value, Math.Min(0, value * 2), Math.Max(1, value * 2), ref finished)) set(value);
                };
            }
            if (modelPath == null) return;
            string path = Path.GetFullPath(modelPath);
            projectPath = RuleWorkshopModel.PublishedPath(path);
            try {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("schema", out var projectSchema) && projectSchema.GetString() == "forest-rule-project")
            { pendingProject = RuleProject.FromJson(document.RootElement.GetRawText()); projectPath = RuleWorkshopModel.PublishedPath(path); return; }
            if (document.RootElement.TryGetProperty("schema", out var behaviorSchema) && behaviorSchema.GetString() == "forest-behaviors")
            { behaviors.Load(BehaviorModel.FromJson(document.RootElement.GetRawText()), path); return; }
            if (document.RootElement.TryGetProperty("schema", out var schema) && schema.GetString() == "forest-current-rules")
            { currentRules.Load(GameRuleCatalog.FromJson(document.RootElement.GetRawText()), path); return; }
            throw new InvalidDataException("Nyiss meg egy teljes szabályprojektet; a külön útkopási modell megszűnt.");
            } catch (Exception e) when (Expected(e)) { projectLoadError = e.Message; }
        }

        internal void Draw(GameWorld world, ref bool open, bool standalone = false)
        {
            if (!open) return;
            EnsureProject(world);
            var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            if (standalone) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar;
            if (!standalone) ImGui.SetNextWindowSize(new Vec2(1280, 820), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Világszerkesztő", ref open, flags)) { ImGui.End(); return; }
            RefreshProjectOperations(); RefreshSceneSave();
            if (projectLoadError != null) {
                ImGui.TextWrapped("A projektet nem sikerült megnyitni: " + projectLoadError);
                ImGui.TextWrapped("A fájl megmaradt; az Editor a tesztvilág aktuális szabályait mutatja.");
            }
            currentRules.EnsureCatalog(world);
            RefreshWorkshopRevision(world);
            WorldHeader(world);
            float statusHeight = ImGui.GetFrameHeightWithSpacing() + 6;
            ImGui.BeginChild("editor-body", new Vec2(0, -statusHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            if (sceneSaveTask != null) ImGui.TextUnformatted("Jelenet ellenőrzése és mentése…");
            else if (world.BoundaryPending) ImGui.TextUnformatted(world.GenerationPending ? world.GenerationStage + "…" : "Szimulációs vagy szerkesztési munka folyamatban…");
            else WorldBody(world, standalone);
            if (pendingCommonChange && !world.BoundaryPending && !ImGui.IsAnyItemActive()) {
                Try(() => ApplyProject(world, false)); pendingCommonChange = false;
            }
            if (world.GenerationCanCancel && ImGui.Button("Pályaépítés megszakítása")) world.CancelGeneration();
            if (!string.IsNullOrEmpty(world.GenerationError)) ImGui.TextWrapped("A pályaépítés nem sikerült: " + world.GenerationError);
            ImGui.EndChild();
            WorkshopStatus();
            ImGui.End();
        }

        private static bool ViewTab(string label, bool active, string tooltip)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, active ? HudTheme.Moss : new Vec4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, active ? HudTheme.Parchment : HudTheme.Muted);
            bool clicked = ImGui.Button(label);
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
            return clicked;
        }

        private void Try(Action action) { try { action(); } catch (Exception e) when (Expected(e)) { message = e.Message; } }
        private static bool Expected(Exception e) => e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException;
    }
}
