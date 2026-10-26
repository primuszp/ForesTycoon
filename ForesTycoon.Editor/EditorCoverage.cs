using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

/// <summary>
/// Whether the editor reaches everything the game runs. Every behaviour hook, rule schedule, effect mode, tunable number and
/// editable catalog rule of the engine must have a page in the editor; anything the engine still does natively is listed,
/// not hidden. The smoke test fails on a gap, and <c>--check-editor-coverage</c> prints the report.
/// </summary>
internal static class EditorCoverage
{
    internal sealed record Report(List<string> Gaps, List<string> Native, int Hooks, int Tuning, int Editable, int SceneFields = 0);

    /// <summary>
    /// The editor page of every scene field: the scene itself and each item type it lists. A field the game reads from the
    /// scene with no entry here is a gap, and so is an entry for a field that no longer exists. Adding scene data to the
    /// game therefore needs an editor surface before the smoke test passes.
    /// </summary>
    internal static readonly IReadOnlyDictionary<Type, IReadOnlyDictionary<string, string>> SceneSurfaces = new Dictionary<Type, IReadOnlyDictionary<string, string>> {
        [typeof(WorldScenario)] = new Dictionary<string, string> {
            ["Name"] = "Térkép · Pálya neve", ["Seed"] = "Térkép · Generálás", ["Size"] = "Térkép · Méret", ["MaxHeight"] = "Térkép · Domborzat magassága",
            ["InitialState"] = "Jelenet… · rögzített nyitójelenet", ["SoilTypes"] = "Talajtípusok", ["SoilPaint"] = "Térkép · Talaj réteg",
            ["ElevationZones"] = "Térkép · Magassági övek réteg", ["ProfileScale"] = "Talajtípusok · Rétegrend (nagyítás)", ["Biomes"] = "Biomok", ["GroundMaterials"] = "Biomok · Felszínanyagok", ["HeightPaint"] = "Térkép · Domborzat és Víz réteg",
            ["ForestPaint"] = "Térkép · Erdő réteg", ["Springs"] = "Térkép · Víz réteg (forrás)", ["Species"] = "Fafajok",
            ["Roads"] = "Térkép · Utak és épületek", ["Sawmills"] = "Térkép · Utak és épületek", ["Depots"] = "Térkép · Utak és épületek",
            ["Missions"] = "Küldetések" },
        [typeof(ElevationZoneDefinition)] = new Dictionary<string, string> {
            ["Id"] = "Magassági övek (létrehozáskor)", ["Name"] = "Magassági övek", ["Color"] = "Magassági övek", ["MaxLevel"] = "Magassági övek" },
        [typeof(SoilTypeDefinition)] = new Dictionary<string, string> {
            ["Id"] = "Talajtípusok (másoláskor)", ["Name"] = "Talajtípusok", ["Color"] = "Talajtípusok", ["PreferredMoisture"] = "Talajtípusok",
            ["PreferredTexture"] = "Talajtípusok", ["Saturation"] = "Talajtípusok", ["FieldCapacity"] = "Talajtípusok", ["WiltingPoint"] = "Talajtípusok",
            ["InfiltrationPerHour"] = "Talajtípusok", ["DrainagePerHour"] = "Talajtípusok", ["Fertility"] = "Talajtípusok", ["BearingCapacity"] = "Talajtípusok",
            ["Horizons"] = "Talajtípusok · Rétegrend" },
        [typeof(BiomeDefinition)] = new Dictionary<string, string> {
            ["Id"] = "Biomok (létrehozáskor)", ["Name"] = "Biomok", ["Color"] = "Biomok", ["Zone"] = "Biomok", ["Share"] = "Biomok", ["Description"] = "Biomok",
            ["DryGround"] = "Biomok", ["WetGround"] = "Biomok", ["SteepGround"] = "Biomok" },
        [typeof(GroundMaterialDefinition)] = new Dictionary<string, string> {
            ["Id"] = "Felszínanyagok (létrehozáskor)", ["Name"] = "Biomok · Felszínanyagok", ["Color"] = "Biomok · Felszínanyagok", ["Pattern"] = "Biomok · Felszínanyagok" },
        [typeof(SoilHorizon)] = new Dictionary<string, string> {
            ["Name"] = "Talajtípusok · Rétegrend", ["Depth"] = "Talajtípusok · Rétegrend", ["Color"] = "Talajtípusok · Rétegrend" },
        [typeof(SpeciesDefinition)] = new Dictionary<string, string> {
            ["Id"] = "Fafajok (beépített faj)", ["Name"] = "Fafajok · Élettan", ["Description"] = "Fafajok · Élettan", ["MaximumAgeYears"] = "Fafajok · Élettan",
            ["MatureAgeYears"] = "Fafajok · Élettan", ["MaximumBiomass"] = "Fafajok · Élettan", ["AnnualGrowthRate"] = "Fafajok · Élettan",
            ["PreferredMoisture"] = "Fafajok · Élettan", ["MoistureTolerance"] = "Fafajok · Élettan", ["PreferredElevation"] = "Fafajok · Élettan",
            ["ElevationTolerance"] = "Fafajok · Élettan", ["ShadeTolerance"] = "Fafajok · Élettan", ["WoodDensity"] = "Fafajok · Élettan",
            ["DrySensitivity"] = "Fafajok · Élettan", ["MatureHeight"] = "Fafajok · Élettan", ["MaxHeight"] = "Fafajok · Élettan",
            ["Capacity"] = "Fafajok · Élettan", ["TimberPrice"] = "Fafajok · Élettan", ["Shape"] = "Fafajok · Megjelenés", ["Colours"] = "Fafajok · Színek és kéreg" },
        [typeof(TreeShapeDefinition)] = new Dictionary<string, string> { ["All"] = "Fafajok · Megjelenés · Minden fázis", ["Phases"] = "Fafajok · Megjelenés · fázisfülek" },
        [typeof(TreeColoursDefinition)] = new Dictionary<string, string> {
            ["Spring"] = "Fafajok · Színek", ["Summer"] = "Fafajok · Színek", ["Autumn"] = "Fafajok · Színek", ["Bark"] = "Fafajok · Kéreg",
            ["Dead"] = "Fafajok · Színek (holtfa)", ["BarkPattern"] = "Fafajok · Kéreg" },
        [typeof(MissionGoal)] = new Dictionary<string, string> {
            ["Id"] = "Küldetések", ["Title"] = "Küldetések", ["Description"] = "Küldetések", ["Metric"] = "Küldetések",
            ["Comparison"] = "Küldetések", ["Target"] = "Küldetések", ["DeadlineYear"] = "Küldetések" },
        [typeof(RoadSegment)] = new Dictionary<string, string> { ["From"] = "Térkép · Utak és épületek", ["To"] = "Térkép · Utak és épületek", ["Surface"] = "Térkép · Utak és épületek" },
    };

    private static int CheckScene(List<string> gaps)
    {
        int fields = 0;
        foreach (var (type, surfaces) in SceneSurfaces) {
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanWrite).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            fields += properties.Count;
            foreach (string name in properties.Where(p => !surfaces.ContainsKey(p)).Order())
                gaps.Add($"Jelenetadat szerkesztőoldal nélkül: {type.Name}.{name}");
            foreach (string name in surfaces.Keys.Where(p => !properties.Contains(p)).Order())
                gaps.Add($"Szerkesztőoldal nem létező jelenetadatra: {type.Name}.{name}");
        }
        return fields;
    }

    internal static Report Check(GameWorld world, BehaviorModel behaviors)
    {
        var gaps = new List<string>();
        var catalog = world.DescribeCurrentRules();

        // Hooks: a pure response is listed under a named topic on Összefüggések; other hooks on Folyamatkapcsolatok,
        // except the native-only tuning hooks, whose number is on Járművek / Játékparaméterek.
        foreach (var hook in WorldBehaviorPolicy.Hooks)
            if (EcologyResponseProgram.IsPureKind(hook.Kind) && RuleEditorView.ResponseFamily(hook.Kind) == hook.Kind)
                gaps.Add($"Összefüggés témacsoport nélkül: {hook.Id} ({hook.Kind})");
        foreach (var graph in behaviors.Graphs)
            if (!WorldBehaviorPolicy.Hooks.Any(h => h.Id == graph.Hook && h.Kind == graph.Kind))
                gaps.Add($"A projekt gráfja a motor által nem ismert hookra köt: {graph.Name} · {graph.Hook} ({graph.Kind})");

        // Schedules and effect modes of state rules.
        foreach (string schedule in WorldRule.Schedules)
            if (schedule != "step" && RuleEditorView.ScheduleChip(schedule) == RuleEditorView.ScheduleChip("step"))
                gaps.Add("Időzítés felirat nélkül: " + schedule);
        foreach (string mode in RuleEffect.Modes)
            if (!RuleEditorView.EditableModes.Contains(mode)) gaps.Add("Hatásmód szerkesztő nélkül: " + mode);

        // Every tunable number appears on Járművek or Játékparaméterek.
        var tunable = catalog.Rules.SelectMany(r => r.Parameters).Where(p => p.Tunable).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var spec in GameTuning.Specs)
            if (!tunable.Contains(spec.Id)) gaps.Add($"Hangolható szám, amely egyik oldalon sem jelenik meg: {spec.Id} ({spec.Name})");

        // Every catalog rule the engine marks editable has something to edit in the project.
        var rules = world.RuleProjectDocument.Rules;
        int editable = 0;
        var native = new List<string>();
        foreach (var rule in catalog.Rules) {
            switch (rule.Execution) {
                case GameRuleExecution.NativeCode: native.Add($"{rule.Name} ({rule.Id})"); continue;
                case GameRuleExecution.Presentation: continue;
            }
            editable++;
            bool owned = rules.Any(r => r.Id == rule.Id || r.Template == rule.Id)
                || WorldBehaviorPolicy.Hooks.Any(h => RuleWorkshopModel.HookBelongsTo(h, rule) && behaviors.Graphs.Any(g => g.Hook == h.Id && g.Kind == h.Kind))
                || rule.Execution == GameRuleExecution.EditableController
                || rule.Parameters.Any(p => p.Tunable);
            if (!owned) gaps.Add($"Szerkeszthetőnek jelölt motorszabály, amelyhez nincs szerkeszthető elem: {rule.Name} ({rule.Id})");
        }
        int sceneFields = CheckScene(gaps);
        return new Report(gaps, native, WorldBehaviorPolicy.Hooks.Count(), GameTuning.Specs.Length, editable, sceneFields);
    }

    internal static string Format(Report r)
    {
        var lines = new List<string> {
            $"Editor–motor lefedettség: {r.Hooks} hook, {r.Tuning} hangolható szám, {r.Editable} szerkeszthető motorszabály, {r.SceneFields} jelenetadat.",
            r.Gaps.Count == 0 ? "Hiány: nincs." : $"Hiány: {r.Gaps.Count}"
        };
        lines.AddRange(r.Gaps.Select(g => "  - " + g));
        lines.Add($"Motoroldali (nem szerkeszthető) szabályok: {r.Native.Count}");
        lines.AddRange(r.Native.Select(n => "  · " + n));
        return string.Join(Environment.NewLine, lines);
    }
}
