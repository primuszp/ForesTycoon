using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
using ImGuiNET;
namespace ForesTycoon.Editor;
internal sealed record RuleWorkspaceContext(EditorPageContext Page, RuleBookSession Book, RuleLibrarySession Library, BehaviorEditorView Behaviors, DynamicsEditorView Dynamics,
    Func<bool> Initialized, Func<string> ProjectPath, Func<string> LiveStatus);

/// <summary>Rule authoring workspace. Owns rule-page presentation and composes graph, controller and stock views.</summary>
internal sealed partial class RuleWorkspaceView
{
    private readonly RuleWorkspaceContext context;
    private readonly RuleLibraryView library;
    private readonly RuleBookView book;
    private readonly RuleCardWidgets cards;
    private EditorUiState ui => context.Page.Ui;
    private BehaviorEditorView behaviors => context.Behaviors;
    private DynamicsEditorView dynamicsEditor => context.Dynamics;
    private ref EditorPage page => ref ui.Page;
    private Dictionary<EditorPage, string> lastCommon => ui.LastCommon;
    private string message { set => context.Page.SetMessage(value); }
    private string projectPath => context.ProjectPath();
    private string liveStatus => context.LiveStatus();
    private bool projectInitialized => context.Initialized();
    private void ShowPage(EditorPage target) => context.Page.Navigate(target);
    private void ApplyProject(GameWorld world, bool publish) => context.Page.ApplyProject(world, publish);
    private bool Try(Action action) => context.Page.Try(action);
    private static bool Expected(Exception error) => EditorErrors.Expected(error);
    private RuleProject CollectProject() => context.Page.Capture();
    internal int ProcessCount => System.Linq.Enumerable.Count(ProcessGraphs());

    internal RuleWorkspaceView(RuleWorkspaceContext context)
    {
        this.context = context;
        fields = new(ui.Rules);
        library = new(new(context.Library, behaviors, ui.Rules, ShowCommonRule, context.Page.Try, context.Page.SetMessage));
        overview = new(context.Book, ui.Rules, ShowRuleBook, ShowRuleOverview);
        objects = new(new(context.Book, ui.Rules, ShowRuleBook, () => Try(() => {
            RuleProject.WriteAtomic(projectPath + ".world.request", "refresh"); message = "Játékpillanatkép kérése elküldve.";
        }), context.LiveStatus, context.Page.SetMessage));
        cards = new(ui.Rules);
        book = new(new(context.Book, ui.Rules, ui.Expression, context.Page.Capture, context.Initialized, context.Page.ApplyProject,
            context.Page.SetMessage, context.Page.Navigate, () => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(dynamicsEditor.Stocks, stock => stock.Id)),
            () => dynamicsEditor.NamedValues(), () => pendingCommonChange = true), fields, curves, overview, cards);
        curves.Bind(behaviors); curves.Bind(dynamicsEditor.EquationEditor);
    }
    internal void ShowRuleLibrary() => ShowCommonRule("library");
    internal void ShowThematicStock(string id, int topic) { context.Library.SetTopic("stock." + id, topic); ShowCommonRule("stock:" + id); }
    internal void RestorePageSelection(EditorPage target)
    {
        if (commonSelection == null || CommonPage(commonSelection) != target)
            commonSelection = target == EditorPage.Library ? "library" : lastCommon.GetValueOrDefault(target);
    }
    internal void Reset(RuleProject project) { book.Reset(project); activeCommonKey = null; }
    internal void ResetCalculation() { book.ResetCalculation(); activeCommonKey = null; }
    internal void CommitEdits() { behaviors.CommitEdit(); book.CommitEdit(); dynamicsEditor.EquationEditor.CommitEdit(); }
    internal void StoreTemplate(BehaviorGraph graph) => context.Library.Store(graph);
    internal void StoreTemplate(BehaviorController controller) => context.Library.Store(controller);
    internal void FlushChanges(GameWorld world)
    {
        if (!pendingCommonChange || world.BoundaryPending || ImGui.IsAnyItemActive()) return;
        Try(() => ApplyProject(world, false)); pendingCommonChange = false;
    }
    internal void Draw(GameWorld world)
    {
        switch (page) {
            case EditorPage.Objects: objects.Draw(world); break;
            case EditorPage.Rules: book.Draw(world); break;
            case EditorPage.Responses: CommonListPage(world, ResponseList, "Válassz egy összefüggést a listából."); break;
            case EditorPage.Processes: CommonListPage(world, ProcessList, "Válassz egy folyamatkapcsolatot, vagy köss újat a játék egy folyamatába."); break;
            case EditorPage.Controllers: CommonListPage(world, ControllerList, "Válassz egy munkaciklust."); break;
            case EditorPage.Stocks: CommonListPage(world, StockList, "Még nincs saját készlet. Az „+ Készlet” gomb indít egyet."); break;
            case EditorPage.Library: DrawCommonSelection(world); break;
            case EditorPage.Overview: overview.Draw(); break;
        }
    }
}
