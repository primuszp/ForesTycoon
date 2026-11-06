using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed class SoilEditorView(EditorPageContext context)
{
    private SceneSession scene => context.Scene;
    private void ApplyScenario(GameWorld world, string text) => context.ApplyScenario(world, text);
    private string message { set => context.SetMessage(value); }
    private ref int selectedSoilType => ref context.Ui.Soils.Selected;
    private ref bool scrollToSoilProfile => ref context.Ui.Soils.ScrollToProfile;
    internal void Draw(GameWorld world)
    {
        EditorStyle.BeginPanel("types-list", new Vec2(ImGui.GetFontSize() * 15.5f, 0));
        EditorStyle.Section("Talajtípusok");
        selectedSoilType = Math.Clamp(selectedSoilType, 0, scene.World.SoilTypes.Count - 1);
        for (int i = 0; i < scene.World.SoilTypes.Count; i++)
            if (SoilSwatch(scene.World.SoilTypes[i], i == selectedSoilType, "type" + i)) selectedSoilType = i;
        ImGui.Spacing();
        if (ImGui.Button("Új talajtípus", new Vec2(-1, 0))) {
            scene.CopySoil(scene.World.SoilTypes[selectedSoilType]); selectedSoilType = scene.World.SoilTypes.Count - 1;
            message = "Új talajtípus. Festéssel kerül a térképre.";
        }
        var type = scene.World.SoilTypes[selectedSoilType];
        bool used = scene.World.SoilPaint.ContainsValue(type.Id) || (scene.World.InitialState != null &&
            Enumerable.Range(0, world.Map.Tiles.Count).Any(t => world.Soils.Profile(t).Id == type.Id));
        ImGui.BeginDisabled(scene.World.SoilTypes.Count == 1 || used);
        if (ImGui.Button("Törlés", new Vec2(-1, 0))) { scene.DeleteSoil(type, used); selectedSoilType = 0; ApplyScenario(world, "Talajtípus törölve."); }
        ImGui.EndDisabled();
        if (used) ImGui.TextWrapped("Használatban van a térképen, ezért nem törölhető.");
        EditorStyle.EndPanel(); ImGui.SameLine();

        EditorStyle.BeginPanel("type-form", Vec2.Zero);
        type = scene.World.SoilTypes[selectedSoilType];
        EditorStyle.Title(type.Name);
        int count = 0, tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int t = 0; t < tiles; t++) if (world.SoilIndex(t) == selectedSoilType) count++;
        EditorStyle.Text(HudTheme.Muted, $"A tesztvilágban {count} csempe ({100.0 * count / Math.Max(1, tiles):0}%) · azonosító: {type.Id}");
        bool changed = false;
        void Done() { if (ImGui.IsItemDeactivatedAfterEdit()) changed = true; }
        if (ImGui.BeginTable("type-fields", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) {
            ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthFixed, 260);
            ImGui.TableSetupColumn("Hol hat");
            void Row(string label, string effect, Action field) {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
                ImGui.TableSetColumnIndex(1); ImGui.SetNextItemWidth(-1); field();
                ImGui.TableSetColumnIndex(2); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, effect); ImGui.PopTextWrapPos();
            }
            Row("Név", "Megjelenik a térképen és a játékban.", () => { string n = type.Name; if (ImGui.InputText("##name", ref n, 80) && n.Trim().Length > 0) scene.Edit(type, edited => edited.Name = n.Trim()); Done(); });
            Row("Szín", "A térkép színe.", () => { var c = Colour(type.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
                if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) scene.Edit(type, edited => edited.Color = Hex(new Vec4(rgb, 1))); Done(); });
            Row("Teherbírás (0–1)", "Földút és közelítőnyom járhatósága átázva: talaj.teherbírás a járhatósági szabályban.", () => { float v = (float)type.BearingCapacity;
                if (ImGui.SliderFloat("##bearing", ref v, 0, 1, "%.2f")) scene.Edit(type, edited => edited.BearingCapacity = Math.Round(v, 3)); Done(); });
            Row("Termékenység (0–1)", "Termőhelyi alkalmasság: a fák növekedése és egészsége.", () => { float v = (float)type.Fertility;
                if (ImGui.SliderFloat("##fertility", ref v, 0, 1, "%.2f")) scene.Edit(type, edited => edited.Fertility = Math.Round(v, 3)); Done(); });
            Row("Telítettség (mm)", "A talaj teljes víztartása; e fölött pangó víz.", () => { double v = type.Saturation; if (ImGui.InputDouble("##sat", ref v, 5, 20, "%.0f")) scene.Edit(type, edited => edited.Saturation = v); Done(); });
            Row("Szabadföldi vízkapacitás (mm)", "E fölött a víz elszivárog a mélybe.", () => { double v = type.FieldCapacity; if (ImGui.InputDouble("##fc", ref v, 5, 20, "%.0f")) scene.Edit(type, edited => edited.FieldCapacity = v); Done(); });
            Row("Hervadáspont (mm)", "Ez alatt a fák nem jutnak vízhez: aszály.", () => { double v = type.WiltingPoint; if (ImGui.InputDouble("##wp", ref v, 1, 10, "%.0f")) scene.Edit(type, edited => edited.WiltingPoint = v); Done(); });
            Row("Beszivárgás (mm/óra)", "Milyen gyorsan nyeli el a csapadékot; a többi lefolyik.", () => { double v = type.InfiltrationPerHour; if (ImGui.InputDouble("##inf", ref v, 1, 5, "%.1f")) scene.Edit(type, edited => edited.InfiltrationPerHour = v); Done(); });
            Row("Drénezés (mm/óra)", "Milyen gyorsan szivárog a többletvíz a mélybe.", () => { double v = type.DrainagePerHour; if (ImGui.InputDouble("##dr", ref v, .1, 1, "%.2f")) scene.Edit(type, edited => edited.DrainagePerHour = v); Done(); });
            Row("Generálás: nedvesség (0–1)", "Hová teszi a generátor: nedvesebb vagy szárazabb helyekre.", () => { float v = (float)type.PreferredMoisture;
                if (ImGui.SliderFloat("##pm", ref v, 0, 1, "%.2f")) scene.Edit(type, edited => edited.PreferredMoisture = Math.Round(v, 3)); Done(); });
            Row("Generálás: textúra (0–1)", "Durva (homok) vagy finom (agyag) foltokba kerül.", () => { float v = (float)type.PreferredTexture;
                if (ImGui.SliderFloat("##pt", ref v, 0, 1, "%.2f")) scene.Edit(type, edited => edited.PreferredTexture = Math.Round(v, 3)); Done(); });
            ImGui.EndTable();
        }
        if (changed) ApplyScenario(world, $"{type.Name} módosítva.");
        DrawProfile(type);
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A típusok új világ létrehozásakor kerülnek a játékba; a futó játék világa a régi talajjal fut tovább. A szerkesztőben a tesztvilág minden módosításkor újraépül.");
        if (scene.World.InitialState != null) EditorStyle.Text(HudTheme.Muted, "A meglévő talajfoltok a helyükön maradnak; kisebb telítettségnél a többletvíz a felszínre kerül.");
        ImGui.PopTextWrapPos();
        EditorStyle.EndPanel();
    }

    /// <summary>
    /// The soil's horizons from the surface down, as the diorama's sides show them: a drawn column next to the editable list.
    /// The built-in profile is shown until the first edit, which copies it into the soil.
    /// </summary>
    internal void DrawProfile(SoilView type)
    {
        EditorStyle.Section("Rétegrend · a dioráma oldala");
        if (scrollToSoilProfile) { scrollToSoilProfile = false; ImGui.SetScrollHereY(0.1f); }
        var shown = type.Profile();
        bool builtin = type.Horizons is not { Count: > 0 };
        void Edit(Action<List<SoilHorizon>> change) => scene.EditHorizons(type, change);

        // The column: true thicknesses times the diorama scale, bedrock below, a depth scale on the left.
        float columnWidth = ImGui.GetFontSize() * 5, height = ImGui.GetFontSize() * 16;
        double total = shown.Sum(h => h.Depth);
        float perMetre = height / (float)Math.Max(.5, total * 1.15);
        Vec2 at = ImGui.GetCursorScreenPos() + new Vec2(ImGui.GetFontSize() * 2.4f, 4);
        var draw = ImGui.GetWindowDrawList();
        float y = at.Y;
        foreach (var h in shown) {
            float next = y + (float)h.Depth * perMetre;
            draw.AddRectFilled(new Vec2(at.X, y), new Vec2(at.X + columnWidth, next), EditorStyle.U(Colour(h.Color)));
            y = next;
        }
        draw.AddRectFilled(new Vec2(at.X, y), new Vec2(at.X + columnWidth, at.Y + height), EditorStyle.U(Colour(SoilHorizon.Bedrock)));
        draw.AddRect(at, at + new Vec2(columnWidth, height), EditorStyle.U(EditorStyle.Hairline));
        for (double m = 0; m <= total * 1.15; m += .5) {
            float ty = at.Y + (float)m * perMetre;
            draw.AddLine(new Vec2(at.X - 5, ty), new Vec2(at.X, ty), EditorStyle.TextMuted);
            draw.AddText(new Vec2(at.X - ImGui.GetFontSize() * 2.3f, ty - ImGui.GetFontSize() / 2), EditorStyle.TextMuted, $"{m:0.#} m");
        }
        Vec2 start = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vec2(at.X + columnWidth + 16, start.Y));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        EditorStyle.Text(HudTheme.Muted, builtin ? "A talaj beépített szelvénye; az első módosítás a talajtípushoz másolja." : "Saját szelvény. A sorrend felülről lefelé.");
        ImGui.PopTextWrapPos();
        int remove = -1, up = -1;
        for (int i = 0; i < shown.Count; i++) {
            var h = shown[i];
            ImGui.PushID("horizon" + i);
            var c = Colour(h.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
            if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) Edit(horizons => horizons[i].Color = Hex(new Vec4(rgb, 1)));
            ImGui.SameLine();
            string name = h.Name; ImGui.SetNextItemWidth(ImGui.GetFontSize() * 11);
            if (ImGui.InputText("##name", ref name, 40) && name.Trim().Length > 0) Edit(horizons => horizons[i].Name = name.Trim());
            ImGui.SameLine();
            float depth = (float)h.Depth; ImGui.SetNextItemWidth(ImGui.GetFontSize() * 6);
            if (ImGui.DragFloat("##depth", ref depth, .01f, .01f, 5, "%.2f m")) Edit(horizons => horizons[i].Depth = Math.Round(Math.Clamp(depth, .01, 5), 2));
            ImGui.SameLine();
            ImGui.BeginDisabled(i == 0); if (EditorStyle.QuietButton("Fel")) up = i; ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Egy szinttel feljebb.");
            ImGui.SameLine();
            ImGui.BeginDisabled(shown.Count == 1); if (EditorStyle.QuietButton("Törlés")) remove = i; ImGui.EndDisabled();
            ImGui.PopID();
        }
        if (up > 0) Edit(own => { (own[up - 1], own[up]) = (own[up], own[up - 1]); });
        if (remove >= 0) Edit(own => own.RemoveAt(remove));
        ImGui.BeginDisabled(shown.Count >= 8);
        if (ImGui.Button("Új szint alulra")) Edit(own => own.Add(new SoilHorizon { Name = "Új szint", Depth = .3, Color = shown[^1].Color }));
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(builtin);
        if (ImGui.Button("Beépített szelvény")) scene.Edit(type, edited => edited.Horizons = new());
        ImGui.EndDisabled();
        float scale = (float)scene.World.ProfileScale; ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
        if (ImGui.SliderFloat("Nagyítás a dioráma oldalán", ref scale, 1, 30, "%.0f×")) scene.EditWorld(scene => scene.ProfileScale = Math.Round(scale));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A vékony szintek a valóságos vastagságuknál ennyiszer mélyebbre rajzolódnak, hogy a terepasztal oldalán látszódjanak. Minden talajra közös.");
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(new Vec2(start.X, Math.Max(ImGui.GetCursorScreenPos().Y, at.Y + height + 8)));
        ImGui.Dummy(new Vec2(1, 1));
    }

}
