using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

/// <summary>
/// Groups of the rule list. A rule without an author's group sits under the object it runs on; the author can make
/// groups of their own ("Útfenntartás", "Erdőfelújítás") and move rules into them by dragging or from the context menu.
/// Groups only order the editor's list; the game runs the rules in the same order either way.
/// </summary>
internal sealed partial class RuleEditorView
{
    private const string AutoGroupPrefix = "\u0001";
    private readonly HashSet<string> emptyRuleGroups = new(StringComparer.CurrentCulture);
    private string newRuleGroup = "";
    private int draggedRule = -1;

    /// <summary>The author's groups (alphabetical, empty ones included), then the automatic per-object groups; members in run order.</summary>
    private List<(string Group, List<int> Members)> RuleGroups(List<WorldRule> rules)
    {
        string Key(WorldRule r) => string.IsNullOrWhiteSpace(r.Group)
            ? AutoGroupPrefix + (Objects.Find(r.Scope)?.Name ?? RuleEngine.Scope(r.Scope)?.Name ?? r.Scope) : r.Group.Trim();
        var groups = Enumerable.Range(0, rules.Count).GroupBy(i => Key(rules[i])).ToDictionary(g => g.Key, g => g.ToList());
        emptyRuleGroups.RemoveWhere(groups.ContainsKey);
        foreach (string g in emptyRuleGroups) groups[g] = new();
        return groups.OrderBy(g => g.Key.StartsWith(AutoGroupPrefix, StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(g => GroupTitle(g.Key), StringComparer.CurrentCulture).Select(g => (g.Key, g.Value)).ToList();
    }

    private IEnumerable<string> CustomRuleGroups(List<WorldRule> rules) =>
        rules.Select(r => r.Group?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Concat(emptyRuleGroups).Distinct().OrderBy(g => g, StringComparer.CurrentCulture);

    private static string GroupTitle(string group) => group.StartsWith(AutoGroupPrefix, StringComparison.Ordinal) ? group[AutoGroupPrefix.Length..] : group;

    private void RuleGroupsToolbar()
    {
        if (ImGui.SmallButton("+ Új csoport")) { newRuleGroup = ""; ImGui.OpenPopup("new-rule-group"); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Saját csoport a szabályaidnak; húzd bele őket, vagy jobb gomb a szabályon.");
        if (ImGui.BeginPopup("new-rule-group")) {
            ImGui.TextUnformatted("Csoport neve");
            ImGui.SetNextItemWidth(240);
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            bool enter = ImGui.InputText("##new-group", ref newRuleGroup, 60, ImGuiInputTextFlags.EnterReturnsTrue);
            string name = newRuleGroup.Trim();
            ImGui.BeginDisabled(name.Length == 0);
            if ((ImGui.Button("Létrehozás") || enter) && name.Length > 0) {
                emptyRuleGroups.Add(name); message = $"Új csoport: {name}. Húzz bele szabályokat."; ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.EndPopup();
        }
        ImGui.Spacing();
    }

    private void DropRuleInto(List<WorldRule> rules, string group)
    {
        ImGui.AcceptDragDropPayload("rule");
        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left) || draggedRule < 0 || draggedRule >= rules.Count) return;
        MoveRuleToGroup(rules[draggedRule], group); draggedRule = -1;
    }

    private void MoveRuleToGroup(WorldRule rule, string group)
    {
        rule.Group = group; pendingCommonChange = true;
        message = group.Length == 0 ? $"{rule.Name}: vissza az objektuma csoportjába." : $"{rule.Name} → {group}";
    }

    /// <summary>Right-click on a rule: move it to one of the author's groups, a new one, or back under its object.</summary>
    private void RuleGroupMenu(List<WorldRule> rules, WorldRule rule)
    {
        if (!ImGui.BeginPopupContextItem("rule-group-" + rule.Id)) return;
        EditorStyle.Text(HudTheme.Muted, "Áthelyezés csoportba");
        foreach (string g in CustomRuleGroups(rules))
            if (ImGui.Selectable(g + "##to-" + g, rule.Group == g)) MoveRuleToGroup(rule, g);
        if (!string.IsNullOrWhiteSpace(rule.Group) && ImGui.Selectable("Objektum szerint (automatikus)")) MoveRuleToGroup(rule, "");
        ImGui.Separator();
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("##group-here", "Új csoport neve + Enter", ref newRuleGroup, 60, ImGuiInputTextFlags.EnterReturnsTrue)
            && newRuleGroup.Trim().Length > 0) { MoveRuleToGroup(rule, newRuleGroup.Trim()); newRuleGroup = ""; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    /// <summary>The rule list, one collapsible header per group; a header accepts dropped rules.</summary>
    private void GroupedRuleList(GameWorld world, List<WorldRule> rules)
    {
        RuleGroupsToolbar();
        foreach (var (group, members) in RuleGroups(rules)) {
            bool custom = !group.StartsWith(AutoGroupPrefix, StringComparison.Ordinal);
            if (members.Contains(selectedBookRule) && commonSelection == null && !ruleOverview) ImGui.SetNextItemOpen(true, ImGuiCond.Once);
            ImGui.PushStyleColor(ImGuiCol.Text, custom ? HudTheme.AmberAccent : HudTheme.Parchment);
            bool open = ImGui.TreeNodeEx($"{GroupTitle(group)}  ({members.Count(m => rules[m].Enabled)}/{members.Count})##group-{group}",
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.SpanAvailWidth | (custom ? ImGuiTreeNodeFlags.DefaultOpen : 0));
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(custom ? "Saját csoport. Jobb gomb: feloldás." : "Automatikus csoport: az ezen az objektumon futó szabályok.\nIde húzva egy szabály visszakerül az objektuma csoportjába.");
            if (ImGui.BeginDragDropTarget()) { DropRuleInto(rules, custom ? group : ""); ImGui.EndDragDropTarget(); }
            if (custom && ImGui.BeginPopupContextItem("group-menu-" + group)) {
                if (ImGui.Selectable("Csoport feloldása")) { foreach (int m in members) rules[m].Group = ""; emptyRuleGroups.Remove(group); pendingCommonChange = true; }
                ImGui.EndPopup();
            }
            if (!open) continue;
            foreach (int i in members) {
                var entry = rules[i]; int index = i;
                RuleCardEntry("rule:" + entry.Id, entry.Name, "Állapotot változtat",
                    selectedBookRule == i && commonSelection == null && !ruleOverview, entry.Enabled,
                    () => { selectedBookRule = index; commonSelection = null; editRuleCalculation = false; ruleOverview = false; },
                    enabled => { entry.Enabled = enabled; ApplyRuleBook(world, entry.Name + (enabled ? ": bekapcsolva." : ": kikapcsolva.")); },
                    "Kikapcsolva nem hajtja végre a hatásait. A korábbi változtatások és más szabályok hatásai megmaradnak.\nHúzd egy csoportra, vagy jobb gomb: csoportba helyezés.");
                if (ImGui.BeginDragDropSource()) { draggedRule = index; ImGui.SetDragDropPayload("rule", IntPtr.Zero, 0); ImGui.TextUnformatted(entry.Name); ImGui.EndDragDropSource(); }
                RuleGroupMenu(rules, entry);
            }
            if (members.Count == 0) EditorStyle.Text(HudTheme.Muted, "Üres. Húzz ide szabályt.");
            ImGui.TreePop();
        }
    }
}
