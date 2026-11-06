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
    /// Composition host for editor pages, project lifecycle, navigation and the preview world.
    /// </summary>
    internal sealed partial class RuleEditorView
    {

        private readonly EditorDocument document = new();
        private readonly EditorUiState ui = new();
        private readonly SoilEditorView soilEditor;
        private readonly SpeciesEditorView speciesEditor;
        private readonly TreeShapeEditorView treeEditor;
        private readonly MapEditorView mapEditor;
        private readonly BiomeEditorView biomeEditor;
        private readonly MissionEditorView missionEditor;
        private readonly TuningEditorView tuningEditor;
        private readonly TrialEditorView trialEditor;
        private string message = "";
        private string projectLoadError;
        internal string ProjectLoadError => projectLoadError;
        private readonly CurrentRulesView currentRules;
        private readonly BehaviorEditorView behaviors;
        private readonly SandboxSession sandbox = new();
        internal void AdvanceSandbox(GameWorld world, double elapsed)
        { RefreshProjectOperations(world); sandbox.Advance(world, elapsed); }

        internal RuleEditorView(string modelPath = null)
        {
            workflow = new(RuleProject.DefaultPath, WorldLoader.OpeningScenePath()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "scenes", "opening.json"));
            var context = new EditorPageContext(new SceneSession(document.Scene), ui, ApplyScenario, ApplyProject, Try, text => message = text,
                ShowPage, () => appliedScenario == ScenarioKey(), document.Capture, document.Revision);
            soilEditor = new(context); treeEditor = new(context); speciesEditor = new(context, treeEditor);
            mapEditor = new(context);
            biomeEditor = new(context, mapEditor); missionEditor = new(context); trialEditor = new(context, mapEditor);
            currentRules = new(document.Tuning, document.Scene.SetTuning, ui.SystemMap, ui.Vehicles, document.Library.Notes, document.Library.SetNotes);
            tuningEditor = new(context, currentRules);
            behaviors = new(document.Behaviors, ui.Behaviors);
            dynamicsEditor = new(document.Dynamics, ui.Dynamics, ui.FlowEquation);
            ruleWorkspace = new(new(context, new RuleBookSession(document), new RuleLibrarySession(document.Library), behaviors, dynamicsEditor, () => projectInitialized, () => projectPath, () => liveStatus));
            if (modelPath == null) return;
            string path = Path.GetFullPath(modelPath);
            projectPath = RuleWorkshopModel.PublishedPath(path);
            try {
            var source = workflow.ReadSource(path);
            if (source.Project != null) { pendingProject = source.Project; return; }
            if (source.Behaviors != null) { behaviors.Load(source.Behaviors); return; }
            if (source.Catalog != null) {
                pendingTuning = source.Catalog.TuningOverrides();
                currentRules.Load(source.Catalog, path);
            }
            } catch (Exception e) when (Expected(e)) { projectLoadError = e.Message; }
        }

        internal void Draw(GameWorld world, ref bool open, bool standalone = false)
        {
            RefreshProjectOperations(world);
            if (!open) return;
            EnsureProject(world);
            var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            if (standalone) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar;
            // The shell draws its own bars edge to edge; pages get their padding from the body.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vec2.Zero);
            bool visible = ImGui.Begin("Világszerkesztő", ref open, flags);
            ImGui.PopStyleVar();
            if (!visible) { ImGui.End(); return; }
            currentRules.EnsureCatalog(world);
            RefreshWorkshopRevision(world);
            Shell(world, standalone);
            ImGui.End();
        }

        private bool Try(Action action) { try { action(); return true; } catch (Exception e) when (Expected(e)) { message = e.Message; return false; } }
        private static bool Expected(Exception e) => EditorErrors.Expected(e);
    }
}
