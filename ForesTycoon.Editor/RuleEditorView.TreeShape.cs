using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ForesTycoon.TreeModels;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// A species' appearance: the tree generator's parameters in plain words, one common set and one set per life phase, with
/// a preview built by the game's own tree mesher from the edited (not yet applied) form.
/// </summary>
internal sealed partial class RuleEditorView
{
    private static readonly string[] PhaseLabels = { "Magonc", "Csemete", "Fiatal", "Érett", "Idős", "Öreg" };
    private static readonly string[] LightLabels = { "Árnyék", "Félárnyék", "Napos" };

    // 0 = every phase, 1..6 = one life phase, 7 = the dead tree.
    private const int DeadTab = 7;
    private int speciesTab, shapePhase, previewLight = 2, previewVariant;
    private bool showAllShapeParameters;
    // 0 spring, 1 summer, 2 autumn, 3 winter.
    private int previewSeason = 1;
    private static readonly string[] SeasonLabels = { "Tavasz", "Nyár", "Ősz", "Tél" };
    private bool previewBare => previewSeason == 3;

    /// <summary>The leaves of a living tree in the previewed season; evergreens keep theirs in winter.</summary>
    private LeafState SeasonLeaves(ForestSpecies species) => previewSeason switch {
        0 => TreePhenology.Evergreen(species) ? LeafState.Full : LeafState.Budding,
        2 => TreePhenology.Evergreen(species) ? LeafState.Full : LeafState.Autumn,
        3 => TreePhenology.Evergreen(species) ? LeafState.Full : LeafState.Bare,
        _ => LeafState.Full };

    /// <summary>What the preview builds from: the species' authored form and colours.</summary>
    private static string LookRevision(object context, ForestSpecies species) => ForestSpeciesBook.WithContext(context,
        () => (ForestSpeciesBook.Shape(species)?.Revision ?? "") + "#" + (ForestSpeciesBook.Look(species)?.Revision ?? ""));
    private float previewYaw = .6f;
    private string shapeFilter = "", shapeRevisionSeen;


    internal void ShowSpeciesAppearance(int index, int phase = 0) { ShowWorldTypes(TypeTab.Species, index); speciesTab = 1; shapePhase = phase; }
    internal void ApplyEditsForTest(GameWorld world) => ApplyProject(world, false);
    internal void ShowSpeciesColoursForTest(int index, int season, float zoom) { ShowWorldTypes(TypeTab.Species, index); speciesTab = 2; previewSeason = season; previewZoom = zoom; previewPitch = zoom > 2 ? .12f : .28f; }
    internal void SetTreePreviewForTest(int mode, int level, bool seeThrough) { previewMode = mode; testShapeLevel = level; previewSeeThrough = seeThrough; }
    private int? testShapeLevel;

    // The dead tree is shown as an old tree that died.
    private static TreeLifePhase Phase(int selection) => selection == DeadTab ? TreeLifePhase.Old : (TreeLifePhase)Math.Clamp(selection - 1, 0, 5);

    private void TreeShapeArea(GameWorld world, SpeciesDefinition sp)
    {
        if (!Enum.TryParse(sp.Id, out ForestSpecies species) || species == ForestSpecies.None) {
            EditorStyle.Text(HudTheme.Muted, "Ennek a fafajnak nincs famodellje."); return;
        }
        EditorStyle.Tabs("shape-phase", new[] { "Minden fázis" }.Concat(PhaseLabels).Append("Elhalt").ToArray(), ref shapePhase);
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, shapePhase == 0
            ? "Közös értékek minden életfázisra. A játék ezekből indul, és a fa korához és a fényhez igazítja őket (például fiatalon kevesebb, árnyékban meredekebb ág)."
            : shapePhase == DeadTab
            ? "Az elpusztult fa értékei, bármely fázisban halt el: mit tart meg az álló holtfa az élő fa ágaiból (például kevesebb vékony ág). Üresen a holtfa az élő fa ágszerkezetét tartja meg. Két év múlva kidől."
            : $"{PhaseLabels[shapePhase - 1]} fázis saját értékei. Ezek végső értékek: a játék kor- és fényigazítása után érvényesek, csak ebben a fázisban.");
        ImGui.PopTextWrapPos();

        // The draft form as the game would see it; previews and current values come from it, not from the applied project.
        object context = ForestSpeciesBook.Compose(GameWorld.SpeciesEdits(scenario));
        var phase = Phase(shapePhase);
        bool dead = shapePhase == DeadTab;
        // A form the generator cannot build falls back to the built-in tree; say so until the form changes.
        string revision = ForestSpeciesBook.WithContext(context, () => ForestSpeciesBook.Shape(species)?.Revision ?? "");
        if (revision != shapeRevisionSeen) { shapeRevisionSeen = revision; TreeArchitecture.LastFormError = null; }
        if (TreeArchitecture.LastFormError is string failed && failed.StartsWith(sp.Name, StringComparison.Ordinal)) {
            ImGui.PushTextWrapPos(0);
            EditorStyle.Text(HudTheme.Bad, "Ezzel a beállítással a fa nem építhető, ezért a játék a beépített formát használja: " + failed);
            ImGui.PopTextWrapPos();
        }
        var shown = ForestSpeciesBook.WithContext(context, () => shapePhase == 0
            ? TreeArchitecture.BaseParameters(species) : TreeArchitecture.EffectiveParameters(species, phase, previewLight, dead));

        // The life course is a long row of trees: it gets most of the width.
        float previewWidth = previewMode == 2 ? Math.Max(300, ImGui.GetContentRegionAvail().X * .68f)
            : Math.Clamp(ImGui.GetContentRegionAvail().X * .6f, 300, 1200);
        ImGui.BeginChild("shape-parameters", new Vec2(-previewWidth - 10, 0));
        bool finished = ShapeParameterList(sp, shown);
        ImGui.EndChild();
        ImGui.SameLine(0, 10);
        shownShapeLevel = testShapeLevel ?? hoveredShapeLevel;
        ImGui.BeginChild("shape-preview", Vec2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        TreePreview(species, phase, dead, context, sp);
        ImGui.EndChild();
        if (finished) { Try(() => ApplyProject(world, false)); message = sp.Name + ": megjelenés módosítva; a tesztvilág fái újraépülnek."; }
    }

    /// <summary>The authored values of the selected level: the common set or one phase's set (created on first write).</summary>
    private Dictionary<string, double> ShapeLevel(SpeciesDefinition sp, bool create)
    {
        if (sp.Shape == null) { if (!create) return null; sp.Shape = new(); }
        if (shapePhase == 0) return sp.Shape.All;
        string key = shapePhase == DeadTab ? TreeShapeDefinition.DeadState : TreeShapeDefinition.PhaseNames[shapePhase - 1];
        if (!sp.Shape.Phases.TryGetValue(key, out var values) && create) sp.Shape.Phases[key] = values = new();
        return values;
    }

    /// <summary>Removes empty phase sets and an empty form, so an untouched species stays exactly built-in.</summary>
    private static void TidyShape(SpeciesDefinition sp)
    {
        if (sp.Shape == null) return;
        foreach (string key in sp.Shape.Phases.Where(p => p.Value.Count == 0).Select(p => p.Key).ToArray()) sp.Shape.Phases.Remove(key);
        if (sp.Shape.IsEmpty) sp.Shape = null;
    }

    /// <summary>The parameter rows, grouped; returns true when an edit was finished and should reach the test world.</summary>
    private bool ShapeParameterList(SpeciesDefinition sp, IReadOnlyDictionary<string, double> shown)
    {
        bool finished = false;
        var authored = ShapeLevel(sp, false);
        int count = authored?.Count ?? 0;
        float half = (ImGui.GetContentRegionAvail().X - 8) / 2;
        ImGui.SetNextItemWidth(half);
        string filter = EditorStyle.Search("##shape-filter", "Keresés: paraméter…", ref shapeFilter);
        ImGui.SameLine();
        ImGui.BeginDisabled(count == 0);
        if (ImGui.Button((shapePhase == 0 ? "Közös értékek törlése" : "Fázis értékeinek törlése") + $" ({count})", new Vec2(-1, 0))) {
            authored?.Clear(); TidyShape(sp); finished = true;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(shapePhase == 0 ? "Vissza a beépített fafajformához (a fázisok saját értékei megmaradnak)." : "Ebben a fázisban ismét a közös értékek és a játék igazítása érvényes.");
        if (shapePhase > 0 && sp.Shape?.Phases is { Count: > 0 } phases) {
            var used = PhaseLabels.Where((_, i) => phases.ContainsKey(TreeShapeDefinition.PhaseNames[i]))
                .Concat(phases.ContainsKey(TreeShapeDefinition.DeadState) ? new[] { "Elhalt" } : Array.Empty<string>()).ToArray();
            if (used.Length > 0) EditorStyle.Text(HudTheme.Muted, "Saját értékei vannak: " + string.Join(", ", used));
        }

        ImGui.PushItemWidth(-EditorStyle.LabelWidth());
        hoveredShapeLevel = -1;
        foreach (string group in TreeShapeParameters.Groups) {
            var entries = TreeShapeParameters.Common.Where(e => e.Group == group
                && (!TreeCrownParameters.IsSurfaceParameter(e.Name) || shown.ContainsKey(e.Name))
                && Matches(filter, e.Label, e.Name, e.Help)).ToArray();
            if (entries.Length == 0) continue;
            EditorStyle.Section(group);
            foreach (var e in entries) finished |= ShapeRow(sp, e, shown);
        }
        ImGui.Spacing();
        if (filter.Length > 0) showAllShapeParameters = true;
        ImGui.Checkbox("A generátor összes paramétere (eredeti nevekkel)", ref showAllShapeParameters);
        if (showAllShapeParameters)
            foreach (var group in TreeShapeParameters.Advanced.Where(e => Matches(filter, e.Name, e.Help)).GroupBy(e => e.Group)) {
                EditorStyle.Section(group.Key);
                foreach (var e in group) finished |= ShapeRow(sp, e, shown);
            }
        ImGui.PopItemWidth();
        return finished;
    }

    /// <summary>One parameter: its value at the selected level, a dot when authored there, and a reset.</summary>
    private bool ShapeRow(SpeciesDefinition sp, TreeShapeParameters.Entry e, IReadOnlyDictionary<string, double> shown)
    {
        var authored = ShapeLevel(sp, false);
        bool own = authored != null && authored.ContainsKey(e.Name);
        double value = own ? authored[e.Name] : TreeShapeParameters.ValueIn(shown, e.Name);
        ImGui.PushID("shape-" + e.Name);
        // Keep the reset inside the parameter panel, including on narrow editor windows.
        float resetWidth = ImGui.CalcTextSize("alap").X + 2 * ImGui.GetStyle().FramePadding.X + 4;
        float labelWidth = Math.Max(EditorStyle.LabelWidth(), ImGui.CalcTextSize(e.Label).X + ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.SetNextItemWidth(Math.Max(60, ImGui.GetContentRegionAvail().X - labelWidth - resetWidth));
        bool changed = false;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, own ? EditorStyle.Fade(HudTheme.AmberAccent, .14f) : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        if (e.Options != null) {
            int choice = Math.Clamp((int)Math.Round(value), 0, e.Options.Length - 1);
            if (ImGui.Combo(e.Label, ref choice, e.Options, e.Options.Length)) { value = choice; changed = true; }
        } else if (e.Integer) {
            int v = (int)Math.Round(value);
            if (ImGui.SliderInt(e.Label, ref v, (int)e.Min, (int)e.Max, "%d", ImGuiSliderFlags.AlwaysClamp)) { value = v; changed = true; }
        } else {
            float v = (float)value;
            string format = Math.Abs(e.Max - e.Min) >= 20 ? "%.0f" : Math.Abs(e.Max - e.Min) >= 2 ? "%.2f" : "%.3f";
            if (ImGui.SliderFloat(e.Label, ref v, (float)e.Min, (float)e.Max, format, ImGuiSliderFlags.AlwaysClamp)) { value = Math.Round(v, 4); changed = true; }
        }
        ImGui.PopStyleColor();
        bool finished = ImGui.IsItemDeactivatedAfterEdit() || changed && e.Options != null;
        if (ImGui.IsItemHovered()) hoveredShapeLevel = BranchLevel(e);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(e.Help + (own ? "\n\nEbben a szintben saját érték." : "\n\nÖrökölt érték; módosításkor saját értékké válik."));
        if (changed) ShapeLevel(sp, true)[e.Name] = value;
        if (own) {
            ImGui.SameLine(0, 4);
            if (EditorStyle.QuietButton("alap", "Saját érték törlése: ismét az örökölt érték érvényes.")) { authored.Remove(e.Name); TidyShape(sp); finished = true; }
        }
        ImGui.PopID();
        return finished;
    }

    // ── Colours and bark ─────────────────────────────────────────────────
    private static readonly string[] BarkPatternLabels = { "Általános kéreg", "Pikkelyes (lucfenyő)", "Fehér, sötét foltokkal (nyír)", "Mélyen barázdált (tölgy)", "Sima, foltos (bükk)" };
    private static readonly int[] BarkPatternCodes = { 0, 2, 3, 4, 5 };

    /// <summary>
    /// The species' colours through the year, its bark and dead wood, and the bark pattern; each authored value can be reset.
    /// The preview beside shows the tree in the chosen season with the bark as the game draws it.
    /// </summary>
    private void TreeColoursArea(GameWorld world, SpeciesDefinition sp)
    {
        if (!Enum.TryParse(sp.Id, out ForestSpecies species) || species == ForestSpecies.None) {
            EditorStyle.Text(HudTheme.Muted, "Ennek a fafajnak nincs famodellje."); return;
        }
        object context = ForestSpeciesBook.Compose(GameWorld.SpeciesEdits(scenario));
        var shown = ForestSpeciesBook.WithContext(context, () => TreeForm.Colours(species));
        var traits = ForestSpeciesBook.WithContext(context, () => ForestSpeciesTraits.For(species));
        bool evergreen = traits.Evergreen, finished = false;

        float previewWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * .6f, 300, 1200);
        ImGui.BeginChild("colour-list", new Vec2(-previewWidth - 10, 0));
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A korona a játék naptára szerint vált: tavasszal kibomlik és friss színű, nyáron a nyári szín, ősszel az őszi szín felé fordul, majd lehullik. "
            + "A megadott tavaszi és őszi szín az, amit a korona eléri.");
        ImGui.PopTextWrapPos();
        var colours = sp.Colours;
        string Get(Func<TreeColoursDefinition, string> read) => colours == null ? null : read(colours);
        void Set(Action<TreeColoursDefinition> write) { sp.Colours ??= new(); write(sp.Colours); if (sp.Colours.IsEmpty) sp.Colours = null; }
        void Row(string label, string help, System.Drawing.Color current, Func<TreeColoursDefinition, string> read, Action<TreeColoursDefinition, string> write, bool enabled = true)
        {
            ImGui.PushID(label);
            bool own = Get(read) != null;
            var rgb = new System.Numerics.Vector3(current.R / 255f, current.G / 255f, current.B / 255f);
            ImGui.BeginDisabled(!enabled);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, own ? EditorStyle.Fade(HudTheme.AmberAccent, .14f) : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
            if (ImGui.ColorEdit3(label, ref rgb, ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.PickerHueWheel))
                Set(c => write(c, Hex(new Vec4(rgb, 1))));
            ImGui.PopStyleColor();
            finished |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(help + (own ? "\n\nSaját szín." : "\n\nBeépített szín; módosításkor saját színné válik."));
            if (own) {
                ImGui.SameLine(0, 4);
                if (EditorStyle.QuietButton("alap", "Vissza a beépített színre.")) { Set(c => write(c, null)); finished = true; }
            }
            ImGui.PopID();
        }
        ImGui.PushItemWidth(-EditorStyle.LabelWidth());
        EditorStyle.Section("Lomb évszakonként");
        Row("Tavasz", "A kibomló lomb színe; a teljes lombosodásig a nyári színre vált.", shown.Spring, c => c.Spring, (c, v) => c.Spring = v, !evergreen);
        Row("Nyár", "A korona alapszíne. Az örökzöldek egész évben ezt viselik.", shown.Summer, c => c.Summer, (c, v) => c.Summer = v);
        Row("Ősz", "Az őszi lomb színe lombhulláskor.", shown.Autumn, c => c.Autumn, (c, v) => c.Autumn = v, !evergreen);
        if (evergreen) EditorStyle.Text(HudTheme.Muted, "Örökzöld: tavasszal és ősszel is a nyári színét viseli, télen sem hullajtja.");
        EditorStyle.Section("Kéreg");
        Row("Kéreg színe", "A törzs és az ágak színe; a minta erre rajzolódik.", shown.Bark, c => c.Bark, (c, v) => c.Bark = v);
        int pattern = Math.Max(0, Array.IndexOf(BarkPatternCodes, traits.Pattern));
        bool ownPattern = colours?.BarkPattern != null;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ownPattern ? EditorStyle.Fade(HudTheme.AmberAccent, .14f) : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        if (ImGui.Combo("Kéregminta", ref pattern, BarkPatternLabels, BarkPatternLabels.Length)) { Set(c => c.BarkPattern = TreeColoursDefinition.BarkPatterns[pattern]); finished = true; }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A játék shaderében rajzolt kéregfelület. Közelről, nagyítva látszik a legjobban.");
        if (ownPattern) {
            ImGui.SameLine(0, 4);
            if (EditorStyle.QuietButton("alap##pattern", "Vissza a beépített mintára.")) { Set(c => c.BarkPattern = null); finished = true; }
        }
        Row("Holtfa színe", "Az elpusztult, álló és kidőlt fa színe.", shown.Dead, c => c.Dead, (c, v) => c.Dead = v);
        ImGui.PopItemWidth();
        ImGui.Spacing();
        if (ImGui.Button("Kéreg közelről")) { previewMode = 0; previewZoom = 6; previewPitch = .12f; previewPan = Vec2.Zero; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Az előnézet a törzs aljára nagyít; görgővel vissza.");
        ImGui.SameLine();
        ImGui.BeginDisabled(colours == null);
        if (ImGui.Button("Minden szín vissza a beépítettre")) { sp.Colours = null; finished = true; }
        ImGui.EndDisabled();
        ImGui.EndChild();
        ImGui.SameLine(0, 10);
        shownShapeLevel = -1;
        ImGui.BeginChild("colour-preview", Vec2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        TreePreview(species, TreeLifePhase.Mature, false, context, sp);
        ImGui.EndChild();
        if (finished) { Try(() => ApplyProject(world, false)); message = sp.Name + ": színek módosítva; a tesztvilág fái újraszíneződnek."; }
    }

    // ── Preview ──────────────────────────────────────────────────────────
    /// <summary>One background build of a preview object: the newest request is built as soon as the previous build ends.</summary>
    private sealed class PreviewBuild<T> where T : class
    {
        private Task<T> task;
        private string taskKey, valueKey;
        internal T Value { get; private set; }
        internal bool Current(string key) => Value != null && valueKey == key && task == null;
        internal bool Busy => task != null;
        internal string Error { get; private set; }
        /// <summary>Why the value is not current for <paramref name="key"/>: the error, still building, or built for another key.</summary>
        internal string Why(string key) => Error ?? (task != null ? $"épül ({taskKey}; kész érték kulcsa: {valueKey ?? "nincs"})" : Value == null ? "még nem indult" : $"más kulcs: {valueKey} ≠ {key}");

        internal void Update(string key, Func<T> build)
        {
            if (task is { IsCompleted: true }) {
                try { Value = task.GetAwaiter().GetResult(); Error = null; }
                catch (Exception e) { Error = e.GetBaseException().Message; }
                valueKey = taskKey; task = null;
            }
            if (task == null && valueKey != key) { taskKey = key; task = Task.Run(build); }
        }
    }

    private readonly PreviewBuild<DendroTreeGenerator.Mesh> editedMesh = new(), builtinMesh = new();
    private readonly PreviewBuild<TreeSkeleton> editedSkeleton = new(), builtinSkeleton = new();
    private TreePreviewRenderer treeRenderer;
    private string previewKey, builtinKey;
    // 0 = as in the game, 1 = the raw branch structure, 2 = the whole life course.
    private int previewMode;
    private bool lifeTrueScale = true;
    private readonly PreviewBuild<LifeStage[]> lifeCourse = new();
    private string lifeKey;
    private bool previewCompare = true, previewSeeThrough, previewLeafPoints = true;
    private float previewPitch = .28f, previewZoom = 1;
    // Pan offset of the view in tree-frame units, and how far one pixel of mouse travel moves it in the last drawn view.
    private Vec2 previewPan, panPerPixel;
    private int panMode = -1;
    private const ImGuiButtonFlags ViewButtons = ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle;
    // The branch level the hovered parameter shapes (0 trunk, 1 limbs, 2 side branches, 3 twigs); -1 = the whole tree.
    private int hoveredShapeLevel = -1, shownShapeLevel = -1;
    private bool Seasonal => previewSeeThrough && !previewBare;

    /// <summary>Why the shown preview could not be built, if it failed.</summary>
    internal string TreePreviewError => previewMode == 0 ? editedMesh.Error ?? builtinMesh.Error
        : previewMode == 1 ? editedSkeleton.Error ?? builtinSkeleton.Error : lifeCourse.Why(lifeKey);
    internal bool TreePreviewReady => previewKey != null && (previewMode == 0
        ? editedMesh.Current(previewKey + "|" + Seasonal) && (!previewCompare || builtinMesh.Current(builtinKey + "|" + Seasonal))
        : previewMode == 1 ? editedSkeleton.Current(previewKey) && builtinSkeleton.Current(builtinKey)
        : lifeCourse.Current(lifeKey));

    /// <summary>The branch level a parameter shapes, from its name ("1Length" → 1) or its group; -1 for the whole tree.</summary>
    private static int BranchLevel(TreeShapeParameters.Entry e) =>
        char.IsDigit(e.Name[0]) ? e.Name[0] - '0' : e.Group switch { "Törzs" => 0, "Vázágak" => 1, "Mellékágak" => 2, _ => -1 };

    private void TreePreview(ForestSpecies species, TreeLifePhase phase, bool dead, object context, SpeciesDefinition sp)
    {
        EditorStyle.Tabs("preview-mode", new[] { "Játékban", "Ágszerkezet", "Életút" }, ref previewMode);
        if (previewMode == 2) { LifeCoursePreview(species, context, sp); return; }
        // The built-in look of this species (form and colours), with the project's other species edits: what the comparison shows.
        var (form, colours) = (sp.Shape, sp.Colours); sp.Shape = null; sp.Colours = null;
        object builtinContext = ForestSpeciesBook.Compose(GameWorld.SpeciesEdits(scenario));
        (sp.Shape, sp.Colours) = (form, colours);

        // Representative size for the phase: the middle of its age range, sized by the game's initial-size graphs.
        float age = PhaseAge(species, phase, sp);
        var size = ForestSpeciesBook.WithContext(context, () => ForestTreeGrowth.Initial(species, Math.Max(.5f, age), .5f));
        int seed = previewVariant * 7919 + 13;
        string revision = LookRevision(context, species);
        string common = string.Join("|", species, phase, dead, previewLight, previewSeason, previewVariant, size, previewMode);
        previewKey = common + "|" + revision; builtinKey = common + "|built-in";

        string note = (dead ? $"Elhalt fa ({PhaseLabels[(int)phase].ToLowerInvariant()} fázisban pusztult el) · " : PhaseLabels[(int)phase] + " · ")
            + $"{age:0} év · {size.Height:0.#} m magas, {size.CrownRadius * 2:0.#} m széles korona. "
            + (previewMode == 0
                ? "A magasságot és a korona szélességét a növekedési szabályok adják; ezek a paraméterek a fa formáját, sűrűségét és ágait alakítják."
                : "A generátor nyers ágváza saját léptékben: itt az ághosszak és szögek hatása közvetlenül látszik.");
        float noteHeight = ImGui.CalcTextSize(note, false, ImGui.GetContentRegionAvail().X).Y;
        Vec2 area = ImGui.GetContentRegionAvail() - new Vec2(0, ImGui.GetFrameHeightWithSpacing() * 2 + noteHeight + 14);
        area = Vec2.Max(area, new Vec2(40, 40));
        Vec2 origin = ImGui.GetCursorScreenPos();
        if (previewMode == 0) {
            var spec = new TreeShapeSpec(species, seed, phase, size, dead ? 0 : 1, new TreeSite(TreeShapeBands.BandLight(previewLight)),
                dead ? LeafState.Bare : SeasonLeaves(species), 0, dead);
            // See-through: the leafy crown over the complete leafless branch system, as the game keeps both for the seasons.
            bool seasonal = Seasonal;
            DendroTreeGenerator.Mesh Build(object books) => ForestSpeciesBook.WithContext(books, () => seasonal
                ? DendroTreeGenerator.GenerateSeasonal(spec, ForestLod.Near) : DendroTreeGenerator.Generate(spec, ForestLod.Near));
            editedMesh.Update(previewKey + "|" + seasonal, () => Build(context));
            if (previewCompare) builtinMesh.Update(builtinKey + "|" + seasonal, () => Build(builtinContext));
            GamePreview(origin, area, size);
        } else {
            editedSkeleton.Update(previewKey, () => ForestSpeciesBook.WithContext(context, () => TreeArchitecture.Skeleton(species, seed, phase, previewLight, dead)));
            builtinSkeleton.Update(builtinKey, () => ForestSpeciesBook.WithContext(builtinContext, () => TreeArchitecture.Skeleton(species, seed, phase, previewLight, dead)));
            ImGui.InvisibleButton("tree-preview", area, ViewButtons);
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, origin + area, EditorStyle.U(.105f, .135f, .15f));
            draw.PushClipRect(origin, origin + area, true);
            SkeletonPreview(draw, origin, area);
            draw.PopClipRect();
        }
        // Pan is in the units of the view it was made in; another view starts centred.
        if (panMode != previewMode) { panMode = previewMode; previewPan = Vec2.Zero; }
        Orbit();
        if ((editedMesh.Error ?? editedSkeleton.Error) is string error) EditorStyle.Text(HudTheme.Bad, "Az előnézet nem készült el: " + error);
        if (editedMesh.Busy || editedSkeleton.Busy || builtinMesh.Busy || builtinSkeleton.Busy)
            ImGui.GetWindowDrawList().AddText(origin + new Vec2(10, area.Y - 24), EditorStyle.TextMuted, "Fa építése…");

        // Controls under the view.
        ImGui.SetCursorScreenPos(origin + new Vec2(0, area.Y + 6));
        float third = (ImGui.GetContentRegionAvail().X - 16) / 3;
        ImGui.SetNextItemWidth(third); ImGui.Combo("##light", ref previewLight, LightLabels, LightLabels.Length);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A fa ekkora fényben nőtt: a játék a fényhez igazítja az ágazást.");
        ImGui.SameLine(0, 8);
        if (previewMode == 0) {
            ImGui.SetNextItemWidth(third); ImGui.Combo("##season", ref previewSeason, SeasonLabels, SeasonLabels.Length);
            ImGui.SameLine(0, 8);
            ImGui.BeginDisabled(previewBare); ImGui.Checkbox("Áttetsző korona", ref previewSeeThrough); ImGui.EndDisabled();
        } else ImGui.Checkbox("Lombpontok", ref previewLeafPoints);
        ImGui.Checkbox("Összevetés a gyárival", ref previewCompare);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(previewMode == 0 ? "Bal oldalt a beépített forma, jobb oldalt a szerkesztett, azonos léptékben."
            : "A beépített ágszerkezet halványan, a szerkesztett színesen, egymásra rajzolva.");
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - 130));
        if (ImGui.Button("‹##variant")) previewVariant = (previewVariant + TreeArchitecture.Variants - 1) % TreeArchitecture.Variants;
        ImGui.SameLine(0, 4); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{previewVariant + 1}. egyed");
        ImGui.SameLine(0, 4); if (ImGui.Button("›##variant")) previewVariant = (previewVariant + 1) % TreeArchitecture.Variants;
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, note);
        ImGui.PopTextWrapPos();
    }

    /// <summary>Age in the middle of a phase; the last phase lasts until the species' maximum age.</summary>
    private static float PhaseAge(ForestSpecies species, TreeLifePhase phase, SpeciesDefinition sp)
    {
        float start = phase == TreeLifePhase.Seedling ? 0 : TreeLifePhases.NextAge(species, phase - 1);
        float end = TreeLifePhases.NextAge(species, phase);
        return float.IsInfinity(end) ? start + Math.Max(5, (float)sp.MaximumAgeYears - start) * .5f : (start + end) / 2;
    }

    /// <summary>One step of a tree's life: what it is called, the page that shapes it, and the tree as the game builds it.</summary>
    private sealed record LifeStage(string Label, int Tab, float Age, ForestTreeDimensions Size, DendroTreeGenerator.Mesh Mesh, bool Fallen);

    /// <summary>
    /// The whole life of one tree in a row, as the game shows it: the six phases, a weakened old tree losing limbs, the standing
    /// dead tree and the fallen one. True scale shows how it grows; otherwise each is enlarged to the same height.
    /// </summary>
    private void LifeCoursePreview(ForestSpecies species, object context, SpeciesDefinition sp)
    {
        int seed = previewVariant * 7919 + 13, light = previewLight; var leaves = SeasonLeaves(species);
        lifeKey = string.Join("|", species, light, leaves, previewVariant, sp.MaximumAgeYears, sp.MatureAgeYears, LookRevision(context, species));
        var ages = Enum.GetValues<TreeLifePhase>().Select(p => PhaseAge(species, p, sp)).ToArray();
        lifeCourse.Update(lifeKey, () => ForestSpeciesBook.WithContext(context, () => {
            var site = new TreeSite(TreeShapeBands.BandLight(light));
            LifeStage Stage(string label, int tab, TreeLifePhase phase, float vigor, bool dead, bool fallen)
            {
                float age = ages[(int)phase];
                var size = ForestTreeGrowth.Initial(species, Math.Max(.5f, age), .5f);
                var mesh = DendroTreeGenerator.Generate(new TreeShapeSpec(species, seed, phase, size, vigor, site,
                    dead ? LeafState.Bare : leaves, 0, dead), ForestLod.Near);
                return new(label, tab, age, size, fallen ? Fallen(mesh, size.Diameter * TreeScale.MetresToWorld) : mesh, fallen);
            }
            var stages = Enum.GetValues<TreeLifePhase>().Select(p => Stage(PhaseLabels[(int)p], (int)p + 1, p, 1, false, false)).ToList();
            stages.Add(Stage("Legyengült", 5, TreeLifePhase.Old, .2f, false, false));
            stages.Add(Stage("Elhalt", DeadTab, TreeLifePhase.Old, 0, true, false));
            stages.Add(Stage("Kidőlt", DeadTab, TreeLifePhase.Old, 0, true, true));
            return stages.ToArray();
        }));

        Vec2 area = ImGui.GetContentRegionAvail() - new Vec2(0, ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing() * 5 + 44);
        area = Vec2.Max(area, new Vec2(40, 40));
        Vec2 origin = ImGui.GetCursorScreenPos();
        var stagesShown = lifeCourse.Value;
        if (stagesShown == null) { ImGui.InvisibleButton("life-course", area); }
        else {
            // Slots side by side on one ground line, crowns standing close as in a stand; a fallen tree lies along its slot.
            float reference = stagesShown.Max(s => s.Size.Height) * TreeScale.MetresToWorld;
            int n = stagesShown.Length;
            var natural = new float[n]; var scales = new float[n];
            float top = 0;
            for (int i = 0; i < n; i++) {
                var s = stagesShown[i];
                float height = s.Size.Height * TreeScale.MetresToWorld, crown = s.Size.CrownRadius * TreeScale.MetresToWorld;
                scales[i] = lifeTrueScale ? 1 : reference * .9f / Math.Max(1e-3f, height);
                natural[i] = Math.Max(reference * .06f, (s.Fallen ? height * 1.05f : Math.Max(crown * 1.6f, height * .3f)) * scales[i]);
                top = Math.Max(top, (s.Fallen ? crown : height) * scales[i]);
            }
            float gap = reference * .02f;
            // Labels alternate rows, so a label shares the width of two slots: small trees get room for half a label.
            float perPixel = Math.Max((natural.Sum() + gap * (n - 1)) / area.X, top * 1.3f / area.Y);
            var widths = natural.Select((w, i) => Math.Max(w, .55f * (ImGui.CalcTextSize("000 év · 00.0 m").X + 8) * perPixel)).ToArray();
            var trees = new List<(DendroTreeGenerator.Mesh, OpenTK.Mathematics.Vector3, float)>();
            var centres = new float[n];
            float x = 0;
            for (int i = 0; i < n; i++) {
                var s = stagesShown[i];
                float height = s.Size.Height * TreeScale.MetresToWorld;
                centres[i] = x + widths[i] / 2;
                trees.Add((s.Mesh, new(centres[i] - (s.Fallen ? height * .5f * scales[i] : 0), 0, 0), scales[i]));
                x += widths[i] + gap;
            }
            float total = x - gap, viewHeight = Math.Max(top * 1.3f, total * area.Y / area.X);
            float viewWidth = viewHeight * area.X / area.Y;
            const float pitch = .12f;
            // Ground a little above the bottom edge, the tallest crown well inside the top.
            var target = new OpenTK.Mathematics.Vector3(total / 2, 0, viewHeight * .43f);
            var eye = target + new OpenTK.Mathematics.Vector3(0, -MathF.Cos(pitch), MathF.Sin(pitch)) * total * 3;
            var viewProjection = OpenTK.Mathematics.Matrix4.LookAt(eye, target, OpenTK.Mathematics.Vector3.UnitZ)
                * OpenTK.Mathematics.Matrix4.CreateOrthographic(viewWidth, viewHeight, total * .5f, total * 6);
            float pixels = ImGui.GetIO().DisplayFramebufferScale.X * 1.5f;
            treeRenderer ??= new TreePreviewRenderer();
            treeRenderer.Render(trees, new(Math.Min(4096, (int)(area.X * pixels)), Math.Min(2048, (int)(area.Y * pixels))), viewProjection, 1);
            ImGui.Image((IntPtr)treeRenderer.Texture, area, new Vec2(0, 1), new Vec2(1, 0));
            // Labels under the slots; a click opens the page that shapes that stage.
            var draw = ImGui.GetWindowDrawList();
            for (int i = 0; i < stagesShown.Length; i++) {
                var s = stagesShown[i];
                float sx = origin.X + area.X / 2 + (centres[i] - total / 2) / viewWidth * area.X;
                string label = s.Label, detail = s.Label is "Elhalt" or "Kidőlt" ? (s.Label == "Kidőlt" ? "+2 év" : $"{s.Age:0} év") : $"{s.Age:0} év · {s.Size.Height:0.#} m";
                float w = Math.Max(ImGui.CalcTextSize(label).X, ImGui.CalcTextSize(detail).X);
                // Alternate rows keep neighbouring names apart.
                Vec2 at = new(Math.Clamp(sx - w / 2, origin.X, origin.X + area.X - w), origin.Y + area.Y + 4 + (i % 2) * (ImGui.GetTextLineHeight() * 2 + 4));
                draw.AddLine(new Vec2(sx, origin.Y + area.Y), new Vec2(sx, at.Y), EditorStyle.U(EditorStyle.Hairline));
                ImGui.SetCursorScreenPos(at);
                bool active = shapePhase == s.Tab;
                if (ImGui.InvisibleButton("life-" + i, new Vec2(Math.Max(w, 10), ImGui.GetTextLineHeight() * 2 + 2))) shapePhase = s.Tab;
                bool hover = ImGui.IsItemHovered();
                if (hover) ImGui.SetTooltip("Kattintás: ennek a szakasznak a paraméterei.");
                draw.AddText(at + new Vec2((w - ImGui.CalcTextSize(label).X) / 2, 0), active ? EditorStyle.U(HudTheme.AmberAccent) : hover ? EditorStyle.TextBright : EditorStyle.U(HudTheme.Parchment), label);
                draw.AddText(at + new Vec2((w - ImGui.CalcTextSize(detail).X) / 2, ImGui.GetTextLineHeight() + 1), EditorStyle.TextMuted, detail);
            }
        }
        if (lifeCourse.Busy) ImGui.GetWindowDrawList().AddText(origin + new Vec2(10, 8), EditorStyle.TextMuted, "Életút építése…");
        if (lifeCourse.Error is string error) EditorStyle.Text(HudTheme.Bad, "Az életút nem készült el: " + error);

        ImGui.SetCursorScreenPos(origin + new Vec2(0, area.Y + ImGui.GetTextLineHeight() * 4 + 18));
        float third = (ImGui.GetContentRegionAvail().X - 16) / 3;
        ImGui.SetNextItemWidth(third); ImGui.Combo("##light", ref previewLight, LightLabels, LightLabels.Length);
        ImGui.SameLine(0, 8);
        ImGui.SetNextItemWidth(third); ImGui.Combo("##season", ref previewSeason, SeasonLabels, SeasonLabels.Length);
        ImGui.SameLine(0, 8); ImGui.Checkbox("Valós méretarány", ref lifeTrueScale);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Bekapcsolva a fák valós méretükben, egymás mellett; kikapcsolva mind azonos magasságra nagyítva.");
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - 130));
        if (ImGui.Button("‹##life-variant")) previewVariant = (previewVariant + TreeArchitecture.Variants - 1) % TreeArchitecture.Variants;
        ImGui.SameLine(0, 4); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{previewVariant + 1}. egyed");
        ImGui.SameLine(0, 4); if (ImGui.Button("›##life-variant")) previewVariant = (previewVariant + 1) % TreeArchitecture.Variants;
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, $"Egy fa élete a magonctól a kidőlésig. A méretet és az életkort a növekedési szabályok adják "
            + $"(érett kor {sp.MatureAgeYears:0}, legnagyobb kor {sp.MaximumAgeYears:0} év); a legyengült fán ágak száradnak el, az elhalt fa két év múlva kidől. "
            + "Egy szakasz nevére kattintva annak a paraméterei nyílnak meg.");
        ImGui.PopTextWrapPos();
    }

    /// <summary>A dead tree lying on the ground, as the game turns it two years after death: along x, resting on its stem.</summary>
    private static DendroTreeGenerator.Mesh Fallen(DendroTreeGenerator.Mesh mesh, float stem)
    {
        static Vertex[] Lay(Vertex[] source, float lift) => source.Select(v => new Vertex(
            new(v.Position.Z, v.Position.Y, -v.Position.X + lift), new(v.Normal.Z, v.Normal.Y, -v.Normal.X), v.Color)).ToArray();
        return mesh with { Trunk = Lay(mesh.Trunk, stem * .5f), Branches = Lay(mesh.Branches, stem * .5f), Crown = Array.Empty<Vertex>() };
    }

    /// <summary>
    /// Left drag turns and tilts, right or middle drag (or Shift + left) pans, the wheel zooms, arrow keys turn,
    /// double-click resets; acts on the last drawn view.
    /// </summary>
    private void Orbit()
    {
        bool hovered = ImGui.IsItemHovered(), active = ImGui.IsItemActive();
        var io = ImGui.GetIO();
        bool panning = active && (ImGui.IsMouseDragging(ImGuiMouseButton.Right, 0) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 0)
            || (io.KeyShift && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0)));
        if (panning) {
            previewPan += new Vec2(-io.MouseDelta.X * panPerPixel.X, io.MouseDelta.Y * panPerPixel.Y);
            previewPan = Vec2.Clamp(previewPan, new Vec2(-3), new Vec2(3));
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
        else if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0)) {
            previewYaw -= io.MouseDelta.X * .01f; previewPitch = Math.Clamp(previewPitch + io.MouseDelta.Y * .006f, -.15f, 1.45f);
        }
        // Smooth, proportional zoom so a trackpad and a wheel feel alike.
        if (hovered && io.MouseWheel != 0) previewZoom = Math.Clamp(previewZoom * MathF.Exp(io.MouseWheel * .12f), .3f, 30);
        if (hovered) {
            float turn = io.DeltaTime * 1.6f;
            if (ImGui.IsKeyDown(ImGuiKey.LeftArrow)) previewYaw += turn;
            if (ImGui.IsKeyDown(ImGuiKey.RightArrow)) previewYaw -= turn;
            if (ImGui.IsKeyDown(ImGuiKey.UpArrow)) previewPitch = Math.Clamp(previewPitch + turn * .6f, -.15f, 1.45f);
            if (ImGui.IsKeyDown(ImGuiKey.DownArrow)) previewPitch = Math.Clamp(previewPitch - turn * .6f, -.15f, 1.45f);
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { previewYaw = .6f; previewPitch = .28f; previewZoom = 1; previewPan = Vec2.Zero; }
        if (hovered && !active) ImGui.SetTooltip("Bal húzás: forgatás · jobb/középső vagy Shift+bal húzás: mozgatás · görgő: nagyítás · nyilak: forgatás · dupla kattintás: alaphelyzet");
    }

    /// <summary>The game's meshes in 3D, the built-in form on the left when comparing, framed by the phase's size.</summary>
    private void GamePreview(Vec2 origin, Vec2 area, ForestTreeDimensions size)
    {
        float scale = ImGui.GetIO().DisplayFramebufferScale.X * 1.5f;
        var pixels = new OpenTK.Mathematics.Vector2i(Math.Min(2048, (int)(area.X * scale)), Math.Min(2048, (int)(area.Y * scale)));
        float height = size.Height * TreeScale.MetresToWorld, crown = size.CrownRadius * TreeScale.MetresToWorld;
        bool compare = previewCompare && builtinMesh.Value != null;
        float spacing = Math.Max(crown * 2.6f, height * .55f);
        var trees = new List<(DendroTreeGenerator.Mesh, OpenTK.Mathematics.Vector3, float)>();
        if (compare) trees.Add((builtinMesh.Value, new(-spacing / 2, 0, 0), 1));
        if (editedMesh.Value != null) trees.Add((editedMesh.Value, new(compare ? spacing / 2 : 0, 0, 0), 1));
        // A fixed frame for the phase: a longer or wider tree looks longer or wider, it is not refitted.
        float extent = Math.Max(height * 1.1f, (compare ? spacing + crown * 2 : crown * 2) * 1.15f * area.Y / Math.Max(1, area.X));
        float distance = extent * 2.1f / previewZoom;
        // Zooming in moves the view down to the lower trunk, where the bark is seen.
        float closeUp = Math.Clamp((previewZoom - 2) / 6, 0, 1);
        // A close-up looks at the edited tree, not at the gap between the two.
        float focusX = compare ? spacing / 2 * closeUp : 0;
        var target = new OpenTK.Mathematics.Vector3(focusX, 0, height * (.48f - .4f * closeUp));
        var direction = new OpenTK.Mathematics.Vector3(MathF.Sin(previewYaw) * MathF.Cos(previewPitch),
            -MathF.Cos(previewYaw) * MathF.Cos(previewPitch), MathF.Sin(previewPitch));
        // Pan slides the view across the screen plane; one pixel moves it by the visible height over the view height.
        const float fieldOfView = MathF.PI / 6.5f;
        var right = new OpenTK.Mathematics.Vector3(MathF.Cos(previewYaw), MathF.Sin(previewYaw), 0);
        var up = OpenTK.Mathematics.Vector3.Cross(right, -direction).Normalized();
        float perPixel = 2 * distance * MathF.Tan(fieldOfView / 2) / Math.Max(1, area.Y) / extent;
        panPerPixel = new(perPixel, perPixel);
        target += (right * previewPan.X + up * previewPan.Y) * extent;
        var eye = target + distance * direction;
        var view = OpenTK.Mathematics.Matrix4.LookAt(eye, target, OpenTK.Mathematics.Vector3.UnitZ);
        var projection = OpenTK.Mathematics.Matrix4.CreatePerspectiveFieldOfView(fieldOfView, area.X / area.Y, distance * .05f, distance * 4);
        if (trees.Count > 0) {
            treeRenderer ??= new TreePreviewRenderer();
            treeRenderer.Render(trees, pixels, view * projection, Seasonal ? .22f : 1);
            ImGui.Image((IntPtr)treeRenderer.Texture, area, new Vec2(0, 1), new Vec2(1, 0));
        }
        // The image takes no input: an invisible button over it receives the drags and the wheel.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("tree-preview", area, ViewButtons);
        if (compare) {
            var draw = ImGui.GetWindowDrawList();
            draw.AddText(origin + new Vec2(12, 10), EditorStyle.TextMuted, "Gyári");
            draw.AddText(origin + new Vec2(area.X - ImGui.CalcTextSize("Szerkesztett").X - 12, 10), EditorStyle.U(HudTheme.AmberAccent), "Szerkesztett");
        }
    }

    private static readonly Vec4[] LevelColours = {
        new(.62f, .43f, .27f, 1), new(.93f, .62f, .30f, 1), new(.95f, .85f, .40f, 1), new(.55f, .80f, .45f, 1) };

    /// <summary>
    /// The generator's raw branch structure, in one scale for the built-in and the edited tree (the built-in height fills the
    /// view), branch levels coloured, the hovered parameter's level highlighted, drawn back to front.
    /// </summary>
    private void SkeletonPreview(ImDrawListPtr draw, Vec2 origin, Vec2 area)
    {
        var edited = editedSkeleton.Value; var builtin = builtinSkeleton.Value ?? edited;
        if (edited == null) return;
        float cy = MathF.Cos(previewYaw), sy = MathF.Sin(previewYaw), cp = MathF.Cos(previewPitch), spt = MathF.Sin(previewPitch);
        float scale = area.Y * .8f / Math.Max(1e-3f, builtin.Height) * previewZoom;
        panPerPixel = new(1 / Math.Max(1, area.Y), 1 / Math.Max(1, area.Y));
        Vec2 baseAt = origin + new Vec2(area.X / 2, area.Y * .92f - (previewZoom - 1) * area.Y * .3f) + new Vec2(-previewPan.X, previewPan.Y) * area.Y;
        (Vec2 Screen, float Depth) Project(OpenTK.Mathematics.Vector3 p)
        {
            float x = p.X * cy - p.Y * sy, y = p.X * sy + p.Y * cy;
            return (baseAt + new Vec2(x, -(p.Z * cp - y * spt)) * scale, y * cp + p.Z * spt);
        }
        // Thickness: the trunk base a few pixels wide, twigs one pixel.
        float baseUnit = TreeSkeleton.UnitRadius(edited.FlowOf(0)[0]);
        float twig = Math.Max(.6f, area.Y * .012f * previewZoom / Math.Max(1, baseUnit));
        var segments = new List<(float Depth, Vec2 A, Vec2 B, float Width, uint Colour)>();
        void Add(TreeSkeleton skeleton, bool ghost)
        {
            for (int s = 0; s < skeleton.StemCount; s++) {
                var points = skeleton.PointsOf(s); var flow = skeleton.FlowOf(s);
                int level = Math.Min(3, skeleton.Stems[s].Level);
                bool lit = shownShapeLevel < 0 || shownShapeLevel == level;
                var colour = ghost ? new Vec4(.75f, .8f, .85f, .22f) : EditorStyle.Fade(LevelColours[level], lit ? 1 : .22f);
                for (int i = 1; i < points.Length; i++) {
                    var a = Project(points[i - 1]); var b = Project(points[i]);
                    float width = Math.Max(1, TreeSkeleton.UnitRadius(flow[i - 1]) * twig) * (lit && shownShapeLevel >= 0 && !ghost ? 1.6f : 1);
                    segments.Add(((a.Depth + b.Depth) / 2, a.Screen, b.Screen, width, EditorStyle.U(colour)));
                }
            }
        }
        if (previewCompare && builtinSkeleton.Value != null) Add(builtinSkeleton.Value, true);
        Add(edited, false);
        segments.Sort((x, y) => y.Depth.CompareTo(x.Depth));
        draw.AddEllipseFilled(baseAt, new Vec2(area.X * .16f, 5), EditorStyle.U(0, 0, 0, .3f));
        foreach (var (_, a, b, width, colour) in segments) draw.AddLine(a, b, colour, width);
        if (previewLeafPoints)
            foreach (var leaf in edited.Leaves) draw.AddCircleFilled(Project(leaf).Screen, 2.2f, EditorStyle.U(.45f, .75f, .35f, .55f));

        // Legend; the hovered parameter's level is named.
        string[] names = { "Törzs", "Vázágak", "Mellékágak", "Gallyak" };
        Vec2 at = origin + new Vec2(10, 10);
        for (int level = 0; level < names.Length; level++) {
            bool lit = shownShapeLevel < 0 || shownShapeLevel == level;
            draw.AddRectFilled(at + new Vec2(0, 4), at + new Vec2(10, 14), EditorStyle.U(EditorStyle.Fade(LevelColours[level], lit ? 1 : .3f)), 2);
            draw.AddText(at + new Vec2(16, 0), lit ? EditorStyle.TextBright : EditorStyle.TextMuted, names[level]);
            at.Y += 18;
        }
        if (previewCompare) draw.AddText(at + new Vec2(0, 4), EditorStyle.TextMuted, "halvány: gyári");
        draw.AddText(origin + new Vec2(area.X - 150, 10), EditorStyle.TextMuted, $"{edited.StemCount} ág · {edited.LeafTotal} lombpont");
    }
}
