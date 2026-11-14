using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;
using static ForesTycoon.Editor.RuleCatalogPresentation;
namespace ForesTycoon.Editor;

internal sealed class RuleCatalogSidebarView(RuleCatalogNavigation navigation)
{
    private GameRuleCatalog catalog => navigation.Session.Catalog;
    private GameRuleIndex index => navigation.Session.Index;
    private List<(RuleCatalogLayout.Domain Domain, string[] Modules)> groups => navigation.Layout.Groups;
    private ref string module => ref navigation.State.Module;
    private ref string selected => ref navigation.State.Selected;
    private ref string search => ref navigation.State.Search;
    private ref bool fitRequested => ref navigation.State.FitRequested;
    private void SelectModule(string name) => navigation.SelectModule(name);
    private GameRuleDefinition[] VisibleRules() => navigation.VisibleRules();
    internal void Draw()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Keresés: szabály, mező…", ref search, 120);
        ImGui.Spacing();
        if (SidebarRow("all", "Összes rendszer", EditorStyle.Fade(HudTheme.Parchment, 0.8f), catalog.Rules.Count, module.Length == 0))
        { module = ""; selected = ""; fitRequested = true; }
        foreach (var (domain, modules) in groups)
        {
            ImGui.Spacing();
            EditorStyle.Text(EditorStyle.Fade(domain.Colour, 0.9f), domain.Name.ToUpperInvariant());
            foreach (string m in modules)
                if (SidebarRow(m, m, domain.Colour, index.InModule(m).Count, module == m)) SelectModule(m);
        }
        ImGui.Spacing();
        if (SidebarRow("gaps", "Hiányzó folyamatok", HudTheme.Bad, catalog.Gaps.Count, module == "gaps")) { module = "gaps"; selected = ""; }

        if ((module.Length > 0 || search.Length > 0) && module != "gaps")
        {
            EditorStyle.Section(search.Length > 0 ? "Találatok" : "Szabályok");
            var rules = VisibleRules();
            if (rules.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs találat.");
            foreach (var rule in rules)
                if (SidebarRow("r" + rule.Id, rule.Name, ExecutionColour(rule.Execution), -1, selected == rule.Id))
                { if (module.Length > 0 && rule.Module != module) SelectModule(rule.Module); selected = rule.Id; }
        }
    }

    private static bool SidebarRow(string id, string label, Vec4 colour, int count, bool active)
    {
        float rowHeight = ImGui.GetTextLineHeight() + 8;
        bool clicked = ImGui.Selectable("##" + id, active, ImGuiSelectableFlags.None, new Vec2(0, rowHeight));
        Vec2 min = ImGui.GetItemRectMin(), max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        if (active) draw.AddRectFilled(min, new Vec2(min.X + 3, max.Y), EditorStyle.U(HudTheme.AmberAccent), 1);
        draw.AddCircleFilled(new Vec2(min.X + 13, (min.Y + max.Y) / 2), 3.5f, EditorStyle.U(colour));
        draw.PushClipRect(min, max - new Vec2(count >= 0 ? 34 : 4, 0), true);
        draw.AddText(new Vec2(min.X + 24, min.Y + 4), active ? EditorStyle.TextBright : EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, 0.86f)), label);
        draw.PopClipRect();
        if (count >= 0)
        {
            string text = count.ToString();
            Vec2 ts = ImGui.CalcTextSize(text);
            Vec2 badge = new(max.X - ts.X - 14, min.Y + 3);
            draw.AddRectFilled(badge, badge + ts + new Vec2(10, 2), EditorStyle.U(1, 1, 1, 0.07f), (ts.Y + 2) / 2);
            draw.AddText(badge + new Vec2(5, 1), EditorStyle.TextMuted, text);
        }
        if (ImGui.IsItemHovered() && ImGui.CalcTextSize(label).X > max.X - min.X - 60) ImGui.SetTooltip(label);
        return clicked;
    }

}
