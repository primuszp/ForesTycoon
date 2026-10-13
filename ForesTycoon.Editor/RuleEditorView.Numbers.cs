using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal enum RulesTopic { RuleBook, Map, Objects, Formulas, Economy, Machines, AllNumbers }

/// <summary>
/// The game's tunable numbers inside the world editor, grouped the way a designer thinks about them: money, then
/// machines. Every edit is applied to the test world at once; saving and publishing carry it into the game.
/// </summary>
internal sealed partial class RuleEditorView
{
    private RulesTopic rulesTopic;

    // Catalog rule IDs per topic; each rule becomes one card with its numbers. Road surfaces live under Types › Surfaces.
    private static readonly string[] EconomyRules = { "timber.deliver", "economy.balance", "economy.fuel", "road.build", "road.repair", "trail.build" };
    private static readonly string[] MachineRules = { "machine.processor", "machine.forwarder", "machine.movement", "upkeep.wear", "upkeep.effects",
        "upkeep.repair", "upkeep.service", "vehicle.mass", "vehicle.motion", "vehicle.resistance", "vehicle.fuel", "time.world" };

    internal void ShowRules(RulesTopic topic) { worldTab = WorldTab.Rules; rulesTopic = topic; }

    private void RulesArea(GameWorld world)
    {
        foreach (var (topic, label, help) in new[] {
            (RulesTopic.RuleBook, "Szabálykönyv", "Szabályok adatként: mikor, hol, ha mi teljesül, akkor mi változik. A játék futtatja; C# nem kell hozzá."),
            (RulesTopic.Map, "Ok-okozati térkép", "Mi mire hat: a mezők csomópontok, a szabályok nyilak, a visszacsatolási hurkok kiemelve. Egy nyílra kattintva a görbe itt szerkeszthető."),
            (RulesTopic.Objects, "Objektumok", "A játék objektumai, amelyekre szabály írható: hierarchia, mezők, kapcsolatok, műveletek. A játék kódjából derül ki."),
            (RulesTopic.Formulas, "Képletek", "A beépített számítások egy-egy értékének felülírása: víz, járhatóság, növekedés…"),
            (RulesTopic.Economy, "Gazdaság", "Faár, hitelkeret, üzemanyagár, építési költségek."),
            (RulesTopic.Machines, "Gépek és járművek", "Kapacitás, sebesség, fogyasztás, kopás, javítás."),
            (RulesTopic.AllNumbers, "Összes szám", "A játék minden hangolható száma kereshető listában.") }) {
            if (topic != RulesTopic.RuleBook) ImGui.SameLine(0, 6);
            if (ViewTab(label, rulesTopic == topic, help)) rulesTopic = topic;
        }
        int unapplied = currentRules.UnappliedTuning(world);
        if (unapplied > 0) {
            ImGui.SameLine(0, 24);
            if (EditorStyle.PrimaryButton($"{unapplied} szám alkalmazása a tesztvilágban", true, "A módosított számok a tesztvilágban azonnal érvényesek lesznek."))
                Try(() => ApplyProject(world, false));
        }
        ImGui.Separator();
        switch (rulesTopic) {
            case RulesTopic.Economy: NumberCards(world, "Gazdaság", "Pénz a játékban: mennyit ér a fa, mennyibe kerül az építés és az üzemanyag, meddig lehet eladósodni.", EconomyRules); break;
            case RulesTopic.Machines: NumberCards(world, "Gépek és járművek", "A gépek munkatempója, a járművek fizikája, fogyasztása és kopása.", MachineRules); break;
            case RulesTopic.AllNumbers:
                ImGui.BeginChild("all-numbers", Vec2.Zero);
                currentRules.DrawTuning(world);
                ImGui.EndChild();
                break;
            case RulesTopic.Formulas: RulesTab(world); break;
            case RulesTopic.Objects: ObjectsArea(world); break;
            case RulesTopic.Map: CausalMapArea(world); break;
            default: RuleBookArea(world); break;
        }
    }

    /// <summary>One card per catalog rule: its description and its numbers with unit, default and reset.</summary>
    private void NumberCards(GameWorld world, string title, string intro, string[] ruleIds)
    {
        ImGui.BeginChild("number-cards", Vec2.Zero, ImGuiChildFlags.Borders);
        EditorStyle.Title(title);
        ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, intro + " Húzással hangolható, Ctrl+kattintással beírható; az elengedés után a tesztvilágban azonnal érvényes."); ImGui.PopTextWrapPos();
        bool changed = false;
        foreach (string ruleId in ruleIds) {
            var specs = GameTuning.Specs.Where(s => s.Rule == ruleId).ToArray();
            if (specs.Length == 0) continue;
            var rule = currentRules.Index?.Find(ruleId);
            EditorStyle.Section(rule?.Name ?? ruleId);
            if (rule?.Description is { Length: > 0 } description) {
                ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, FirstSentence(description)); ImGui.PopTextWrapPos();
            }
            var formulas = FormulaRules().Where(f => f.Rule.Id == ruleId).ToArray();
            foreach (var (hook, _) in formulas) {
                if (EditorStyle.QuietButton("Képlet: " + HookName(hook) + "##f" + HookKey(hook), "Ugrás a képlethez, amely ezeket a számokat használja."))
                { rulesTopic = RulesTopic.Formulas; selectedRuleHook = HookKey(hook); }
                ImGui.SameLine();
            }
            if (formulas.Length > 0) ImGui.NewLine();
            if (!ImGui.BeginTable("numbers-" + ruleId, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) continue;
            ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthFixed, 280);
            ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthFixed, 180);
            ImGui.TableSetupColumn("Egység és alapérték");
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 60);
            foreach (var spec in specs) {
                var p = currentRules.TuningParameter(spec.Id);
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding();
                EditorStyle.Text(p?.Changed == true ? HudTheme.AmberAccent : HudTheme.Parchment, spec.Name);
                ImGui.TableSetColumnIndex(1);
                if (currentRules.TuningField(spec.Id, "n" + spec.Id)) changed = true;
                ImGui.TableSetColumnIndex(2); ImGui.AlignTextToFramePadding();
                EditorStyle.Text(HudTheme.Muted, $"{spec.Unit} · alap: {DefaultText(spec)}");
                ImGui.TableSetColumnIndex(3);
                if (p?.Changed == true && EditorStyle.QuietButton("alap##r" + spec.Id, $"Vissza az alapértékre: {DefaultText(spec)}") && currentRules.ResetTuning(spec.Id))
                    changed = true;
            }
            ImGui.EndTable();
        }
        if (changed) Try(() => { ApplyProject(world, false); message = "Szám módosítva; a tesztvilágban már ez érvényes. Mentés után a jelenettel együtt a játékba kerül."; });
        ImGui.EndChild();
    }

    /// <summary>The numbers of the catalog rule a formula belongs to, editable next to the formula.</summary>
    private void RuleNumbers(GameWorld world, string ruleId)
    {
        var specs = GameTuning.Specs.Where(s => s.Rule == ruleId).ToArray();
        if (specs.Length == 0) return;
        EditorStyle.Section("A szabály számai");
        if (!ImGui.BeginTable("rule-numbers", 3, ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("név", ImGuiTableColumnFlags.WidthFixed, 220);
        ImGui.TableSetupColumn("érték", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("egység");
        bool changed = false;
        foreach (var spec in specs) {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding();
            EditorStyle.Text(currentRules.TuningParameter(spec.Id)?.Changed == true ? HudTheme.AmberAccent : HudTheme.Parchment, spec.Name);
            ImGui.TableSetColumnIndex(1); if (currentRules.TuningField(spec.Id, "rn" + spec.Id)) changed = true;
            ImGui.TableSetColumnIndex(2); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{spec.Unit} · alap: {DefaultText(spec)}");
        }
        ImGui.EndTable();
        if (changed) Try(() => { ApplyProject(world, false); message = "Szám módosítva; a tesztvilágban már ez érvényes."; });
    }

    private static string DefaultText(TuningSpec spec) => spec.Key == Tune.CreditLimit && spec.Default >= Economy.UnlimitedCredit ? "korlátlan" : spec.Default.ToString("G6");

    private static string FirstSentence(string text)
    {
        int end = text.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + 1)];
    }
}
