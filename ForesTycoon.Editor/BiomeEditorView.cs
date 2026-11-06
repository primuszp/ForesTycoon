using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The world pages: the generated and painted map, the types it is painted with (soil, species, surfaces), the vehicles'
/// data and game parameters, and the rain trial. <see cref="WorldTab"/> and <see cref="TypeTab"/> remain the entry points
/// other code uses to open a page.
/// </summary>
internal sealed class BiomeEditorView(EditorPageContext context, MapEditorView map)
{
    private SceneSession scene => context.Scene;
    private ref int selectedBiome => ref context.Ui.Biomes.Selected;
    private ref int biomeTab => ref context.Ui.Biomes.Tab;
    private ref int selectedMaterial => ref context.Ui.Biomes.Material;
    internal void Show(int index = 0, int tab = 0) { selectedBiome = index; biomeTab = tab; context.Navigate(EditorPage.Biomes); }
    internal void Draw(GameWorld world)
    {
        EditorStyle.Tabs("biome-tab", new[] { "Biomok", "Felszínanyagok" }, ref biomeTab);
        if (biomeTab == 1) { GroundMaterialsTab(world); return; }
        EditorStyle.BeginPanel("biome-list", new Vec2(ImGui.GetFontSize() * 15.5f, 0));
        EditorStyle.Section("Biomok");
        selectedBiome = Math.Clamp(selectedBiome, 0, Math.Max(0, scene.World.Biomes.Count - 1));
        foreach (var zone in scene.World.ElevationZones) {
            EditorStyle.Text(HudTheme.Muted, zone.Name);
            for (int i = 0; i < scene.World.Biomes.Count; i++)
                if (scene.World.Biomes[i].Zone == zone.Id && ColourSwatch(scene.World.Biomes[i].Name, Colour(scene.World.Biomes[i].Color), i == selectedBiome, "biome" + i)) selectedBiome = i;
            if (!scene.World.Biomes.Any(b => b.Zone == zone.Id)) EditorStyle.Text(HudTheme.Muted, "   (nincs biom)");
        }
        ImGui.Spacing();
        ImGui.BeginDisabled(scene.World.Biomes.Count >= 32);
        if (ImGui.Button("Új biom", new Vec2(-1, 0))) {
            var zone = scene.World.Biomes.Count > 0 ? scene.World.Biomes[selectedBiome].Zone : scene.World.ElevationZones[0].Id;
            scene.EditWorld(scene => scene.Biomes.Add(new BiomeDefinition { Id = "biome" + Guid.NewGuid().ToString("N")[..6], Name = "Új biom", Zone = zone, Color = "#8A9A6A" }));
            selectedBiome = scene.World.Biomes.Count - 1;
        }
        ImGui.EndDisabled();
        ImGui.BeginDisabled(scene.World.Biomes.Count == 0);
        if (ImGui.Button("Törlés", new Vec2(-1, 0))) { scene.EditWorld(scene => scene.Biomes.RemoveAt(selectedBiome)); selectedBiome = 0; }
        ImGui.EndDisabled();
        if (ImGui.Button("Alapértelmezett biomok", new Vec2(-1, 0))) { scene.EditWorld(scene => scene.Biomes = BiomeDefinition.Defaults()); selectedBiome = 0; }
        EditorStyle.EndPanel(); ImGui.SameLine();

        EditorStyle.BeginPanel("biome-form", Vec2.Zero);
        if (scene.World.Biomes.Count == 0) { EditorStyle.Text(HudTheme.Muted, "Nincs biom: minden csempe biom nélküli (tile.biome = -1)."); EditorStyle.EndPanel(); return; }
        var biome = scene.World.Biomes[selectedBiome];
        EditorStyle.Title(biome.Name);
        var counts = map.BiomeCounts(world); int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        EditorStyle.Text(HudTheme.Muted, $"A tesztvilágban {counts[selectedBiome]} csempe ({100.0 * counts[selectedBiome] / Math.Max(1, tiles):0}%) · szabályokban: tile.biome = {selectedBiome} · azonosító: {biome.Id}");
        ParameterTable("biome-fields", Row => {
            Row("Név", "A térképen, a csempeadatoknál és a játékban.", () => { string n = biome.Name; if (ImGui.InputText("##name", ref n, 60) && n.Trim().Length > 0) scene.Edit(biome, edited => edited.Name = n.Trim()); });
            Row("Szín", "A Biom térképréteg színe.", () => { var c = Colour(biome.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
                if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) scene.Edit(biome, edited => edited.Color = Hex(new Vec4(rgb, 1))); });
            Row("Magassági öv", "Ebben az övben fordul elő. Az övek határa a Térkép · Magassági övek rétegen állítható.", () => {
                int zone = Math.Max(0, scene.World.ElevationZones.FindIndex(z => z.Id == biome.Zone));
                if (ImGui.Combo("##zone", ref zone, scene.World.ElevationZones.Select(z => z.Name).ToArray(), scene.World.ElevationZones.Count)) scene.Edit(biome, edited => edited.Zone = scene.World.ElevationZones[zone].Id); });
            string[] names = scene.World.GroundMaterials.Select(m => m.Name).ToArray();
            void Ground(string label, string help, Func<string> get, Action<string> set) => Row(label, help, () => {
                int i = Math.Max(0, scene.World.GroundMaterials.FindIndex(m => m.Id == get()));
                if (ImGui.Combo("##" + label, ref i, names, names.Length)) set(scene.World.GroundMaterials[i].Id); });
            Ground("Száraz felszín", "Ez borítja a biom csempéit, ahol a talaj száraz vagy átlagos.", () => biome.DryGround, v => scene.Edit(biome, edited => edited.DryGround = v));
            Ground("Nedves felszín", "A nedves csempék (patak, mélyedés, tópart) felé erre vált át, fokozatosan.", () => biome.WetGround, v => scene.Edit(biome, edited => edited.WetGround = v));
            Ground("Meredek felszín", "A lejtős csempéken ez keveredik be: enyhe lejtőn kicsit, meredeken teljesen.", () => biome.SteepGround, v => scene.Edit(biome, edited => edited.SteepGround = v));
            Row("Részarány", "Ha az övben több biom van, ekkora súllyal osztoznak rajta, kb. 16 csempés foltokban.", () =>
                Number("##share", () => biome.Share, v => scene.Edit(biome, edited => edited.Share = v), .1, "%.2f", .01, 1000));
            Row("Leírás", "A táj jellege; a súgóban és a tervezésnél segít.", () => { string d = biome.Description ?? "";
                if (ImGui.InputTextMultiline("##description", ref d, 400, new Vec2(-1, ImGui.GetTextLineHeight() * 3.4f))) scene.Edit(biome, edited => edited.Description = d.Trim()); });
        });
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A biom a csempe övéből adódik, ezért terepszerkesztéskor vele együtt változik; tárolni nem kell. "
            + "A szabályok a tile.zone, tile.biome és tile.slope mezőkből olvassák; a térképen a Biom réteg mutatja.");
        ImGui.PopTextWrapPos();
        EditorStyle.EndPanel();
    }

    private static readonly string[] PatternNames = { "Fű", "Talaj, avar", "Homok", "Szikla", "Moha", "Kőtörmelék", "Hó" };
    /// <summary>The ground materials the biomes draw with: colour and detail pattern; a material a biome uses cannot be deleted.</summary>
    private void GroundMaterialsTab(GameWorld world)
    {
        var materials = scene.World.GroundMaterials;
        EditorStyle.BeginPanel("material-list", new Vec2(ImGui.GetFontSize() * 15.5f, 0));
        EditorStyle.Section("Felszínanyagok");
        selectedMaterial = Math.Clamp(selectedMaterial, 0, materials.Count - 1);
        for (int i = 0; i < materials.Count; i++)
            if (ColourSwatch(materials[i].Name, Colour(materials[i].Color), i == selectedMaterial, "material" + i)) selectedMaterial = i;
        ImGui.Spacing();
        ImGui.BeginDisabled(materials.Count >= 32);
        if (ImGui.Button("Új felszínanyag", new Vec2(-1, 0))) {
            scene.EditWorld(scene => scene.GroundMaterials.Add(new GroundMaterialDefinition { Id = "ground" + Guid.NewGuid().ToString("N")[..6], Name = "Új felszín", Color = materials[selectedMaterial].Color, Pattern = materials[selectedMaterial].Pattern }));
            selectedMaterial = materials.Count - 1;
        }
        ImGui.EndDisabled();
        var material = materials[selectedMaterial];
        var users = scene.World.Biomes.Where(b => b.DryGround == material.Id || b.WetGround == material.Id || b.SteepGround == material.Id).Select(b => b.Name).ToList();
        ImGui.BeginDisabled(users.Count > 0 || materials.Count == 1);
        if (ImGui.Button("Törlés", new Vec2(-1, 0))) { scene.DeleteMaterial(material); selectedMaterial = 0; }
        ImGui.EndDisabled();
        if (users.Count > 0) ImGui.TextWrapped("Biom használja, ezért nem törölhető.");
        if (ImGui.Button("Alapértelmezett anyagok", new Vec2(-1, 0))) {
            scene.ResetMaterials(); selectedMaterial = 0;
        }
        EditorStyle.EndPanel(); ImGui.SameLine();

        EditorStyle.BeginPanel("material-form", Vec2.Zero);
        materials = scene.World.GroundMaterials; material = materials[selectedMaterial];
        EditorStyle.Title(material.Name);
        EditorStyle.Text(HudTheme.Muted, users.Count == 0 ? $"Nem használja biom · azonosító: {material.Id}" : $"Használja: {string.Join(", ", users)} · azonosító: {material.Id}");
        ParameterTable("material-fields", Row => {
            Row("Név", "A biomok felszínválasztójában.", () => { string n = material.Name; if (ImGui.InputText("##name", ref n, 60) && n.Trim().Length > 0) scene.Edit(material, edited => edited.Name = n.Trim()); });
            Row("Szín", "A talaj alapszíne; a játék a szomszédos csempék színével keveri, és nagy foltokban kissé világosítja, sötétíti.", () => {
                var c = Colour(material.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
                if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) scene.Edit(material, edited => edited.Color = Hex(new Vec4(rgb, 1))); });
            Row("Minta", "A felszín apró részletei a játékban: fűszálak, homokfodrok, sziklarepedések, kavicsok…", () => {
                int p = Math.Max(0, Array.IndexOf(GroundMaterialDefinition.Patterns, material.Pattern));
                if (ImGui.Combo("##pattern", ref p, PatternNames, PatternNames.Length)) scene.Edit(material, edited => edited.Pattern = GroundMaterialDefinition.Patterns[p]); });
        });
        ImGui.Spacing();
        // A strip of the material next to the biomes' other grounds, as the map draws them.
        var draw = ImGui.GetWindowDrawList(); Vec2 at = ImGui.GetCursorScreenPos(); float size = ImGui.GetFontSize() * 3;
        draw.AddRectFilled(at, at + new Vec2(size * 4, size), EditorStyle.U(Colour(material.Color)), 4);
        draw.AddRect(at, at + new Vec2(size * 4, size), EditorStyle.U(EditorStyle.Hairline), 4);
        ImGui.Dummy(new Vec2(size * 4, size + 4));
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A játék ezt a színt és mintát rajzolja azokon a csempéken, ahol egy biom ezt az anyagot használja; a Térkép · Felszín réteg mutatja, hol.");
        ImGui.PopTextWrapPos();
        EditorStyle.EndPanel();
    }

}
