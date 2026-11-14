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
/// <summary>Shape and colour forms. Preview work, camera and graphics resources belong to the preview component.</summary>
internal sealed class TreeShapeEditorView(EditorPageContext context) : IDisposable
{
    private readonly TreeAppearancePreview preview = new(context.Scene, context.Ui.Tree);
    private SceneSession scene => context.Scene;
    private bool Try(Action action) => context.Try(action);
    private void ApplyProject(GameWorld world, bool publish) => context.ApplyProject(world, publish);
    private string message { set => context.SetMessage(value); }
    private ref int shapePhase => ref context.Ui.Tree.Phase;
    private ref int? testShapeLevel => ref context.Ui.Tree.TestLevel;
    private ref bool showAllShapeParameters => ref context.Ui.Tree.ShowAllParameters;
    private ref string shapeFilter => ref context.Ui.Tree.Filter;
    private ref int hoveredShapeLevel => ref context.Ui.Tree.HoveredLevel;
    private ref int shownShapeLevel => ref context.Ui.Tree.ShownLevel;
    private int previewLight => context.Ui.Tree.Light;
    private int previewMode => context.Ui.Tree.Mode;
    private string shapeRevisionSeen;
    internal bool TreePreviewReady => preview.Ready;
    internal string TreePreviewError => preview.Error;
    public void Dispose() => preview.Dispose();
    internal void DrawShape(GameWorld world, SpeciesView sp)
    {
        if (!Enum.TryParse(sp.Id, out ForestSpecies species) || species == ForestSpecies.None)
        {
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
        object context = ForestSpeciesBook.Compose(scene.SpeciesEdits());
        var phase = Phase(shapePhase);
        bool dead = shapePhase == DeadTab;
        // A form the generator cannot build falls back to the built-in tree; say so until the form changes.
        string revision = ForestSpeciesBook.WithContext(context, () => ForestSpeciesBook.Shape(species)?.Revision ?? "");
        if (revision != shapeRevisionSeen) { shapeRevisionSeen = revision; TreeArchitecture.LastFormError = null; }
        if (TreeArchitecture.LastFormError is string failed && failed.StartsWith(sp.Name, StringComparison.Ordinal))
        {
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
        preview.Draw(species, phase, dead, context, sp);
        ImGui.EndChild();
        if (finished) { if (Try(() => ApplyProject(world, false))) message = sp.Name + ": megjelenés módosítva; a tesztvilág fái újraépülnek."; }
    }

    private string ShapePhaseKey => shapePhase == 0 ? null : shapePhase == DeadTab
        ? TreeShapeDefinition.DeadState : TreeShapeDefinition.PhaseNames[shapePhase - 1];
    private IReadOnlyDictionary<string, double> ShapeLevel(SpeciesView species)
        => ShapePhaseKey == null ? species.Shape?.All : species.Shape?.Phases.GetValueOrDefault(ShapePhaseKey);

    /// <summary>The parameter rows, grouped; returns true when an edit was finished and should reach the test world.</summary>
    private bool ShapeParameterList(SpeciesView sp, IReadOnlyDictionary<string, double> shown)
    {
        bool finished = false;
        var authored = ShapeLevel(sp);
        int count = authored?.Count ?? 0;
        float half = (ImGui.GetContentRegionAvail().X - 8) / 2;
        ImGui.SetNextItemWidth(half);
        string filter = EditorStyle.Search("##shape-filter", "Keresés: paraméter…", ref shapeFilter);
        ImGui.SameLine();
        ImGui.BeginDisabled(count == 0);
        if (ImGui.Button((shapePhase == 0 ? "Közös értékek törlése" : "Fázis értékeinek törlése") + $" ({count})", new Vec2(-1, 0)))
        {
            scene.ResetShapePhase(sp, ShapePhaseKey); finished = true;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(shapePhase == 0 ? "Vissza a beépített fafajformához (a fázisok saját értékei megmaradnak)." : "Ebben a fázisban ismét a közös értékek és a játék igazítása érvényes.");
        if (shapePhase > 0 && sp.Shape?.Phases is { Count: > 0 } phases)
        {
            var used = PhaseLabels.Where((_, i) => phases.ContainsKey(TreeShapeDefinition.PhaseNames[i]))
                .Concat(phases.ContainsKey(TreeShapeDefinition.DeadState) ? new[] { "Elhalt" } : Array.Empty<string>()).ToArray();
            if (used.Length > 0) EditorStyle.Text(HudTheme.Muted, "Saját értékei vannak: " + string.Join(", ", used));
        }

        ImGui.PushItemWidth(-EditorStyle.LabelWidth());
        hoveredShapeLevel = -1;
        foreach (string group in TreeShapeParameters.Groups)
        {
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
            foreach (var group in TreeShapeParameters.Advanced.Where(e => Matches(filter, e.Name, e.Help)).GroupBy(e => e.Group))
            {
                EditorStyle.Section(group.Key);
                foreach (var e in group) finished |= ShapeRow(sp, e, shown);
            }
        ImGui.PopItemWidth();
        return finished;
    }

    /// <summary>One parameter: its value at the selected level, a dot when authored there, and a reset.</summary>
    private bool ShapeRow(SpeciesView sp, TreeShapeParameters.Entry e, IReadOnlyDictionary<string, double> shown)
    {
        var authored = ShapeLevel(sp);
        bool own = authored != null && authored.ContainsKey(e.Name);
        double value = own ? authored[e.Name] : TreeShapeParameters.ValueIn(shown, e.Name);
        ImGui.PushID("shape-" + e.Name);
        // Keep the reset inside the parameter panel, including on narrow editor windows.
        float resetWidth = ImGui.CalcTextSize("alap").X + 2 * ImGui.GetStyle().FramePadding.X + 4;
        float labelWidth = Math.Max(EditorStyle.LabelWidth(), ImGui.CalcTextSize(e.Label).X + ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.SetNextItemWidth(Math.Max(60, ImGui.GetContentRegionAvail().X - labelWidth - resetWidth));
        bool changed = false;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, own ? EditorStyle.Fade(HudTheme.AmberAccent, .14f) : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        if (e.Options != null)
        {
            int choice = Math.Clamp((int)Math.Round(value), 0, e.Options.Length - 1);
            if (ImGui.Combo(e.Label, ref choice, e.Options, e.Options.Length)) { value = choice; changed = true; }
        }
        else if (e.Integer)
        {
            int v = (int)Math.Round(value);
            if (ImGui.SliderInt(e.Label, ref v, (int)e.Min, (int)e.Max, "%d", ImGuiSliderFlags.AlwaysClamp)) { value = v; changed = true; }
        }
        else
        {
            float v = (float)value;
            string format = Math.Abs(e.Max - e.Min) >= 20 ? "%.0f" : Math.Abs(e.Max - e.Min) >= 2 ? "%.2f" : "%.3f";
            if (ImGui.SliderFloat(e.Label, ref v, (float)e.Min, (float)e.Max, format, ImGuiSliderFlags.AlwaysClamp)) { value = Math.Round(v, 4); changed = true; }
        }
        ImGui.PopStyleColor();
        bool finished = ImGui.IsItemDeactivatedAfterEdit() || changed && e.Options != null;
        if (ImGui.IsItemHovered()) hoveredShapeLevel = BranchLevel(e);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(e.Help + (own ? "\n\nEbben a szintben saját érték." : "\n\nÖrökölt érték; módosításkor saját értékké válik."));
        if (changed) scene.SetShapeParameter(sp, ShapePhaseKey, e.Name, value);
        if (own)
        {
            ImGui.SameLine(0, 4);
            if (EditorStyle.QuietButton("alap", "Saját érték törlése: ismét az örökölt érték érvényes.")) { scene.SetShapeParameter(sp, ShapePhaseKey, e.Name, null); finished = true; }
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
    internal void DrawColours(GameWorld world, SpeciesView sp)
    {
        if (!Enum.TryParse(sp.Id, out ForestSpecies species) || species == ForestSpecies.None)
        {
            EditorStyle.Text(HudTheme.Muted, "Ennek a fafajnak nincs famodellje."); return;
        }
        object context = ForestSpeciesBook.Compose(scene.SpeciesEdits());
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
        void Row(string label, string help, System.Drawing.Color current, Func<TreeColoursDefinition, string> read, SpeciesColourChannel channel, bool enabled = true)
        {
            ImGui.PushID(label);
            bool own = Get(read) != null;
            var rgb = new System.Numerics.Vector3(current.R / 255f, current.G / 255f, current.B / 255f);
            ImGui.BeginDisabled(!enabled);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, own ? EditorStyle.Fade(HudTheme.AmberAccent, .14f) : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
            if (ImGui.ColorEdit3(label, ref rgb, ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.PickerHueWheel))
                scene.SetSpeciesColour(sp, channel, Hex(new Vec4(rgb, 1)));
            ImGui.PopStyleColor();
            finished |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(help + (own ? "\n\nSaját szín." : "\n\nBeépített szín; módosításkor saját színné válik."));
            if (own)
            {
                ImGui.SameLine(0, 4);
                if (EditorStyle.QuietButton("alap", "Vissza a beépített színre.")) { scene.SetSpeciesColour(sp, channel, null); finished = true; }
            }
            ImGui.PopID();
        }
        ImGui.PushItemWidth(-EditorStyle.LabelWidth());
        EditorStyle.Section("Lomb évszakonként");
        Row("Tavasz", "A kibomló lomb színe; a teljes lombosodásig a nyári színre vált.", shown.Spring, c => c.Spring, SpeciesColourChannel.Spring, !evergreen);
        Row("Nyár", "A korona alapszíne. Az örökzöldek egész évben ezt viselik.", shown.Summer, c => c.Summer, SpeciesColourChannel.Summer);
        Row("Ősz", "Az őszi lomb színe lombhulláskor.", shown.Autumn, c => c.Autumn, SpeciesColourChannel.Autumn, !evergreen);
        if (evergreen) EditorStyle.Text(HudTheme.Muted, "Örökzöld: tavasszal és ősszel is a nyári színét viseli, télen sem hullajtja.");
        EditorStyle.Section("Kéreg");
        Row("Kéreg színe", "A törzs és az ágak színe; a minta erre rajzolódik.", shown.Bark, c => c.Bark, SpeciesColourChannel.Bark);
        int pattern = Math.Max(0, Array.IndexOf(BarkPatternCodes, traits.Pattern));
        bool ownPattern = colours?.BarkPattern != null;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ownPattern ? EditorStyle.Fade(HudTheme.AmberAccent, .14f) : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        if (ImGui.Combo("Kéregminta", ref pattern, BarkPatternLabels, BarkPatternLabels.Length)) { scene.SetSpeciesColour(sp, SpeciesColourChannel.BarkPattern, TreeColoursDefinition.BarkPatterns[pattern]); finished = true; }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A játék shaderében rajzolt kéregfelület. Közelről, nagyítva látszik a legjobban.");
        if (ownPattern)
        {
            ImGui.SameLine(0, 4);
            if (EditorStyle.QuietButton("alap##pattern", "Vissza a beépített mintára.")) { scene.SetSpeciesColour(sp, SpeciesColourChannel.BarkPattern, null); finished = true; }
        }
        Row("Holtfa színe", "Az elpusztult, álló és kidőlt fa színe.", shown.Dead, c => c.Dead, SpeciesColourChannel.Dead);
        ImGui.PopItemWidth();
        ImGui.Spacing();
        if (ImGui.Button("Kéreg közelről")) preview.FocusBark();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Az előnézet a törzs aljára nagyít; görgővel vissza.");
        ImGui.SameLine();
        ImGui.BeginDisabled(colours == null);
        if (ImGui.Button("Minden szín vissza a beépítettre")) { scene.ResetSpeciesColours(sp); finished = true; }
        ImGui.EndDisabled();
        ImGui.EndChild();
        ImGui.SameLine(0, 10);
        shownShapeLevel = -1;
        ImGui.BeginChild("colour-preview", Vec2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        preview.Draw(species, TreeLifePhase.Mature, false, context, sp);
        ImGui.EndChild();
        if (finished) { if (Try(() => ApplyProject(world, false))) message = sp.Name + ": színek módosítva; a tesztvilág fái újraszíneződnek."; }
    }

}
