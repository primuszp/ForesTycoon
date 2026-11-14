using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ForesTycoon.TreeModels;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

using static ForesTycoon.Editor.TreeAppearancePresentation;
/// <summary>Builds and presents tree previews, and owns their GPU lifetime.</summary>
internal sealed class TreeAppearancePreview(SceneSession scene, TreePreviewUiState state) : IDisposable
{
    private static readonly string[] LightLabels = { "Árnyék", "Félárnyék", "Napos" };
    private ref int shapePhase => ref state.Phase;
    private ref int previewLight => ref state.Light;
    private ref int previewVariant => ref state.Variant;
    private float previewYaw => state.Yaw;
    internal void FocusBark() { state.Mode = 0; state.Zoom = 6; state.Pitch = .12f; state.Pan = Vec2.Zero; }
    // 0 spring, 1 summer, 2 autumn, 3 winter.
    private ref int previewSeason => ref state.Season;
    private static readonly string[] SeasonLabels = { "Tavasz", "Nyár", "Ősz", "Tél" };
    private bool previewBare => previewSeason == 3;

    /// <summary>The leaves of a living tree in the previewed season; evergreens keep theirs in winter.</summary>
    private LeafState SeasonLeaves(ForestSpecies species) => previewSeason switch
    {
        0 => TreePhenology.Evergreen(species) ? LeafState.Full : LeafState.Budding,
        2 => TreePhenology.Evergreen(species) ? LeafState.Full : LeafState.Autumn,
        3 => TreePhenology.Evergreen(species) ? LeafState.Full : LeafState.Bare,
        _ => LeafState.Full
    };

    private readonly EditorPreviewBuild<DendroTreeGenerator.Mesh> editedMesh = new(), builtinMesh = new();
    private readonly EditorPreviewBuild<TreeSkeleton> editedSkeleton = new(), builtinSkeleton = new();
    private TreePreviewRenderer treeRenderer;
    private string previewKey, builtinKey;
    // 0 = as in the game, 1 = the raw branch structure, 2 = the whole life course.
    private ref int previewMode => ref state.Mode;
    private ref bool lifeTrueScale => ref state.TrueScale;
    private readonly EditorPreviewBuild<LifeStage[]> lifeCourse = new();
    private string lifeKey;
    private ref bool previewCompare => ref state.Compare;
    private ref bool previewSeeThrough => ref state.SeeThrough;
    private ref bool previewLeafPoints => ref state.LeafPoints;
    private ref float previewPitch => ref state.Pitch;
    private ref float previewZoom => ref state.Zoom;
    // Pan offset of the view in tree-frame units, and how far one pixel of mouse travel moves it in the last drawn view.
    private ref Vec2 previewPan => ref state.Pan;
    private readonly TreePreviewCamera camera = new(state);
    private ref int panMode => ref state.PanMode;
    private const ImGuiButtonFlags ViewButtons = ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle;
    // The branch level the hovered parameter shapes (0 trunk, 1 limbs, 2 side branches, 3 twigs); -1 = the whole tree.
    private ref int hoveredShapeLevel => ref state.HoveredLevel;
    private ref int shownShapeLevel => ref state.ShownLevel;
    private bool Seasonal => previewSeeThrough && !previewBare;

    /// <summary>Why the shown preview could not be built, if it failed.</summary>
    internal string Error => previewMode == 0 ? editedMesh.Error ?? builtinMesh.Error
        : previewMode == 1 ? editedSkeleton.Error ?? builtinSkeleton.Error : lifeCourse.Why(lifeKey);
    internal bool Ready => previewMode switch
    {
        0 => previewKey != null && editedMesh.Current(previewKey + "|" + Seasonal)
            && (!previewCompare || builtinMesh.Current(builtinKey + "|" + Seasonal)),
        1 => previewKey != null && editedSkeleton.Current(previewKey) && builtinSkeleton.Current(builtinKey),
        2 => lifeKey != null && lifeCourse.Current(lifeKey),
        _ => false
    };

    internal void Draw(ForestSpecies species, TreeLifePhase phase, bool dead, object context, SpeciesView sp)
    {
        EditorStyle.Tabs("preview-mode", new[] { "Játékban", "Ágszerkezet", "Életút" }, ref previewMode);
        if (previewMode == 2) { LifeCoursePreview(species, context, sp); return; }
        // The built-in look of this species (form and colours), with the project's other species edits: what the comparison shows.
        var comparison = scene.SpeciesEdits();
        if (comparison.TryGetValue(species, out var authored)) comparison[species] = authored with { Shape = null, Look = null };
        object builtinContext = ForestSpeciesBook.Compose(comparison);

        // Representative size for the phase: the middle of its age range, sized by the game's initial-size graphs.
        float age = PhaseAge(species, phase, sp);
        var size = ForestSpeciesBook.WithContext(context, () => ForestTreeGrowth.Initial(species, Math.Max(.5f, age), .5f));
        int seed = previewVariant * 7919 + 13, light = previewLight;
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
        if (previewMode == 0)
        {
            var spec = new TreeShapeSpec(species, seed, phase, size, dead ? 0 : 1, new TreeSite(TreeShapeBands.BandLight(previewLight)),
                dead ? LeafState.Bare : SeasonLeaves(species), 0, dead);
            // See-through: the leafy crown over the complete leafless branch system, as the game keeps both for the seasons.
            bool seasonal = Seasonal;
            DendroTreeGenerator.Mesh Build(object books) => ForestSpeciesBook.WithContext(books, () => seasonal
                ? DendroTreeGenerator.GenerateSeasonal(spec, ForestLod.Near) : DendroTreeGenerator.Generate(spec, ForestLod.Near));
            editedMesh.Update(previewKey + "|" + seasonal, () => Build(context));
            if (previewCompare) builtinMesh.Update(builtinKey + "|" + seasonal, () => Build(builtinContext));
            GamePreview(origin, area, size);
        }
        else
        {
            editedSkeleton.Update(previewKey, () => ForestSpeciesBook.WithContext(context, () => TreeArchitecture.Skeleton(species, seed, phase, light, dead)));
            builtinSkeleton.Update(builtinKey, () => ForestSpeciesBook.WithContext(builtinContext, () => TreeArchitecture.Skeleton(species, seed, phase, light, dead)));
            ImGui.InvisibleButton("tree-preview", area, ViewButtons);
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, origin + area, EditorStyle.U(.105f, .135f, .15f));
            draw.PushClipRect(origin, origin + area, true);
            SkeletonPreview(draw, origin, area);
            draw.PopClipRect();
        }
        // Pan is in the units of the view it was made in; another view starts centred.
        if (panMode != previewMode) { panMode = previewMode; previewPan = Vec2.Zero; }
        camera.HandleInput();
        if ((editedMesh.Error ?? editedSkeleton.Error) is string error) EditorStyle.Text(HudTheme.Bad, "Az előnézet nem készült el: " + error);
        if (editedMesh.Busy || editedSkeleton.Busy || builtinMesh.Busy || builtinSkeleton.Busy)
            ImGui.GetWindowDrawList().AddText(origin + new Vec2(10, area.Y - 24), EditorStyle.TextMuted, "Fa építése…");

        // Controls under the view.
        ImGui.SetCursorScreenPos(origin + new Vec2(0, area.Y + 6));
        float third = (ImGui.GetContentRegionAvail().X - 16) / 3;
        ImGui.SetNextItemWidth(third); ImGui.Combo("##light", ref previewLight, LightLabels, LightLabels.Length);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A fa ekkora fényben nőtt: a játék a fényhez igazítja az ágazást.");
        ImGui.SameLine(0, 8);
        if (previewMode == 0)
        {
            ImGui.SetNextItemWidth(third); ImGui.Combo("##season", ref previewSeason, SeasonLabels, SeasonLabels.Length);
            ImGui.SameLine(0, 8);
            ImGui.BeginDisabled(previewBare); ImGui.Checkbox("Áttetsző korona", ref previewSeeThrough); ImGui.EndDisabled();
        }
        else ImGui.Checkbox("Lombpontok", ref previewLeafPoints);
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
    private static float PhaseAge(ForestSpecies species, TreeLifePhase phase, SpeciesView sp)
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
    private void LifeCoursePreview(ForestSpecies species, object context, SpeciesView sp)
    {
        int seed = previewVariant * 7919 + 13, light = previewLight; var leaves = SeasonLeaves(species);
        lifeKey = string.Join("|", species, light, leaves, previewVariant, sp.MaximumAgeYears, sp.MatureAgeYears, LookRevision(context, species));
        var ages = Enum.GetValues<TreeLifePhase>().Select(p => PhaseAge(species, p, sp)).ToArray();
        lifeCourse.Update(lifeKey, () => ForestSpeciesBook.WithContext(context, () =>
        {
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
        else
        {
            // Slots side by side on one ground line, crowns standing close as in a stand; a fallen tree lies along its slot.
            float reference = stagesShown.Max(s => s.Size.Height) * TreeScale.MetresToWorld;
            int n = stagesShown.Length;
            var natural = new float[n]; var scales = new float[n];
            float top = 0;
            for (int i = 0; i < n; i++)
            {
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
            for (int i = 0; i < n; i++)
            {
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
            for (int i = 0; i < stagesShown.Length; i++)
            {
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
        camera.PanPerPixel = new(perPixel, perPixel);
        target += (right * previewPan.X + up * previewPan.Y) * extent;
        var eye = target + distance * direction;
        var view = OpenTK.Mathematics.Matrix4.LookAt(eye, target, OpenTK.Mathematics.Vector3.UnitZ);
        var projection = OpenTK.Mathematics.Matrix4.CreatePerspectiveFieldOfView(fieldOfView, area.X / area.Y, distance * .05f, distance * 4);
        if (trees.Count > 0)
        {
            treeRenderer ??= new TreePreviewRenderer();
            treeRenderer.Render(trees, pixels, view * projection, Seasonal ? .22f : 1);
            ImGui.Image((IntPtr)treeRenderer.Texture, area, new Vec2(0, 1), new Vec2(1, 0));
        }
        // The image takes no input: an invisible button over it receives the drags and the wheel.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("tree-preview", area, ViewButtons);
        if (compare)
        {
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
        camera.PanPerPixel = new(1 / Math.Max(1, area.Y), 1 / Math.Max(1, area.Y));
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
            for (int s = 0; s < skeleton.StemCount; s++)
            {
                var points = skeleton.PointsOf(s); var flow = skeleton.FlowOf(s);
                int level = Math.Min(3, skeleton.Stems[s].Level);
                bool lit = shownShapeLevel < 0 || shownShapeLevel == level;
                var colour = ghost ? new Vec4(.75f, .8f, .85f, .22f) : EditorStyle.Fade(LevelColours[level], lit ? 1 : .22f);
                for (int i = 1; i < points.Length; i++)
                {
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
        for (int level = 0; level < names.Length; level++)
        {
            bool lit = shownShapeLevel < 0 || shownShapeLevel == level;
            draw.AddRectFilled(at + new Vec2(0, 4), at + new Vec2(10, 14), EditorStyle.U(EditorStyle.Fade(LevelColours[level], lit ? 1 : .3f)), 2);
            draw.AddText(at + new Vec2(16, 0), lit ? EditorStyle.TextBright : EditorStyle.TextMuted, names[level]);
            at.Y += 18;
        }
        if (previewCompare) draw.AddText(at + new Vec2(0, 4), EditorStyle.TextMuted, "halvány: gyári");
        draw.AddText(origin + new Vec2(area.X - 150, 10), EditorStyle.TextMuted, $"{edited.StemCount} ág · {edited.LeafTotal} lombpont");
    }
    public void Dispose()
    {
        editedMesh.Dispose(); builtinMesh.Dispose(); editedSkeleton.Dispose(); builtinSkeleton.Dispose(); lifeCourse.Dispose();
        treeRenderer?.Dispose(); treeRenderer = null;
    }
}
