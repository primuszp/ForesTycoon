using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor;
/// <summary>The rule book's list, effect graph, inspector and calculation editor.</summary>
internal sealed partial class RuleBookView
{
    private readonly RuleBookContext context;
    private readonly RuleFieldPickerView fields;
    private readonly RuleCurveEditorView curves;
    private readonly RuleOverviewView overview;
    private readonly RuleCardWidgets cards;
    private readonly BehaviorEditorView expressionEditor;
    private RuleWorkspaceUiState state => context.State;
    private const float ListWidth = RuleCardWidgets.ListWidth;
    private bool projectInitialized => context.Initialized();
    private string message { set => context.SetMessage(value); }
    private ref bool scrollRuleCard => ref state.ScrollRuleCard;
    private void ShowPage(EditorPage target) => context.Navigate(target);
    private void ApplyProject(GameWorld world, bool publish) => context.ApplyProject(world, publish);
    private static bool Expected(Exception error) => EditorErrors.Expected(error);
    private RuleProject CollectProject() => context.Capture();
    internal RuleBookView(RuleBookContext context, RuleFieldPickerView fields, RuleCurveEditorView curves, RuleOverviewView overview, RuleCardWidgets cards)
    {
        this.context = context; this.fields = fields; this.curves = curves; this.overview = overview; this.cards = cards;
        expressionEditor = new(ui: context.CalculationUi); curves.Bind(expressionEditor);
    }
    internal void Reset(RuleProject project) { LoadRuleBook(project); ResetCalculation(); }
    internal void ResetCalculation() { ruleBookError = null; expressionKey = null; }
    internal void CommitEdit() => expressionEditor.CommitEdit();
    private IReadOnlyList<RuleFieldUse> FieldUses(IReadOnlyList<RuleView> rules) => context.Book.FieldUses();
    private void OtherWriters(GameWorld world, RuleView rule) => overview.DrawSharedWriters(rule);
    private bool ObjectFieldPicker(string label, string scope, string current, string[] choices, Action<string> set, bool empty = false)
        => fields.Draw(label, scope, current, choices, set, empty);
    private string[] ReadableFields(GameWorld world, RuleScope scope) => context.Book.ReadableFields(scope, context.StockIds());
    private string[] WritableFields(GameWorld world, RuleScope scope) => context.Book.WritableFields(scope);
    private bool CurveEditor(string id, RuleCurve curve, string scope, IReadOnlyList<string> inputs, ref bool finished, bool bindings = true)
        => curves.Draw(id, curve, scope, inputs, ref finished, bindings);
    private bool CurveNumber(string label, ref double value, double min, double max, ref bool finished)
        => curves.Number(label, ref value, min, max, ref finished);
    private bool RuleCardEntry(string id, string name, string kind, bool selected, bool enabled, Action select, Action<bool> enable, string tooltip)
        => cards.Entry(id, name, kind, selected, enabled, select, enable, tooltip);
}
