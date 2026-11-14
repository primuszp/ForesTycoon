using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;

internal sealed class RuleTuningView(RuleCatalogNavigation navigation,
    Action<string, double> setTuningValue, Action<GameWorld> ensureCatalog, Func<GameRuleDefinition, Action> ruleTarget)
{
    private string tuningSearch = "";
    private bool changedOnly;
    private Func<GameRuleDefinition, Action> RuleTarget => ruleTarget;
    private GameRuleCatalog catalog => navigation.Session.Catalog;
    private GameRuleIndex index => navigation.Session.Index;
    private List<(RuleCatalogLayout.Domain Domain, string[] Modules)> groups => navigation.Layout.Groups;
    internal bool Field(string key, string id)
    {
        if (catalog == null) return false;
        foreach (var rule in catalog.Rules)
            for (int i = 0; i < rule.Parameters.Count; i++)
            {
                var p = rule.Parameters[i];
                if (p.Key != key) continue;
                float value = (float)p.Value;
                float speed = (float)Math.Max(Math.Abs(p.Value) * 0.005, (p.Max - p.Min) * 0.0005);
                ImGui.SetNextItemWidth(-1);
                if (p.Changed) ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.AmberAccent);
                if (ImGui.DragFloat("##" + id, ref value, speed, (float)p.Min, (float)p.Max, Format(p), ImGuiSliderFlags.AlwaysClamp))
                    SetParameter(rule, i, value == (float)p.Default ? p.Default : Math.Clamp(value, p.Min, p.Max));
                if (p.Changed) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{p.Name}\nAlapérték: {p.Default:G6} {p.Unit}\nHúzás: hangolás · Ctrl+kattintás: beírás");
                return ImGui.IsItemDeactivatedAfterEdit();
            }
        ImGui.TextDisabled("—");
        return false;
    }



    /// <summary>Replaces the document values and refreshes every catalog projection.</summary>

    private void SetParameter(GameRuleDefinition rule, int index, double value) => setTuningValue(rule.Parameters[index].Key, value);

    internal int UnappliedTuning(GameWorld world) => navigation.Session.UnappliedTuning(world.Tuning);

    private static bool BeginParameterTable(string id, bool withDefault)
    {
        if (!ImGui.BeginTable(id, withDefault ? 5 : 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX)) return false;
        ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        if (withDefault) ImGui.TableSetupColumn("Alap", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Egység", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 44);
        if (withDefault) ImGui.TableHeadersRow();
        return true;
    }

    private static string Format(RuleParameter p) => "%.6g";

    /// <summary>One parameter: a drag field when tunable (Ctrl+click to type), its unit and a reset to the default.</summary>
    private void ParameterRow(GameRuleDefinition rule, int index, bool withDefault)
    {
        var p = rule.Parameters[index];
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        if (p.Changed)
        {
            Vec2 at = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddCircleFilled(at + new Vec2(-6, ImGui.GetTextLineHeight() / 2 + 3), 3, EditorStyle.Selection);
        }
        ImGui.AlignTextToFramePadding();
        ImGui.PushTextWrapPos(); EditorStyle.Text(p.Tunable ? HudTheme.Parchment : HudTheme.Muted, p.Name); ImGui.PopTextWrapPos();
        if (ImGui.IsItemHovered() && p.Tunable)
            ImGui.SetTooltip($"{p.Name}\nKulcs: {p.Key}\nAlapérték: {p.Default:G6} {p.Unit}\nTartomány: {p.Min:G6} – {p.Max:G6}\nHúzás: hangolás · Ctrl+kattintás: beírás");
        ImGui.TableNextColumn();
        if (p.Tunable)
        {
            float value = (float)p.Value;
            float speed = (float)Math.Max(Math.Abs(p.Value) * 0.005, (p.Max - p.Min) * 0.0005);
            ImGui.SetNextItemWidth(-1);
            if (p.Changed) ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.AmberAccent);
            if (p.Key == "TruckTrailerLayout")
            {
                int layout = (int)Math.Round(p.Value);
                if (ImGui.Combo("##" + rule.Id + p.Key, ref layout, "Szóló tehergépkocsi\0Nyerges / középtengelyes pótkocsi\0Vonórudas, forgózsámolyos pótkocsi\0"))
                    SetParameter(rule, index, layout);
            }
            else if (p.Key == "ProcessorSteeringModel")
            {
                int model = (int)Math.Round(p.Value);
                if (ImGui.Combo("##" + rule.Id + p.Key, ref model, "Kerékkormányzás\0Alvázcsuklós kormányzás\0"))
                    SetParameter(rule, index, model);
            }
            else if (p.Unit == "db")
            {
                int count = (int)Math.Round(p.Value);
                if (ImGui.DragInt("##" + rule.Id + p.Key, ref count, .03f, (int)p.Min, (int)p.Max, "%d", ImGuiSliderFlags.AlwaysClamp))
                    SetParameter(rule, index, count);
            }
            else if (ImGui.DragFloat("##" + rule.Id + p.Key, ref value, speed, (float)p.Min, (float)p.Max, Format(p), ImGuiSliderFlags.AlwaysClamp))
                SetParameter(rule, index, value == (float)p.Default ? p.Default : Math.Clamp(value, p.Min, p.Max));
            if (p.Changed) ImGui.PopStyleColor();
        }
        else { ImGui.AlignTextToFramePadding(); EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.85f), p.Value.ToString("G6")); }
        if (withDefault)
        {
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
            EditorStyle.Text(HudTheme.Muted, p.Tunable ? p.Default.ToString("G6") : "");
        }
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
        ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, p.Unit); ImGui.PopTextWrapPos();
        ImGui.TableNextColumn();
        if (p.Changed)
        {
            if (EditorStyle.QuietButton("alap##" + rule.Id + p.Key, $"Vissza az alapértékre: {p.Default:G6}")) SetParameter(rule, index, p.Default);
        }
    }

    /// <summary>Every tunable number of the game in one list, by system and rule.</summary>
    /// <summary>The tuning tables; <paramref name="include"/> limits them to some rules (by rule id), as the Vehicles tab does.</summary>
    internal void Draw(GameWorld world, Func<string, bool> include = null)
    {
        if (catalog == null) ensureCatalog(world);
        include ??= _ => true;
        var all = catalog.Rules.Where(r => include(r.Id)).SelectMany(r => r.Parameters).Where(p => p.Tunable).ToList();
        int changed = all.Count(p => p.Changed), unapplied = UnappliedTuning(world);
        ImGui.SetNextItemWidth(320);
        ImGui.InputTextWithHint("##tuning-search", "Keresés: paraméter, szabály, rendszer…", ref tuningSearch, 120);
        ImGui.SameLine(); ImGui.Checkbox("Csak a módosítottak", ref changedOnly);
        ImGui.SameLine(0, 18); ImGui.AlignTextToFramePadding();
        EditorStyle.Text(HudTheme.Muted, $"{all.Count} hangolható szám · ");
        ImGui.SameLine(0, 0); EditorStyle.Text(changed > 0 ? HudTheme.AmberAccent : HudTheme.Muted, $"{changed} eltér az alapértéktől");
        if (unapplied > 0) { ImGui.SameLine(0, 0); EditorStyle.Text(HudTheme.Info, $" · {unapplied} még nincs alkalmazva"); }
        if (changed > 0)
        {
            ImGui.SameLine(0, 18);
            if (EditorStyle.QuietButton("Mind alapra", "Minden hangolt értéket visszaállít az alapértékre (alkalmazni külön kell)."))
                foreach (var rule in catalog.Rules.Where(r => include(r.Id)))
                    for (int i = 0; i < rule.Parameters.Count; i++)
                        if (rule.Parameters[i].Changed) SetParameter(rule, i, rule.Parameters[i].Default);
        }
        ImGui.Spacing();
        ImGui.BeginChild("tuning-list", new Vec2(0, 0), ImGuiChildFlags.Borders);
        string query = tuningSearch.Trim();
        bool Match(GameRuleDefinition r, RuleParameter p) =>
            include(r.Id) && (!changedOnly || p.Changed) && (query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Module.Contains(query, StringComparison.OrdinalIgnoreCase));
        bool any = false;
        foreach (var (domain, modules) in groups)
            foreach (string m in modules)
            {
                var rules = index.InModule(m).Where(r => r.Parameters.Any(p => p.Tunable && Match(r, p))).ToList();
                if (rules.Count == 0) continue;
                any = true;
                ImGui.Spacing();
                EditorStyle.Chip(m, domain.Colour, "tm" + m);
                foreach (var rule in rules)
                {
                    ImGui.Indent(10);
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.Parchment);
                    if (RuleTarget?.Invoke(rule) is { } open)
                    {
                        if (EditorStyle.QuietButton(rule.Name + " ›##tr" + rule.Id, "A szabály megnyitása, amely ezeket a számokat használja.")) open();
                    }
                    else { ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(rule.Name); }
                    ImGui.PopStyleColor();
                    ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.8f), rule.Scope);
                    if (BeginParameterTable("tt" + rule.Id, true))
                    {
                        for (int i = 0; i < rule.Parameters.Count; i++)
                            if (rule.Parameters[i].Tunable && Match(rule, rule.Parameters[i])) ParameterRow(rule, i, true);
                        ImGui.EndTable();
                    }
                    ImGui.Unindent(10);
                }
            }
        if (!any) EditorStyle.Text(HudTheme.Muted, changedOnly ? "Nincs az alapértéktől eltérő szám." : "Nincs találat.");
        ImGui.EndChild();
    }

    internal void Parameters(GameRuleDefinition rule)
    {
        if (!BeginParameterTable("parameters", false)) return;
        for (int i = 0; i < rule.Parameters.Count; i++) ParameterRow(rule, i, false);
        ImGui.EndTable();
    }
}
