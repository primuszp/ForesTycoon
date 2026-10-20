using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The object library: the kinds of world objects the game exposes to rules, discovered from its code (see
/// ForesTycoon/World/RuleEntities.cs). A kind inherits its parent's fields; the page shows where each field comes from,
/// what is attached to the object, what a rule may do to it, which rules already run on it — and gives a writable field
/// a behaviour in one click. A new interface or field in the game appears here without editor code.
/// </summary>
internal sealed partial class RuleEditorView
{
    private static RuleSchema Objects => GameWorld.RuleObjects;

    private string selectedObject, objectFilter = "";
    private (string Scope, int Frame, int Count) objectCount = (null, -1000, 0);

    /// <summary>One node of the hierarchy; concrete kinds are selectable, abstract ones only when <paramref name="abstractSelectable"/>.</summary>
    private static void ObjectNode(RuleEntitySchema e, string selected, Action<string> select, bool abstractSelectable, string filter = "")
    {
        var children = Objects.Children(e.Id).ToArray();
        if (filter.Length > 0 && !MatchesObject(e, filter)) return;
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding
            | (children.Length == 0 ? ImGuiTreeNodeFlags.Leaf : 0) | (selected == e.Id ? ImGuiTreeNodeFlags.Selected : 0);
        ImGui.PushStyleColor(ImGuiCol.Text, e.Abstract ? HudTheme.Muted : HudTheme.Parchment);
        bool open = ImGui.TreeNodeEx(e.Name + "##obj" + e.Id, flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen() && (abstractSelectable || !e.Abstract || RuleEngine.Scope(e.Id) != null)) select(e.Id);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{e.Help}\n{e.Fields.Length} mező · {e.Relations.Length} kapcsolat · {e.Actions.Length} művelet");
        if (!open) return;
        foreach (var c in children) ObjectNode(c, selected, select, abstractSelectable, filter);
        ImGui.TreePop();
    }

    /// <summary>A kind matches when it, a descendant, or one of its own fields carries the text.</summary>
    private static bool MatchesObject(RuleEntitySchema e, string filter) =>
        Contains(e.Name, filter) || Contains(e.CodeName, filter) || e.Fields.Any(f => f.DeclaredBy == e.Id && (Contains(f.Display, filter) || Contains(f.Name, filter)))
        || Objects.Children(e.Id).Any(c => MatchesObject(c, filter));
    private static bool Contains(string text, string part) => text?.IndexOf(part, StringComparison.CurrentCultureIgnoreCase) >= 0;

    internal void ShowObject(string id) { worldTab = WorldTab.Objects; selectedObject = id; }

    // ── The workspace ────────────────────────────────────────────────────
    private void ObjectsTab(GameWorld world)
    {
        selectedObject ??= Objects.Entities.FirstOrDefault(e => RuleEngine.Scope(e.Id) != null)?.Id ?? Objects.Entities.FirstOrDefault()?.Id;
        var rules = RuleBookRules(world);
        var uses = FieldUses(rules).ToDictionary(u => u.Key, StringComparer.Ordinal);

        ImGui.BeginChild("objects-tree", new Vec2(300, 0), ImGuiChildFlags.Borders);
        EditorStyle.Section("Objektumok a játékból");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##object-filter", "Keresés: objektum vagy mező…", ref objectFilter, 64);
        ImGui.Spacing();
        // Families (a root with descendants) first; standalone kinds — data records, settings — grouped below by name.
        string filter = objectFilter.Trim();
        var roots = Objects.Roots.ToList();
        foreach (var root in roots.Where(r => Objects.Children(r.Id).Any())) ObjectNode(root, selectedObject, id => selectedObject = id, true, filter);
        var single = roots.Where(r => !Objects.Children(r.Id).Any() && (filter.Length == 0 || MatchesObject(r, filter)))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        if (single.Count > 0) {
            ImGui.Spacing();
            if (filter.Length > 0) ImGui.SetNextItemOpen(true);
            if (ImGui.TreeNodeEx($"Önálló objektumok ({single.Count})##standalone", ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.SpanAvailWidth)) {
                foreach (var e in single) ObjectNode(e, selectedObject, id => selectedObject = id, true, filter);
                ImGui.TreePop();
            }
        }
        ImGui.Spacing();
        EditorStyle.Text(HudTheme.Muted, "Halvány: csoportosító fajta, mezőit a leszármazottak öröklik.");
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("objects-card", Vec2.Zero, ImGuiChildFlags.Borders);
        var entity = Objects.Find(selectedObject);
        if (entity == null) EditorStyle.Text(HudTheme.Muted, "Válassz egy objektumot a bal oldali fából.");
        else ObjectCard(world, entity, rules, uses);
        ImGui.EndChild();
    }

    private void ObjectCard(GameWorld world, RuleEntitySchema e, List<WorldRule> rules, Dictionary<string, FieldUse> uses)
    {
        var scope = RuleEngine.Scope(e.Id);
        // Breadcrumb: every ancestor is a link.
        var lineage = Objects.Lineage(e.Id);
        for (int i = 0; i < lineage.Count; i++) {
            if (i > 0) { ImGui.SameLine(0, 6); EditorStyle.Text(HudTheme.Muted, "›"); ImGui.SameLine(0, 6); }
            if (i == lineage.Count - 1) EditorStyle.Text(HudTheme.Parchment, lineage[i].Name);
            else if (EditorStyle.QuietButton(lineage[i].Name + "##crumb" + i)) selectedObject = lineage[i].Id;
        }
        EditorStyle.Title(e.Name, true);
        if (e.Help.Length > 0) { ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, e.Help); ImGui.PopTextWrapPos(); }
        ImGui.Spacing();
        EditorStyle.Chip(e.Abstract ? "csoportosító" : "szabály futhat rajta", e.Abstract ? HudTheme.Muted : HudTheme.Good, "obj-kind");
        if (scope != null) {
            if (objectCount.Scope != e.Id || ImGui.GetFrameCount() - objectCount.Frame > 60)
                objectCount = (e.Id, ImGui.GetFrameCount(), world.BoundaryPending ? objectCount.Count : scope.Elements(world).Length);
            ImGui.SameLine(0, 8); EditorStyle.Chip($"most {objectCount.Count} {(e.Element.Length > 0 ? e.Element : "elem")} a világban", HudTheme.Info, "obj-count");
        }
        ImGui.SameLine(0, 8); EditorStyle.Chip("kód: " + (e.Type?.Name ?? e.CodeName), EditorStyle.PresentationKind, "obj-code");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A játék kódjában ez a típus írja le az objektumot. Új mező vagy fajta ott felvéve itt magától megjelenik.");

        // ── Behaviours on this object ────────────────────────────────────
        EditorStyle.Section("Viselkedések ezen az objektumon");
        var own = rules.Select((r, i) => (Rule: r, Index: i)).Where(x => x.Rule.Scope == e.Id).ToList();
        if (own.Count == 0) EditorStyle.Text(HudTheme.Muted, scope == null
            ? "Csoportosító fajtán nem fut szabály; a viselkedést a konkrét leszármazottra add."
            : "Még nincs szabály ezen az objektumon. Egy írható mező „+ Viselkedés” gombja indít egyet.");
        foreach (var (rule, index) in own) {
            EditorStyle.Chip(rule.Enabled ? "be" : "ki", rule.Enabled ? HudTheme.Good : HudTheme.Muted, "obj-rule-state" + index);
            ImGui.SameLine(0, 8);
            if (ImGui.Selectable(rule.Name + "##obj-rule" + index, false, ImGuiSelectableFlags.None, new Vec2(0, 0))) OpenRule(index);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Megnyitás a Szabályok lapon.\nÍr: " + string.Join(", ", rule.Effects.Select(x => FieldDisplay(e.Id, x.Target))));
        }

        // ── Fields, own first, then grouped by the ancestor that declares them ──
        var groups = e.Fields.GroupBy(f => f.DeclaredBy).OrderBy(g => g.Key == e.Id ? 0 : 1).ThenByDescending(g => lineage.ToList().FindIndex(l => l.Id == g.Key));
        foreach (var group in groups) {
            EditorStyle.Section(group.Key == e.Id ? "Saját mezők" : "Örökölt innen: " + (Objects.Find(group.Key)?.Name ?? group.Key));
            FieldTable(world, e, scope, group.ToList(), rules, uses, "f-" + group.Key);
        }
        if (e.Summaries.Length > 0) {
            EditorStyle.Section("Összesítések a gyűjteményekből");
            FieldTable(world, e, null, e.Summaries.ToList(), rules, uses, "f-summaries");
        }

        if (e.Relations.Length > 0 || e.Collections.Length > 0) {
            EditorStyle.Section("Kapcsolatok");
            foreach (var r in e.Relations) RelationRow(r.Display, r.Help, r.Target, r.Name + ".", "rel" + r.Name);
            foreach (var c in e.Collections) RelationRow(c.Display, c.Help, c.Target, c.Name + ".átlag(…)", "col" + c.Name, many: true);
        }
        if (e.Actions.Length > 0) {
            EditorStyle.Section("Műveletek");
            foreach (var a in e.Actions) {
                EditorStyle.Chip(a.Display, EditorStyle.OperationKind, "act" + a.Name);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(a.Help + "\nKépletben: " + a.Name);
                if (scope != null) { ImGui.SameLine(0, 8); if (ImGui.SmallButton("+ Viselkedés##act" + a.Name)) AddBehavior(world, e.Id, a.Name, "action", a.Display); }
                ImGui.SameLine(0, 10); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, a.Help); ImGui.PopTextWrapPos();
            }
        }
    }

    private void RelationRow(string display, string help, string target, string usage, string id, bool many = false)
    {
        EditorStyle.Text(HudTheme.Parchment, display + (many ? " (több)" : "")); ImGui.SameLine(0, 8);
        EditorStyle.Text(HudTheme.Muted, "→"); ImGui.SameLine(0, 8);
        if (EditorStyle.Chip(Objects.Find(target)?.Name ?? target, EditorStyle.InputKind, id, true)) selectedObject = target;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(help + "\nKépletben: " + usage + "mező\nKattintás: a kapcsolt objektum.");
    }

    private void FieldTable(GameWorld world, RuleEntitySchema e, RuleScope scope, List<RuleFieldSchema> fields, List<WorldRule> rules,
        Dictionary<string, FieldUse> uses, string id)
    {
        if (!ImGui.BeginTable(id, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH)) return;
        ImGui.TableSetupColumn("Mező", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Írható", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Szabályok", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();
        foreach (var f in fields) {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            EditorStyle.Text(HudTheme.Parchment, f.Display);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip((f.Help.Length > 0 ? f.Help + "\n" : "") + "Képletben: " + f.Name);
            ImGui.TableSetColumnIndex(1);
            EditorStyle.Text(HudTheme.Muted, FieldRange(f));
            ImGui.TableSetColumnIndex(2);
            if (f.Writable) EditorStyle.Text(HudTheme.Good, "igen"); else EditorStyle.Text(HudTheme.Muted, "–");
            ImGui.TableSetColumnIndex(3);
            uses.TryGetValue(e.Id + "." + f.Name, out var use);
            if (use == null || use.Writers.Count + use.Readers.Count + use.Native.Count == 0) EditorStyle.Text(HudTheme.Muted, "nem használt");
            else {
                var parts = new List<string>();
                if (use.Writers.Count > 0) parts.Add($"ír {use.Writers.Count}");
                if (use.Readers.Count > 0) parts.Add($"olvas {use.Readers.Count}");
                if (use.Native.Count > 0) parts.Add("beépített");
                EditorStyle.Text(use.Shared ? HudTheme.AmberAccent : HudTheme.Parchment, string.Join(" · ", parts));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n",
                    use.Writers.Select(w => $"{w.Name}: {WriteMode(w.Mode)}{(w.Enabled ? "" : " (ki)")}")
                    .Concat(use.Readers.Select(r => $"{r.Name}: olvassa{(r.Enabled ? "" : " (ki)")}")).Concat(use.Native))
                    + (use.Shared ? "\nTöbb bekapcsolt szabály írja: a futási sorrend számít." : ""));
            }
            ImGui.TableSetColumnIndex(4);
            if (scope != null && f.Writable && f.Collection == null) {
                if (ImGui.SmallButton("+ Viselkedés##" + id + f.Name)) AddBehavior(world, e.Id, f.Name, "add", f.Display);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Új szabály: mi és hogyan változtassa {RuleSentence.Article(f.Display)} {f.Display} mezőt.\nKikapcsolva indul; a Szabályok lapon építed fel.");
            }
        }
        ImGui.EndTable();
    }

    private static string FieldRange(RuleFieldSchema f)
    {
        if (f.Options.Length > 0) return string.Join(" / ", f.Options.Take(4).Select(o => o.Display)) + (f.Options.Length > 4 ? " …" : "");
        string range = double.IsInfinity(f.Min) && double.IsInfinity(f.Max) ? ""
            : $"{(double.IsInfinity(f.Min) ? "…" : f.Min.ToString("0.##"))}–{(double.IsInfinity(f.Max) ? "…" : f.Max.ToString("0.##"))}";
        return string.Join(" ", new[] { range, f.Unit }.Where(s => !string.IsNullOrEmpty(s)));
    }

    /// <summary>A new, switched-off rule on <paramref name="scope"/> that changes one field (or runs one action); opens it for editing.</summary>
    private void AddBehavior(GameWorld world, string scope, string target, string mode, string display)
    {
        var rules = RuleBookRules(world);
        string name = (Objects.Find(scope)?.Name ?? scope) + ": " + display; int n = 2;
        while (rules.Exists(r => r.Name == name)) name = $"{Objects.Find(scope)?.Name ?? scope}: {display} {n++}";
        rules.Add(new WorldRule { Id = $"rule-{Guid.NewGuid():N}", Name = name, Scope = scope, Enabled = false,
            Effects = { mode == "action" ? new() { Target = target, Mode = "action" } : new() { Target = target, Mode = mode, Influence = new() } } });
        OpenRule(rules.Count - 1);
        message = $"Új viselkedés: {name}. Add meg, mi hasson rá, majd kapcsold be.";
    }

    private void OpenRule(int index)
    {
        worldTab = WorldTab.Rules; selectedBookRule = index; commonSelection = null; editRuleCalculation = false; ruleOverview = false;
    }
}
