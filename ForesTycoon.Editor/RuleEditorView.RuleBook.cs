using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The rule book: a rule reads as a sentence – WHEN · WHERE · IF · THEN – and is run by the game. Every rule is the
/// project's own: a starter rule only seeds a new project and can be edited, switched off, reset to its starter or deleted.
/// </summary>
internal sealed partial class RuleEditorView
{
    private List<WorldRule> ruleBook;
    private List<RuleFieldDefinition> ruleFields;
    private List<string> removedStarters;
    private int selectedBookRule;
    private int? initialBookSelection;
    private string ruleBookError, ruleFilter = "";
    private RuleFieldDefinition newField = new() { Name = "saját.", Scope = "tile" };

    private void LoadRuleBook(RuleProject project)
    {
        removedStarters = (project.RemovedStarters ?? new()).ToList();
        ruleBook = RuleEngine.Clone(project.Rules ?? new()); RuleEngine.AddMissingStarters(ruleBook, removedStarters);
        ruleFields = RuleEngine.CloneFields(project.Fields ?? new());
        selectedBookRule = Math.Clamp(initialBookSelection ?? 0, 0, Math.Max(0, ruleBook.Count - 1)); initialBookSelection = null; ruleBookError = null;
    }
    private List<WorldRule> RuleBookRules(GameWorld world) => ruleBook ??= RuleEngine.Clone(world.RuleProjectDocument.Rules);
    private List<RuleFieldDefinition> RuleBookFields(GameWorld world) => ruleFields ??= RuleEngine.CloneFields(world.RuleProjectDocument.Fields);
    private List<string> RemovedStarters(GameWorld world) => removedStarters ??= world.RuleProjectDocument.RemovedStarters.ToList();

    /// <summary>Deletes a rule. A starter it was made from is remembered, so loading the project does not add it back.</summary>
    private void DeleteRule(GameWorld world, WorldRule rule)
    {
        var rules = RuleBookRules(world); var removed = RemovedStarters(world);
        rules.Remove(rule);
        if (rule.Template.Length > 0 && !rules.Exists(r => r.Template == rule.Template) && !removed.Contains(rule.Template)) removed.Add(rule.Template);
        selectedBookRule = Math.Clamp(selectedBookRule, 0, Math.Max(0, rules.Count - 1));
        ApplyRuleBook(world, rule.Name + ": törölve.");
    }

    /// <summary>Adds a starter rule as a new own rule, with a fresh id when the starter's is taken.</summary>
    private void AddStarterRule(GameWorld world, WorldRule starter)
    {
        var rules = RuleBookRules(world);
        if (rules.Exists(r => r.Id == starter.Id)) starter.Id = starter.Id + "-" + Guid.NewGuid().ToString("N")[..4];
        RemovedStarters(world).Remove(starter.Template);
        rules.Add(starter); selectedBookRule = rules.Count - 1; selectedInfluence = selectedTerm = 0; editRuleCalculation = false; page = EditorPage.Rules;
        ApplyRuleBook(world, starter.Name + ": hozzáadva a mintából.");
    }
    internal void ShowRuleBook(int index = 0) { scrollRuleCard = true; page = EditorPage.Rules; ruleCardTab = 0; selectedBookRule = index; editRuleCalculation = false; if (!projectInitialized) initialBookSelection = index; }

    /// <summary>Checks the whole book as the game will; on success the test world runs it at once.</summary>
    private void ApplyRuleBook(GameWorld world, string what)
    {
        try {
            _ = new RuleEngine(RuleEngine.Clone(ruleBook), RuleEngine.CloneFields(ruleFields), dynamicsEditor.Draft.Stocks.Select(s => s.Id), _ => 0);
            ruleBookError = null;
            Try(() => ApplyProject(world, false));
            message = what + " A tesztvilágban már ez fut.";
        } catch (InvalidDataException e) { ruleBookError = e.Message; }
    }

    private const string ScheduleHelp = "Időszakos: 0,5 játékmásodpercenként. Havi: erdőhónap végén. Áthaladási: jármű belépésekor. "
        + "Állati: állati lépésenként. Állomány: élőhelyfelmérés után. Létrehozáskor: az új egyedeken. Azonos indításnál fentről lefelé haladnak.";

    private void RuleBookArea(GameWorld world)
    {
        var rules = RuleBookRules(world); var fields = RuleBookFields(world);
        selectedBookRule = Math.Clamp(selectedBookRule, 0, Math.Max(0, rules.Count - 1));

        // ── Left: the book ───────────────────────────────────────────────
        EditorStyle.BeginPanel("book-list", new Vec2(ListWidth, 0));
        if (EditorStyle.PrimaryButton("+ Új szabály")) ImGui.OpenPopup("new-rule");
        ImGui.SameLine();
        RuleGroupsToolbar();
        if (ImGui.BeginPopup("new-rule")) {
            if (ImGui.Selectable("Üres szabály")) {
                int n = rules.Count + 1;
                while (rules.Exists(r => r.Name == $"Szabály {n}")) n++;
                // The first kind of object, in the object model's order, that a rule can change.
                var first = RuleEngine.Scopes.First(s => s.Fields.Any(f => f.Write != null));
                rules.Add(new WorldRule { Id = $"rule-{Guid.NewGuid():N}", Name = $"Szabály {n}", Scope = first.Id, Enabled = false,
                    Effects = { new() { Target = first.Fields.First(f => f.Write != null).Name, Mode = "add", Influence = new() } } });
                selectedBookRule = rules.Count - 1; editRuleCalculation = false;
                message = "Új szabály: válaszd ki, hol fusson, és adj hozzá hatást.";
            }
            ImGui.Separator();
            EditorStyle.Text(HudTheme.Muted, "Mintából");
            foreach (var template in RuleEngine.StarterRules()) {
                if (ImGui.Selectable(template.Name + "##starter-" + template.Id)) AddStarterRule(world, template);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(template.Description);
            }
            ImGui.EndPopup();
        }
        string filter = EditorStyle.Search("##rule-filter", "Keresés: szabály vagy csoport…", ref ruleFilter);
        ImGui.Spacing();
        // The schedule note under the list keeps the room its wrapped text needs.
        float note = ImGui.CalcTextSize(ScheduleHelp, false, ImGui.GetContentRegionAvail().X).Y + ImGui.GetStyle().ItemSpacing.Y * 2;
        ImGui.BeginChild("book-scroll", new Vec2(0, -note));
        GroupedRuleList(world, rules, filter);
        if (rules.Count == 0) EditorStyle.Text(HudTheme.Muted, "Nincs szabály. Az „+ Új szabály” üresen vagy mintából indít egyet.");
        ImGui.EndChild();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, ScheduleHelp);
        ImGui.PopTextWrapPos();
        EditorStyle.EndPanel(); ImGui.SameLine();

        if (rules.Count == 0) return;
        var rule = rules[selectedBookRule];
        if (rule.Effects.All(e => e.Influence != null) && !editRuleCalculation) {
            InfluenceArea(world, rule); return;
        }
        DrawRuleCalculation(world, rule);
    }
}
