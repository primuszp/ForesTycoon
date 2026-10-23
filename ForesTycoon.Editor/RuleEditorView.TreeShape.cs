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

    // 0 = every phase, 1..6 = one life phase.
    private int speciesTab, shapePhase, previewLight = 2, previewVariant;
    private bool previewBare, showAllShapeParameters;
    private float previewYaw = .6f;
    private string shapeFilter = "";


    internal void ShowSpeciesAppearance(int index, int phase = 0) { ShowWorldTypes(TypeTab.Species, index); speciesTab = 1; shapePhase = phase; }
    internal void ApplyEditsForTest(GameWorld world) => ApplyProject(world, false);
    internal void SetTreePreviewForTest(int mode, int level, bool seeThrough) { previewMode = mode; testShapeLevel = level; previewSeeThrough = seeThrough; }
    private int? testShapeLevel;

    private static TreeLifePhase Phase(int selection) => (TreeLifePhase)Math.Clamp(selection - 1, 0, 5);

    private void TreeShapeArea(GameWorld world, SpeciesDefinition sp)
    {
        if (!Enum.TryParse(sp.Id, out ForestSpecies species) || species == ForestSpecies.None) {
            EditorStyle.Text(HudTheme.Muted, "Ennek a fafajnak nincs famodellje."); return;
        }
        EditorStyle.Tabs("shape-phase", new[] { "Minden fázis" }.Concat(PhaseLabels).ToArray(), ref shapePhase);
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, shapePhase == 0
            ? "Közös értékek minden életfázisra. A játék ezekből indul, és a fa korához és a fényhez igazítja őket (például fiatalon kevesebb, árnyékban meredekebb ág)."
            : $"{PhaseLabels[shapePhase - 1]} fázis saját értékei. Ezek végső értékek: a játék kor- és fényigazítása után érvényesek, csak ebben a fázisban.");
        ImGui.PopTextWrapPos();

        // The draft form as the game would see it; previews and current values come from it, not from the applied project.
        object context = ForestSpeciesBook.Compose(GameWorld.SpeciesEdits(scenario));
        var phase = Phase(shapePhase);
        var shown = ForestSpeciesBook.WithContext(context, () => shapePhase == 0
            ? TreeArchitecture.BaseParameters(species) : TreeArchitecture.EffectiveParameters(species, phase, previewLight));

        float previewWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * .5f, 300, 820);
        ImGui.BeginChild("shape-parameters", new Vec2(-previewWidth - 10, 0));
        bool finished = ShapeParameterList(sp, shown);
        ImGui.EndChild();
        ImGui.SameLine(0, 10);
        shownShapeLevel = testShapeLevel ?? hoveredShapeLevel;
        ImGui.BeginChild("shape-preview", Vec2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        TreePreview(species, phase, context, sp);
        ImGui.EndChild();
        if (finished) { Try(() => ApplyProject(world, false)); message = sp.Name + ": megjelenés módosítva; a tesztvilág fái újraépülnek."; }
    }

    /// <summary>The authored values of the selected level: the common set or one phase's set (created on first write).</summary>
    private Dictionary<string, double> ShapeLevel(SpeciesDefinition sp, bool create)
    {
        if (sp.Shape == null) { if (!create) return null; sp.Shape = new(); }
        if (shapePhase == 0) return sp.Shape.All;
        string key = TreeShapeDefinition.PhaseNames[shapePhase - 1];
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
            var used = PhaseLabels.Where((_, i) => phases.ContainsKey(TreeShapeDefinition.PhaseNames[i])).ToArray();
            if (used.Length > 0) EditorStyle.Text(HudTheme.Muted, "Saját értékei vannak: " + string.Join(", ", used));
        }

        ImGui.PushItemWidth(-EditorStyle.LabelWidth());
        hoveredShapeLevel = -1;
        foreach (string group in TreeShapeParameters.Groups) {
            var entries = TreeShapeParameters.Common.Where(e => e.Group == group && Matches(filter, e.Label, e.Name, e.Help)).ToArray();
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
            if (EditorStyle.QuietButton("↺", "Saját érték törlése: ismét az örökölt érték érvényes.")) { authored.Remove(e.Name); TidyShape(sp); finished = true; }
        }
        ImGui.PopID();
        return finished;
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
    // 0 = as in the game, 1 = the raw branch structure.
    private int previewMode;
    private bool previewCompare = true, previewSeeThrough, previewLeafPoints = true;
    private float previewPitch = .28f, previewZoom = 1;
    // The branch level the hovered parameter shapes (0 trunk, 1 limbs, 2 side branches, 3 twigs); -1 = the whole tree.
    private int hoveredShapeLevel = -1, shownShapeLevel = -1;
    private bool Seasonal => previewSeeThrough && !previewBare;

    internal bool TreePreviewReady => previewKey != null && (previewMode == 0
        ? editedMesh.Current(previewKey + "|" + Seasonal) && (!previewCompare || builtinMesh.Current(builtinKey + "|" + Seasonal))
        : editedSkeleton.Current(previewKey) && builtinSkeleton.Current(builtinKey));

    /// <summary>The branch level a parameter shapes, from its name ("1Length" → 1) or its group; -1 for the whole tree.</summary>
    private static int BranchLevel(TreeShapeParameters.Entry e) =>
        char.IsDigit(e.Name[0]) ? e.Name[0] - '0' : e.Group switch { "Törzs" => 0, "Vázágak" => 1, "Mellékágak" => 2, _ => -1 };

    private void TreePreview(ForestSpecies species, TreeLifePhase phase, object context, SpeciesDefinition sp)
    {
        EditorStyle.Tabs("preview-mode", new[] { "Játékban", "Ágszerkezet" }, ref previewMode);
        // The built-in form of this species, with the project's other species edits: what the comparison shows.
        var form = sp.Shape; sp.Shape = null;
        object builtinContext = ForestSpeciesBook.Compose(GameWorld.SpeciesEdits(scenario));
        sp.Shape = form;

        // Representative size for the phase: the middle of its age range, sized by the game's initial-size graphs.
        float start = phase == TreeLifePhase.Seedling ? 0 : TreeLifePhases.NextAge(species, phase - 1);
        float end = TreeLifePhases.NextAge(species, phase);
        float age = float.IsInfinity(end) ? start + Math.Max(5, (float)sp.MaximumAgeYears - start) * .5f : (start + end) / 2;
        var size = ForestSpeciesBook.WithContext(context, () => ForestTreeGrowth.Initial(species, Math.Max(.5f, age), .5f));
        int seed = previewVariant * 7919 + 13;
        string revision = ForestSpeciesBook.WithContext(context, () => ForestSpeciesBook.Shape(species)?.Revision ?? "");
        string common = string.Join("|", species, phase, previewLight, previewBare, previewVariant, size, previewMode);
        previewKey = common + "|" + revision; builtinKey = common + "|built-in";

        string note = $"{PhaseLabels[(int)phase]} · {age:0} év · {size.Height:0.#} m magas, {size.CrownRadius * 2:0.#} m széles korona. "
            + (previewMode == 0
                ? "A magasságot és a korona szélességét a növekedési szabályok adják; ezek a paraméterek a fa formáját, sűrűségét és ágait alakítják."
                : "A generátor nyers ágváza saját léptékben: itt az ághosszak és szögek hatása közvetlenül látszik.");
        float noteHeight = ImGui.CalcTextSize(note, false, ImGui.GetContentRegionAvail().X).Y;
        Vec2 area = ImGui.GetContentRegionAvail() - new Vec2(0, ImGui.GetFrameHeightWithSpacing() * 2 + noteHeight + 14);
        area = Vec2.Max(area, new Vec2(40, 40));
        Vec2 origin = ImGui.GetCursorScreenPos();
        if (previewMode == 0) {
            var spec = new TreeShapeSpec(species, seed, phase, size, 1, new TreeSite(TreeShapeBands.BandLight(previewLight)),
                previewBare ? LeafState.Bare : LeafState.Full, 0);
            // See-through: the leafy crown over the complete leafless branch system, as the game keeps both for the seasons.
            bool seasonal = Seasonal;
            DendroTreeGenerator.Mesh Build(object books) => ForestSpeciesBook.WithContext(books, () => seasonal
                ? DendroTreeGenerator.GenerateSeasonal(spec, ForestLod.Near) : DendroTreeGenerator.Generate(spec, ForestLod.Near));
            editedMesh.Update(previewKey + "|" + seasonal, () => Build(context));
            if (previewCompare) builtinMesh.Update(builtinKey + "|" + seasonal, () => Build(builtinContext));
            GamePreview(origin, area, size);
        } else {
            editedSkeleton.Update(previewKey, () => ForestSpeciesBook.WithContext(context, () => TreeArchitecture.Skeleton(species, seed, phase, previewLight)));
            builtinSkeleton.Update(builtinKey, () => ForestSpeciesBook.WithContext(builtinContext, () => TreeArchitecture.Skeleton(species, seed, phase, previewLight)));
            ImGui.InvisibleButton("tree-preview", area);
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, origin + area, EditorStyle.U(.105f, .135f, .15f));
            draw.PushClipRect(origin, origin + area, true);
            SkeletonPreview(draw, origin, area);
            draw.PopClipRect();
        }
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
            int season = previewBare ? 1 : 0; ImGui.SetNextItemWidth(third);
            if (ImGui.Combo("##season", ref season, new[] { "Lombos", "Lombtalan" }, 2)) previewBare = season == 1;
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

    /// <summary>Drag to turn and tilt, wheel to zoom, double-click to reset; acts on the last drawn view.</summary>
    private void Orbit()
    {
        bool hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) {
            var delta = ImGui.GetIO().MouseDelta;
            previewYaw -= delta.X * .01f; previewPitch = Math.Clamp(previewPitch + delta.Y * .006f, -.15f, 1.3f);
        }
        if (hovered && ImGui.GetIO().MouseWheel != 0) previewZoom = Math.Clamp(previewZoom * (1 + ImGui.GetIO().MouseWheel * .12f), .5f, 5);
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { previewYaw = .6f; previewPitch = .28f; previewZoom = 1; }
        if (hovered) ImGui.SetTooltip("Húzás: forgatás és döntés · görgő: nagyítás · dupla kattintás: alaphelyzet");
    }

    /// <summary>The game's meshes in 3D, the built-in form on the left when comparing, framed by the phase's size.</summary>
    private void GamePreview(Vec2 origin, Vec2 area, ForestTreeDimensions size)
    {
        float scale = ImGui.GetIO().DisplayFramebufferScale.X * 1.5f;
        var pixels = new OpenTK.Mathematics.Vector2i(Math.Min(2048, (int)(area.X * scale)), Math.Min(2048, (int)(area.Y * scale)));
        float height = size.Height * TreeScale.MetresToWorld, crown = size.CrownRadius * TreeScale.MetresToWorld;
        bool compare = previewCompare && builtinMesh.Value != null;
        float spacing = Math.Max(crown * 2.6f, height * .55f);
        var trees = new List<(DendroTreeGenerator.Mesh, OpenTK.Mathematics.Vector3)>();
        if (compare) trees.Add((builtinMesh.Value, new(-spacing / 2, 0, 0)));
        if (editedMesh.Value != null) trees.Add((editedMesh.Value, new(compare ? spacing / 2 : 0, 0, 0)));
        // A fixed frame for the phase: a longer or wider tree looks longer or wider, it is not refitted.
        float extent = Math.Max(height * 1.1f, (compare ? spacing + crown * 2 : crown * 2) * 1.15f * area.Y / Math.Max(1, area.X));
        float distance = extent * 2.1f / previewZoom;
        var target = new OpenTK.Mathematics.Vector3(0, 0, height * .48f);
        var eye = target + distance * new OpenTK.Mathematics.Vector3(MathF.Sin(previewYaw) * MathF.Cos(previewPitch),
            -MathF.Cos(previewYaw) * MathF.Cos(previewPitch), MathF.Sin(previewPitch));
        var view = OpenTK.Mathematics.Matrix4.LookAt(eye, target, OpenTK.Mathematics.Vector3.UnitZ);
        var projection = OpenTK.Mathematics.Matrix4.CreatePerspectiveFieldOfView(MathF.PI / 6.5f, area.X / area.Y, distance * .05f, distance * 4);
        if (trees.Count > 0) {
            treeRenderer ??= new TreePreviewRenderer();
            treeRenderer.Render(trees, pixels, view * projection, Seasonal ? .22f : 1);
            ImGui.Image((IntPtr)treeRenderer.Texture, area, new Vec2(0, 1), new Vec2(1, 0));
        } else ImGui.InvisibleButton("tree-preview", area);
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
        Vec2 baseAt = origin + new Vec2(area.X / 2, area.Y * .92f - (previewZoom - 1) * area.Y * .3f);
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
