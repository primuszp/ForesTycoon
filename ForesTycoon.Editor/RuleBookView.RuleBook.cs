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
internal sealed partial class RuleBookView
{
    private RuleBookSession ruleBook => context.Book;
    private ref int selectedBookRule => ref state.SelectedBookRule;
    private ref int? initialBookSelection => ref state.InitialBookSelection;
    private string ruleBookError;
    private ref string ruleFilter => ref state.RuleFilter;
    private RuleFieldDefinition newField => state.NewField;

    private void LoadRuleBook(RuleProject project)
    {
        selectedBookRule = Math.Clamp(initialBookSelection ?? 0, 0, Math.Max(0, project.Rules.Count - 1)); initialBookSelection = null; ruleBookError = null;
    }
    private IReadOnlyList<RuleView> RuleBookRules(GameWorld world) => ruleBook.Entries;
    private IReadOnlyList<RuleFieldView> RuleBookFields(GameWorld world) => ruleBook.CustomFields;

    /// <summary>Deletes a rule. A starter it was made from is remembered, so loading the project does not add it back.</summary>
    private void DeleteRule(GameWorld world, RuleView rule)
    {
        var rules = RuleBookRules(world);
        ruleBook.Delete(rule);
        selectedBookRule = Math.Clamp(selectedBookRule, 0, Math.Max(0, rules.Count - 1));
        ApplyRuleBook(world, rule.Name + ": törölve.");
    }

    /// <summary>Adds a starter rule as a new own rule, with a fresh id when the starter's is taken.</summary>
    private void AddStarterRule(GameWorld world, WorldRule starter)
    {
        var rules = RuleBookRules(world);
        var added = ruleBook.AddStarter(starter);
        selectedBookRule = ruleBook.IndexOf(added); selectedInfluence = selectedTerm = 0; editRuleCalculation = false; ShowPage(EditorPage.Rules);
        ApplyRuleBook(world, added.Name + ": hozzáadva a mintából.");
    }
    internal void ShowRuleBook(int index = 0) { scrollRuleCard = true; ShowPage(EditorPage.Rules); ruleCardTab = 0; selectedBookRule = index; editRuleCalculation = false; if (!projectInitialized) initialBookSelection = index; }

    /// <summary>Checks the whole book as the game will; on success the test world runs it at once.</summary>
    private void ApplyRuleBook(GameWorld world, string what)
    {
        try {
            ApplyProject(world, false);
            ruleBookError = null;
            message = what + " A tesztvilág alkalmazása naplózva.";
        } catch (Exception e) when (Expected(e)) { ruleBookError = message = e.Message; }
    }

    private const string ScheduleHelp = "Időszakos: 0,5 játékmásodpercenként. Havi: erdőhónap végén. Áthaladási: jármű belépésekor. "
        + "Állati: állati lépésenként. Állomány: élőhelyfelmérés után. Létrehozáskor: az új egyedeken. Azonos indításnál fentről lefelé haladnak.";

    internal void Draw(GameWorld world)
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
                ruleBook.CreateEmpty();
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
