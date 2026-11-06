using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed class SpeciesEditorView(EditorPageContext context, TreeShapeEditorView appearance)
{
    private SceneSession scene => context.Scene;
    private void ApplyScenario(GameWorld world, string text) => context.ApplyScenario(world, text);
    private void ApplyProject(GameWorld world, bool publish) => context.ApplyProject(world, publish);
    private bool Try(Action action) => context.Try(action);
    private string message { set => context.SetMessage(value); }
    private ref int selectedSpecies => ref context.Ui.Species.Selected;
    private ref int speciesTab => ref context.Ui.Species.Tab;
    internal void Draw(GameWorld world)
    {
        EditorStyle.BeginPanel("species-list", new Vec2(ImGui.GetFontSize() * 15.5f, 0));
        EditorStyle.Section("Fafajok");
        selectedSpecies = Math.Clamp(selectedSpecies, 0, scene.World.Species.Count - 1);
        for (int i = 0; i < scene.World.Species.Count; i++)
            if (ColourSwatch(scene.World.Species[i].Name, SpeciesColour(scene.World.Species[i].Id), i == selectedSpecies, "sp" + i)) selectedSpecies = i;
        ImGui.Spacing();
        if (ImGui.Button("Alapértékek visszaállítása", new Vec2(-1, 0))) {
            var builtin = GameWorld.DefaultScenario().Species.Find(x => x.Id == scene.World.Species[selectedSpecies].Id);
            if (builtin != null) { scene.EditWorld(scene => scene.Species[selectedSpecies] = builtin); if (Try(() => ApplyProject(world, false))) message = builtin.Name + ": a beépített értékek."; }
        }
        ImGui.TextWrapped("Új fafajhoz famodell is kell; ezek a meglévő fajok paraméterei.");
        ImGui.EndChild(); ImGui.SameLine();

        var sp = scene.World.Species[selectedSpecies];
        EditorStyle.BeginPanel("species-form", new Vec2(0, 0));
        EditorStyle.Title(sp.Name);
        int count = 0, tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int t = 0; t < tiles; t++) if (world.TileSpecies(t).ToString() == sp.Id) count++;
        EditorStyle.Text(HudTheme.Muted, $"A tesztvilágban {count} csempén áll ({100.0 * count / Math.Max(1, tiles):0}%) · azonosító: {sp.Id}");
        ImGui.Spacing();
        EditorStyle.Tabs("species-tab", new[] { "Élettan és gazdaság", "Megjelenés", "Színek és kéreg" }, ref speciesTab);
        if (speciesTab == 1) { appearance.DrawShape(world, sp); ImGui.EndChild(); return; }
        if (speciesTab == 2) { appearance.DrawColours(world, sp); ImGui.EndChild(); return; }
        bool changed = ParameterTable("species-fields", Row => {
            Row("Név", "Megjelenik a játékban és a listákban.", () => { string n = sp.Name; if (ImGui.InputText("##name", ref n, 80) && n.Trim().Length > 0) scene.Edit(sp, edited => edited.Name = n.Trim()); });
            Row("Leírás", "A játék fafajleírása (súgó és erdőnézet).", () => { string d = sp.Description ?? ""; if (ImGui.InputTextMultiline("##description", ref d, 600, new Vec2(-1, ImGui.GetTextLineHeight() * 3.4f))) scene.Edit(sp, edited => edited.Description = d.Trim()); });
            Row("Faanyagár (eFt/m³)", "Gazdaság: ennyit ér a kitermelt fa a malomban (a piaci árszorzóval).", () => Number("##price", () => sp.TimberPrice, v => scene.Edit(sp, edited => edited.TimberPrice = v), 1, "%.1f", 0, 10000));
            Row("Éves növekedési ráta", "Milyen gyorsan gyarapszik a fatömeg kedvező termőhelyen.", () => Number("##growth", () => sp.AnnualGrowthRate, v => scene.Edit(sp, edited => edited.AnnualGrowthRate = v), .005, "%.3f", .001, 1));
            Row("Legnagyobb fatömeg", "Ennyi fatömeget érhet el egy teljes állomány (relatív egység).", () => Number("##biomass", () => sp.MaximumBiomass, v => scene.Edit(sp, edited => edited.MaximumBiomass = v), .05, "%.2f", .01, 10));
            Row("Érett kor (év)", "Ekkortól termel magot és ad kitermelhető fát.", () => Number("##mature", () => sp.MatureAgeYears, v => scene.Edit(sp, edited => edited.MatureAgeYears = v), 1, "%.0f", 1, 1999));
            Row("Legnagyobb kor (év)", "Elöregedés és elhalás.", () => Number("##maxage", () => sp.MaximumAgeYears, v => scene.Edit(sp, edited => edited.MaximumAgeYears = v), 5, "%.0f", 2, 2000));
            Row("Kedvelt nedvesség (0–1)", "Termőhelyi alkalmasság: a talaj nedvessége szerint.", () => Unit("##pm", () => sp.PreferredMoisture, v => scene.Edit(sp, edited => edited.PreferredMoisture = v)));
            Row("Nedvességtűrés (0–1)", "Mennyire tér el a kedvelttől, amit még elvisel.", () => Unit("##mt", () => sp.MoistureTolerance, v => scene.Edit(sp, edited => edited.MoistureTolerance = Math.Max(.01, v))));
            Row("Kedvelt magasság (0–1)", "Termőhelyi alkalmasság: síkság vagy hegyvidék.", () => Unit("##pe", () => sp.PreferredElevation, v => scene.Edit(sp, edited => edited.PreferredElevation = v)));
            Row("Magasságtűrés (0–1)", "Mennyire tér el a kedvelttől, amit még elvisel.", () => Unit("##et", () => sp.ElevationTolerance, v => scene.Edit(sp, edited => edited.ElevationTolerance = Math.Max(.01, v))));
            Row("Árnyéktűrés (0–1)", "Fény- és koronatér-versengés: 0 fényigényes úttörő, 1 zárt lomb alatt is újul.", () => Unit("##shade", () => sp.ShadeTolerance, v => scene.Edit(sp, edited => edited.ShadeTolerance = v)));
            Row("Szárazságérzékenység", "A vízválasz kitevője: kisebb érték, jobb aszálytűrés.", () => Number("##dry", () => sp.DrySensitivity, v => scene.Edit(sp, edited => edited.DrySensitivity = v), .05, "%.2f", .01, 5));
            Row("Érett magasság (m)", "A kifejlett fa jellemző magassága.", () => Number("##mh", () => sp.MatureHeight, v => scene.Edit(sp, edited => edited.MatureHeight = v), 1, "%.1f", .5, 100));
            Row("Legnagyobb magasság (m)", "Eddig nőhet; a növekedés itt áll meg.", () => Number("##xh", () => sp.MaxHeight, v => scene.Edit(sp, edited => edited.MaxHeight = v), 1, "%.1f", .5, 100));
            Row("Egyedszám csempénként", "Ennyi fa fér el egy teljes állományban.", () => { int v = sp.Capacity; if (ImGui.InputInt("##cap", ref v)) scene.Edit(sp, edited => edited.Capacity = Math.Clamp(v, 1, 64)); });
            Row("Fasűrűség", "Kinyerhető faanyag a fatömegből (lucfenyőhöz viszonyítva).", () => Number("##density", () => sp.WoodDensity, v => scene.Edit(sp, edited => edited.WoodDensity = v), .05, "%.2f", .01, 5));
        });
        if (changed) {
            scene.Edit(sp, edited => edited.MaxHeight = Math.Max(sp.MaxHeight, sp.MatureHeight)); scene.Edit(sp, edited => edited.MaximumAgeYears = Math.Max(sp.MaximumAgeYears, sp.MatureAgeYears + 1));
            if (Try(() => ApplyProject(world, false))) message = sp.Name + " módosítva; a tesztvilág fái már ezzel nőnek.";
        }
        ImGui.Spacing();
        ImGui.TextWrapped("A fafajok paraméterei a futó világra is hatnak: a fák ezután ezekkel nőnek. A kezdő erdő kiosztása új világnál változik.");
        ImGui.EndChild();
    }

}
