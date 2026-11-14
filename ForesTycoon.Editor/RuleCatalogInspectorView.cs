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

internal sealed class RuleCatalogInspectorView(RuleCatalogNavigation navigation, RuleTuningView tuning,
    Func<string, string> readNotes, Action<string, string> writeNotes, Func<GameRuleDefinition, Action> ruleTarget)
{
    private string observedRule;
    private string[] observations = Array.Empty<string>();
    private long observedAt;
    private Func<GameRuleDefinition, Action> RuleTarget => ruleTarget;
    internal void Reset() => observedRule = null;
    private GameRuleCatalog catalog => navigation.Session.Catalog;
    private GameRuleIndex index => navigation.Session.Index;
    private List<(RuleCatalogLayout.Domain Domain, string[] Modules)> groups => navigation.Layout.Groups;
    private Dictionary<(string, string), int> moduleLinks => navigation.Layout.Links;
    private ref string module => ref navigation.State.Module;
    private ref string selected => ref navigation.State.Selected;
    private ref string search => ref navigation.State.Search;
    private RuleCatalogLayout.Domain DomainOf(string name) => navigation.Layout.DomainOf(name);
    private void SelectModule(string name) => navigation.SelectModule(name);
    private void Navigate(string id) => navigation.Navigate(id);
    internal void Draw(GameWorld world, Action<BehaviorHook> editBehavior, Action<string> editController)
    {
        var rule = index.Find(selected);
        if (rule != null) { RuleDetails(world, rule, editBehavior, editController); return; }
        if (module.Length > 0 && module != "gaps") { ModuleSummary(); return; }

        EditorStyle.Title(catalog.Name);
        ImGui.PushTextWrapPos();
        EditorStyle.Text(HudTheme.Muted, "A játék összes szabálya rendszerenként. Egy szabály mezőket olvas és ír; ahol egy szabály kimenete egy másiknak bemenete, ott kapcsolat fut közöttük.");
        ImGui.PopTextWrapPos();
        if (EditorStyle.BeginFacts("overview-facts"))
        {
            EditorStyle.Fact("Szabályok", catalog.Rules.Count.ToString());
            EditorStyle.Fact("Rendszerek", index.Modules.Count.ToString());
            EditorStyle.Fact("Kapcsolatok", $"{index.Connections.Count} mező szerint");
            EditorStyle.Fact("Hiányok", catalog.Gaps.Count.ToString(), HudTheme.Bad);
            ImGui.EndTable();
        }
        EditorStyle.Section("Végrehajtás");
        foreach (GameRuleExecution e in Enum.GetValues<GameRuleExecution>())
        {
            int count = catalog.Rules.Count(r => r.Execution == e);
            EditorStyle.Chip($"{Status(e)} · {count}", ExecutionColour(e), "legend" + e);
            ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, StatusHelp(e)); ImGui.PopTextWrapPos();
        }
        EditorStyle.Section("Tartományok");
        foreach (var (domain, modules) in groups)
        {
            EditorStyle.Chip(domain.Name, domain.Colour, "domain" + domain.Name); ImGui.SameLine();
            ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{modules.Length} rendszer, {modules.Sum(m => index.InModule(m).Count)} szabály");
        }
    }

    private void ModuleSummary()
    {
        var domain = DomainOf(module);
        EditorStyle.Chip(domain.Name, domain.Colour, "module-domain");
        EditorStyle.Title(module);
        var rules = index.InModule(module);
        EditorStyle.Text(HudTheme.Muted, $"{rules.Count} szabály. Válassz egyet a térképen vagy az oldalsávban.");
        EditorStyle.Section("Ebből a rendszerből táplálkozik");
        ModuleLinks(moduleLinks.Where(k => k.Key.Item2 == module).Select(k => (k.Key.Item1, k.Value)));
        EditorStyle.Section("Ezt a rendszert táplálja");
        ModuleLinks(moduleLinks.Where(k => k.Key.Item1 == module).Select(k => (k.Key.Item2, k.Value)));
    }

    private void ModuleLinks(IEnumerable<(string Module, int Count)> links)
    {
        var list = links.OrderByDescending(l => l.Count).ToList();
        if (list.Count == 0) { EditorStyle.Text(HudTheme.Muted, "—"); return; }
        foreach (var (m, count) in list)
        {
            if (EditorStyle.Chip(m, DomainOf(m).Colour, "ml" + m, true)) SelectModule(m);
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{count} mező");
        }
    }

    private void RuleDetails(GameWorld world, GameRuleDefinition rule, Action<BehaviorHook> editBehavior, Action<string> editController)
    {
        var domain = DomainOf(rule.Module);
        if (EditorStyle.Chip(rule.Module, domain.Colour, "rule-module", true)) { search = ""; string id = rule.Id; SelectModule(rule.Module); selected = id; }
        ImGui.SameLine();
        EditorStyle.Chip(Status(rule.Execution), ExecutionColour(rule.Execution), "rule-exec");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(StatusHelp(rule.Execution));
        EditorStyle.Title(rule.Name);
        if (editController != null && rule.Id is "machine.controller" or "machine.forwarder" or "machine.processor" or "vehicle.transfer" or "fleet.orders")
            foreach (string kind in new[] { "forwarder", "processor", "truck" })
                if ((rule.Id == "machine.controller" || rule.Id == "machine." + kind || kind == "truck" && rule.Id is "vehicle.transfer" or "fleet.orders")
                    && ImGui.Button("Állapotgráf: " + kind)) editController(kind);
        ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Parchment, rule.Description); ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (RuleTarget?.Invoke(rule) is { } open && EditorStyle.PrimaryButton("Megnyitás szerkesztésre")) open();
        if (editBehavior != null)
        {
            var hooks = WorldBehaviorPolicy.Hooks.Where(h => !h.Id.StartsWith("tuning.", StringComparison.Ordinal) && RuleWorkshopModel.HookBelongsTo(h, rule)).ToArray();
            // A rule made of several graphs lists each of them.
            if (hooks.Length > 1)
                foreach (var hook in hooks)
                    if (EditorStyle.QuietButton("› " + hook.Name + "##" + hook.Id + hook.Kind)) editBehavior(hook);
        }
        if (EditorStyle.BeginFacts("rule-facts"))
        {
            EditorStyle.Fact("Hatókör", rule.Scope);
            EditorStyle.Fact("Ütem", rule.Schedule);
            EditorStyle.Fact("Azonosító", rule.Id, HudTheme.Muted);
            ImGui.EndTable();
        }
        if (rule.Formula.Length > 0)
        {
            EditorStyle.Section("Egyenlet / művelet");
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vec4(0.05f, 0.065f, 0.055f, 1));
            ImGui.BeginChild("formula", new Vec2(-1, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
            ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.AmberAccent, rule.Formula); ImGui.PopTextWrapPos();
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        EditorStyle.Section($"Bemenetek · {rule.Reads.Length}");
        foreach (string field in rule.Reads)
            FieldRow(field, index.Writers(field).Where(r => r.Id != rule.Id).ToArray(), "‹", "saját állapot vagy induló adat");
        EditorStyle.Section($"Kimenetek · {rule.Writes.Length}");
        foreach (string field in rule.Writes)
            FieldRow(field, index.Readers(field).Where(r => r.Id != rule.Id).ToArray(), "›", "nincs olvasója");

        if (rule.Parameters.Count > 0)
        {
            int tunable = rule.Parameters.Count(p => p.Tunable);
            EditorStyle.Section(tunable > 0 ? $"Játékparaméterek · {tunable} hangolható" : "Játékparaméterek");
            tuning.Parameters(rule);
            if (rule.Parameters.Any(p => !p.Tunable))
                EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.75f), "Halvány érték: tájékoztató adat, a világ létrehozásakor rögzül.");
        }
        long now = Stopwatch.GetTimestamp();
        if (observedRule != rule.Id || Stopwatch.GetElapsedTime(observedAt, now).TotalSeconds >= .25)
        {
            observations = rule.Reads.Concat(rule.Writes).Distinct().Select(world.ObserveRuleField).Where(value => value != null).ToArray();
            observedRule = rule.Id; observedAt = now;
        }
        if (observations.Length > 0)
        {
            EditorStyle.Section("Élő adatok a tesztvilágból");
            foreach (string value in observations) { ImGui.Bullet(); ImGui.SameLine(); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Info, value); ImGui.PopTextWrapPos(); }
        }
        EditorStyle.Section("Tervezői megjegyzés");
        string notes = readNotes?.Invoke(rule.Id) ?? rule.Notes;
        if (ImGui.InputTextMultiline("##notes", ref notes, 4096, new Vec2(-1, 96)))
        {
            if (writeNotes != null) writeNotes(rule.Id, notes); else rule.Notes = notes;
        }
        if (notes.Length == 0 && !ImGui.IsItemActive())
        {
            Vec2 min = ImGui.GetItemRectMin();
            ImGui.GetWindowDrawList().AddText(min + new Vec2(8, 6), EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, 0.6f)), "Ötlet, kérdés, hangolási jegyzet…");
        }
    }

    private void FieldRow(string field, GameRuleDefinition[] others, string arrow, string empty)
    {
        EditorStyle.Text(HudTheme.Parchment, field);
        ImGui.Indent(14);
        if (others.Length == 0) EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.7f), $"{arrow} {empty}");
        float right = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();
        for (int i = 0; i < others.Length; i++)
        {
            var other = others[i];
            if (i > 0)
            {
                float next = ImGui.CalcTextSize(other.Name).X + 30;
                ImGui.SameLine();
                if (ImGui.GetCursorPosX() + next > right) ImGui.NewLine();
            }
            if (EditorStyle.Chip(other.Name, DomainOf(other.Module).Colour, field + arrow + other.Id, true)) Navigate(other.Id);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{other.Module} › {other.Name}\nKattintás: ugrás a szabályhoz");
        }
        ImGui.Unindent(14);
        ImGui.Spacing();
    }
}
