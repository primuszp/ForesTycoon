using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>
/// Which rules read and write each field of the object model, in run order. Two rules writing the same field are
/// flagged: the later one sees (or, when it sets the value, overwrites) the earlier one's result. Native causes come
/// from the [RuleCause] declarations next to the fields the game computes itself.
/// </summary>
internal sealed partial class RuleEditorView
{
    private bool ruleOverview;
    private bool overviewOnlyShared;

    internal void ShowRuleOverview() { worldTab = WorldTab.Rules; commonSelection = null; ruleOverview = true; }

    internal sealed record FieldWrite(int Rule, string Name, string Mode, bool Enabled, string Schedule);
    internal sealed record FieldUse(string Key, string Entity, string Field, List<FieldWrite> Writers, List<(int Rule, string Name, bool Enabled)> Readers, List<string> Native)
    {
        public bool Shared => Writers.Count(w => w.Enabled) > 1;
    }

    /// <summary>How a rule on <paramref name="scope"/> names a field (“tile.moisture”) as an object-model key (“tile.moisture” of the tile kind).</summary>
    private static string FieldKey(string scope, string name)
    {
        var entity = Objects.Find(scope);
        if (entity == null) return scope + "." + name;
        if (entity.Fields.Any(f => f.Name == name) || entity.Actions.Any(a => a.Name == name)) return scope + "." + name;
        foreach (var r in entity.Relations)
            if (name.StartsWith(r.Name + ".", StringComparison.Ordinal)) return r.Target + "." + name[(r.Name.Length + 1)..];
        return null;
    }

    internal static IReadOnlyList<FieldUse> FieldUses(IReadOnlyList<WorldRule> rules)
    {
        var uses = new Dictionary<string, FieldUse>(StringComparer.Ordinal);
        FieldUse Use(string key)
        {
            if (key == null) return null;
            if (!uses.TryGetValue(key, out var u)) {
                int dot = key.IndexOf('.');
                uses[key] = u = new(key, key[..dot], key[(dot + 1)..], new(), new(), new());
            }
            return u;
        }
        for (int i = 0; i < rules.Count; i++) {
            var rule = rules[i];
            foreach (var effect in rule.Effects) {
                Use(FieldKey(rule.Scope, effect.Target))?.Writers.Add(new(i, rule.Name, effect.Mode, rule.Enabled, rule.Schedule));
                IEnumerable<string> reads = effect.Influence != null
                    ? effect.Influence.Terms.SelectMany(t => GraphInputs(rule.Scope, effect, t).Select(x => x.Source).Append(t.AmountField))
                    : Array.Empty<string>();
                foreach (string name in reads.Where(n => !string.IsNullOrEmpty(n) && !n.StartsWith("tuning.", StringComparison.Ordinal)).Distinct()) {
                    var use = Use(FieldKey(rule.Scope, name));
                    if (use != null && !use.Readers.Any(r => r.Rule == i)) use.Readers.Add((i, rule.Name, rule.Enabled));
                }
            }
        }
        // Formula rules: the causal map reads every field a formula mentions.
        try {
            var map = RuleCausalMap.Build(Objects, rules.Where(r => r.Effects.Any(e => e.Influence == null) || r.Condition.Length > 0).ToList());
            foreach (var edge in map.Edges.Where(e => !e.Native)) {
                int index = rules.ToList().FindIndex(r => r.Id == edge.Rule);
                var use = Use(edge.From);
                if (index >= 0 && use != null && !use.Readers.Any(r => r.Rule == index)) use.Readers.Add((index, rules[index].Name, rules[index].Enabled));
            }
            foreach (var edge in RuleCausalMap.Build(Objects, Array.Empty<WorldRule>()).Edges.Where(e => e.Native))
                Use(edge.To)?.Native.Add($"{(edge.Sign > 0 ? "Nő" : edge.Sign < 0 ? "Csökken" : "Változik")} {RuleSentence.Article(KeyDisplay(edge.From))} {KeyDisplay(edge.From)} hatására ({edge.Rule})");
        } catch (Exception) { /* A formula that does not parse simply contributes no reads. */ }
        return uses.Values.OrderByDescending(u => u.Shared).ThenBy(u => Objects.Find(u.Entity)?.Name ?? u.Entity, StringComparer.CurrentCulture)
            .ThenBy(u => u.Field, StringComparer.Ordinal).ToList();
    }

    private static string KeyDisplay(string key)
    {
        int dot = key.IndexOf('.');
        if (dot < 0) return key;
        string entity = key[..dot], field = key[(dot + 1)..];
        return (Objects.Find(entity)?.Name ?? entity) + " · " + FieldDisplay(entity, field);
    }

    private static string WriteMode(string mode) => mode switch { "set" => "felülírja", "approach" => "közelíti", "action" => "végrehajtja", _ => "változtatja" };
    private static string ScheduleLabel(string schedule) => schedule switch { "passage" => "áthaladáskor", "month" => "erdőhónaponként", "growth" => "növekedési ráta frissítésekor", _ => "időszakonként" };

    /// <summary>Under the rule's sentence: other rules writing the same field, and in which order they run.</summary>
    private void OtherWriters(GameWorld world, WorldRule rule)
    {
        var rules = RuleBookRules(world); int self = rules.IndexOf(rule);
        var shared = FieldUses(rules).Where(u => u.Writers.Any(w => w.Rule == self) && u.Writers.Any(w => w.Rule != self && w.Enabled)).ToArray();
        if (shared.Length == 0 || !ImGui.CollapsingHeader($"{shared.Length} célmezőt más szabály is változtat · futási rend")) return;
        foreach (var use in shared) {
            string order = string.Join("; ", use.Writers.Where(w => w.Enabled || w.Rule == self).OrderBy(w => w.Rule).Select(w => $"{w.Rule + 1}. {(w.Rule == self ? "ez a szabály" : w.Name)} ({ScheduleLabel(w.Schedule)})"));
            EditorStyle.Text(HudTheme.AmberAccent, $"A(z) {KeyDisplay(use.Key)} mezőt más szabály is írja: {order}. Azonos indításnál listasorrendben futnak; külön eseményeknél a tényleges időrend dönt.");
            if (ImGui.IsItemClicked()) ruleOverview = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Kattintás: áttekintés, melyik szabály mit olvas és ír.");
        }
    }

    private void RuleOverviewArea(GameWorld world, List<WorldRule> rules)
    {
        ImGui.BeginChild("rule-overview", Vec2.Zero, ImGuiChildFlags.Borders);
        ImGui.PushTextWrapPos(0);
        EditorStyle.Title("Mezők és szabályok");
        EditorStyle.Text(HudTheme.Muted, "Melyik szabály melyik objektum melyik tulajdonságát olvassa és írja. Az azonos eseményre vagy időlépésre induló szabályok listasorrendben futnak. " +
            "Az áthaladási és időszakos szabályok a saját indításukkor futnak; a későbbi végrehajtás az addig kialakult állapotot látja.");
        var uses = FieldUses(rules);
        int shared = uses.Count(u => u.Shared);
        if (shared > 0) EditorStyle.Text(HudTheme.AmberAccent, $"Figyelem: {shared} mezőt több bekapcsolt szabály is ír.");
        else EditorStyle.Text(HudTheme.Good, "Minden mezőt legfeljebb egy bekapcsolt szabály ír.");
        ImGui.Checkbox("Csak a több szabály által írt mezők", ref overviewOnlyShared);
        ImGui.PopTextWrapPos();
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("field-uses", 4, flags)) {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Objektum · tulajdonság", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("Írja (futási sorrendben)", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Olvassa", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("A játék maga számolja", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableHeadersRow();
            foreach (var use in uses.Where(u => !overviewOnlyShared || u.Shared)) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushTextWrapPos(0);
                EditorStyle.Text(use.Shared ? HudTheme.AmberAccent : HudTheme.Parchment, (use.Shared ? "(!) " : "") + KeyDisplay(use.Key));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(FieldHelp(use.Entity, use.Field));
                ImGui.PopTextWrapPos();
                ImGui.TableNextColumn();
                foreach (var w in use.Writers.OrderBy(w => w.Rule)) RuleLink(w.Rule, $"{w.Rule + 1}. {w.Name} · {ScheduleLabel(w.Schedule)} · {WriteMode(w.Mode)}", w.Enabled);
                if (use.Writers.Count == 0) EditorStyle.Text(HudTheme.Muted, "—");
                ImGui.TableNextColumn();
                foreach (var r in use.Readers.OrderBy(r => r.Rule)) RuleLink(r.Rule, $"{r.Rule + 1}. {r.Name}", r.Enabled);
                if (use.Readers.Count == 0) EditorStyle.Text(HudTheme.Muted, "—");
                ImGui.TableNextColumn();
                ImGui.PushTextWrapPos(0);
                foreach (string n in use.Native) EditorStyle.Text(HudTheme.Muted, n);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();

        void RuleLink(int index, string text, bool enabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, enabled ? HudTheme.Parchment : HudTheme.Muted);
            if (ImGui.Selectable(text + (enabled ? "" : " (ki)") + "##link" + index + text)) { selectedBookRule = index; ruleOverview = false; }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Kattintás: a szabály megnyitása.");
        }
    }
}
