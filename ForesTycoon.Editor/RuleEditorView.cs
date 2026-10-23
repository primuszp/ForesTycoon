using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using ForesTycoon.Rules;
using Vec2 = System.Numerics.Vector2;

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
            { behaviors.Load(BehaviorModel.FromJson(document.RootElement.GetRawText())); return; }
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
            // The shell draws its own bars edge to edge; pages get their padding from the body.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vec2.Zero);
            bool visible = ImGui.Begin("Világszerkesztő", ref open, flags);
            ImGui.PopStyleVar();
            if (!visible) { ImGui.End(); return; }
            RefreshProjectOperations(); RefreshSceneSave();
            currentRules.EnsureCatalog(world);
            RefreshWorkshopRevision(world);
            Shell(world, standalone);
            ImGui.End();
        }

        private void Try(Action action) { try { action(); } catch (Exception e) when (Expected(e)) { message = e.Message; } }
        private static bool Expected(Exception e) => e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException;
    }
}
