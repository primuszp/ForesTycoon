using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>
/// The object library: the kinds of world objects the game exposes to rules, discovered from its code (see
/// ForesTycoon/World/RuleEntities.cs). A kind inherits its parent's fields; the tree shows where each field comes from,
/// what is attached to the object, what a rule may do to it, and which rules already run on it.
/// </summary>
internal sealed partial class RuleEditorView
{
    private string selectedObject = "tree";

    private static RuleSchema Objects => GameWorld.RuleObjects;

    private void ObjectsArea(GameWorld world)
    {
        ImGui.BeginChild("objects-tree", new Vec2(290, 0), ImGuiChildFlags.Borders);
        EditorStyle.Section("Objektumok a játékból");
        foreach (var root in Objects.Roots) ObjectNode(root, selectedObject, id => selectedObject = id, abstractSelectable: true);
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A lista a játék kódjából jön: minden [RuleEntity] interfész egy objektumfajta, és örökli a szülője mezőit. " +
            "Dőlt: csak csoportosít, szabály a leszármazottain fut.");
        ImGui.PopTextWrapPos();
        ImGui.EndChild(); ImGui.SameLine();

        ImGui.BeginChild("object-detail", Vec2.Zero, ImGuiChildFlags.Borders);
        var e = Objects.Find(selectedObject) ?? Objects.Entities[0];
        EditorStyle.Text(HudTheme.AmberAccent, string.Join("  ›  ", Objects.Lineage(e.Id).Select(x => x.Name)));
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Parchment, e.Help);
        EditorStyle.Text(HudTheme.Muted, e.Abstract ? "Csoportosító fajta: a mezői a leszármazottakon olvashatók."
            : $"Hatókör: „{e.Name}” · most {ElementCount(world, RuleEngine.Scope(e.Id))} {e.Element} · kód: {e.Type.Name}");
        ImGui.PopTextWrapPos();

        EditorStyle.Section("Mezők");
        if (ImGui.BeginTable("object-fields", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) {
            ImGui.TableSetupColumn("Név", ImGuiTableColumnFlags.WidthFixed, 170); ImGui.TableSetupColumn("Egység", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Írható", ImGuiTableColumnFlags.WidthFixed, 55); ImGui.TableSetupColumn("Örökölt innen", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Leírás"); ImGui.TableHeadersRow();
            foreach (var f in e.Fields) {
                bool own = f.DeclaredBy == e.Id;
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); EditorStyle.Text(f.Writable ? HudTheme.AmberAccent : HudTheme.Parchment, f.Display);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("In formulas: " + f.Name + "\nCode: " + f.Property.DeclaringType.Name + "." + f.Property.Name);
                ImGui.TableNextColumn(); EditorStyle.Text(HudTheme.Muted, f.Unit);
                ImGui.TableNextColumn(); EditorStyle.Text(f.Writable ? HudTheme.Good : HudTheme.Muted, f.Writable ? "igen" : "–");
                ImGui.TableNextColumn(); EditorStyle.Text(HudTheme.Muted, own ? "saját" : Objects.Find(f.DeclaredBy)?.Name ?? f.DeclaredBy);
                ImGui.TableNextColumn(); ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(f.Help); ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }

        EditorStyle.Section("Kapcsolatok");
        if (e.Relations.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs: a szabály csak a saját mezőit látja.");
        foreach (var r in e.Relations) {
            var target = Objects.Find(r.Target);
            if (ImGui.Selectable($"{r.Display} ({r.Name})  ›  {target.Name}##rel{r.Name}")) selectedObject = target.Id;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(r.Help + "\nKattintás: a kapcsolt objektum megnyitása.");
            ImGui.Indent(20);
            ImGui.PushTextWrapPos(0);
            EditorStyle.Text(HudTheme.Muted, "Képletben: " + string.Join(", ", target.Fields.Select(f => r.Name + "." + f.Name)));
            ImGui.PopTextWrapPos();
            ImGui.Unindent(20);
        }

        EditorStyle.Section("Műveletek");
        if (e.Actions.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs.");
        foreach (var a in e.Actions) { ImGui.TextUnformatted(a.Name); ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, a.Help); }

        EditorStyle.Section("Szabályok ezen az objektumon");
        var rules = RuleBookRules(world);
        bool any = false;
        for (int i = 0; i < rules.Count; i++) {
            if (!InLineage(rules[i].Scope, e.Id)) continue;
            any = true;
            if (ImGui.Selectable($"{rules[i].Name}{(rules[i].Enabled ? "" : "  (kikapcsolva)")}##obj-rule{i}")) ShowRuleBook(i);
        }
        if (!any) EditorStyle.Text(HudTheme.Muted, "Még nincs.");
        if (!e.Abstract && ImGui.Button("Új szabály ezen: " + e.Name)) {
            int n = rules.Count(r => !r.BuiltIn) + 1;
            while (rules.Exists(r => r.Id == $"sajat-{n}")) n++;
            rules.Add(new WorldRule { Id = $"sajat-{n}", Name = $"Saját szabály {n}", Scope = e.Id, Enabled = false });
            ShowRuleBook(rules.Count - 1);
            message = "Új, kikapcsolt szabály: írd meg, majd kapcsold be.";
        }
        ImGui.EndChild();
    }

    /// <summary>True when <paramref name="scope"/> is <paramref name="entity"/> or one of its descendants.</summary>
    private static bool InLineage(string scope, string entity) => Objects.Lineage(scope).Any(x => x.Id == entity);

    /// <summary>One node of the hierarchy; concrete kinds are selectable, abstract ones only when <paramref name="abstractSelectable"/>.</summary>
    private static void ObjectNode(RuleEntitySchema e, string selected, Action<string> select, bool abstractSelectable)
    {
        var children = Objects.Children(e.Id).ToArray();
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth
            | (children.Length == 0 ? ImGuiTreeNodeFlags.Leaf : 0) | (selected == e.Id ? ImGuiTreeNodeFlags.Selected : 0);
        ImGui.PushStyleColor(ImGuiCol.Text, e.Abstract ? HudTheme.Muted : HudTheme.Parchment);
        bool open = ImGui.TreeNodeEx(e.Name + "##obj" + e.Id, flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen() && (abstractSelectable || !e.Abstract)) select(e.Id);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{e.Help}\n{e.Fields.Length} mező · {e.Relations.Length} kapcsolat · {e.Actions.Length} művelet");
        if (!open) return;
        foreach (var c in children) ObjectNode(c, selected, select, abstractSelectable);
        ImGui.TreePop();
    }
}
