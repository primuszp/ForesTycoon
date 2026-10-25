using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal enum WorldTab { Map, Types, Objects, Missions, Rules, Trial }
internal enum MapLayer { Soil, Elevation, Zones, Water, GroundWater, Forest, Tracks, Trafficability }
internal enum TypeTab { Soil, Species, Surfaces, Vehicles, Parameters }

/// <summary>
/// The world pages: the generated and painted map, the types it is painted with (soil, species, surfaces), the vehicles'
/// data and game parameters, and the rain trial. <see cref="WorldTab"/> and <see cref="TypeTab"/> remain the entry points
/// other code uses to open a page.
/// </summary>
internal sealed partial class RuleEditorView
{
    private MapLayer mapLayer;
    private WorldScenario scenario;
    private string appliedScenario;
    private int brushSoil = 1, brushSize = 2, selectedTile = -1, selectedSoilType, seedEdit;
    // Elevation: 0 raise, 1 lower, 2 flatten. Water: 0 lake, 1 land, 2 spring, 3 remove spring. Forest: species index, -1 clears.
    private float springRate = 300;
    private int elevationTool, waterTool, brushSpecies, selectedSpecies;
    private readonly HashSet<int> stroke = new();


    private int trialIntensity = 25, trialSeconds = 60;
    private Task<WorldTrialResult> worldTrial;
    private WorldTrialResult worldTrialResult;

    internal sealed record WorldTrialResult(double[] Baseline, double[] Edited, double[] Water, bool[] Unpaved, string Summary);

    internal void ShowWorldTrial(WorldTrialResult result) { page = EditorPage.Trial; worldTrialResult = result; }
    internal void ShowWorld(WorldTab tab, MapLayer layer = MapLayer.Soil, int tile = -1, string rule = null)
    {
        ShowPage(tab switch {
            WorldTab.Types => EditorPage.Soils, WorldTab.Objects => EditorPage.Objects, WorldTab.Missions => EditorPage.Missions,
            WorldTab.Rules => EditorPage.Rules, WorldTab.Trial => EditorPage.Trial, _ => EditorPage.Map });
        mapLayer = layer; if (tile >= 0) selectedTile = tile;
    }
    /// <summary>A spring on the map, then some simulated time: shows where its water goes.</summary>
    internal void AddSpring(GameWorld world, int tile, double rate, double seconds)
    {
        Scenario(world).Springs[tile] = rate; ApplyProject(world, false); world.ExecutePendingCommands();
        for (int i = 0; i < seconds * 30; i++) world.Update(1.0 / 30);
    }
    internal void ShowWorldTypes(TypeTab tab, int index = 0)
    {
        ShowPage(tab switch {
            TypeTab.Species => EditorPage.Species, TypeTab.Surfaces => EditorPage.Surfaces, TypeTab.Vehicles => EditorPage.Vehicles,
            TypeTab.Parameters => EditorPage.Parameters, _ => EditorPage.Soils });
        selectedSpecies = index;
    }

    private WorldScenario Scenario(GameWorld world)
    {
        if (scenario != null) return scenario;
        scenario = world.RuleProjectDocument.World ?? WorldLoader.CaptureScenario(world);
        // Maps made before species moved here edit the built-in species.
        if (scenario.Species.Count == 0) scenario.Species = GameWorld.DefaultScenario().Species;
        seedEdit = scenario.Seed;
        return scenario;
    }
    private string ScenarioKey() => scenario == null ? new RuleProject().ToJson() : MapPart(new RuleProject { World = scenario }.ToJson());
    /// <summary>The editor's test world is rebuilt from the map whenever the map or a type changes.</summary>
    private void ApplyScenario(GameWorld world, string what)
    {
        try {
            scenario.Validate();
            var project = CollectProject(world); project.Algorithms = null;
            world.RequestScenario(scenario, project); appliedScenario = ScenarioKey(); statusFrame = -1000;
            message = what + " A tesztvilág újraépítése folyamatban.";
        } catch (Exception e) when (e is System.IO.InvalidDataException or ArgumentException or InvalidOperationException) { message = e.Message; }
    }

    // ── Where the work stands: file, test world, game ────────────────────
    private (string Path, DateTime Written, string Key) sceneFile;
    /// <summary>The scene file's content in the same normal form as the edited document; re-read only when the file changes.</summary>
    private string SceneFileKey()
    {
        try {
            if (!System.IO.File.Exists(scenePath)) return null;
            var written = System.IO.File.GetLastWriteTimeUtc(scenePath);
            if (sceneFile.Path != scenePath || sceneFile.Written != written) {
                sceneFile = (scenePath, written, SceneKey(RuleProject.FromJson(System.IO.File.ReadAllText(scenePath))));
            }
            return sceneFile.Key;
        } catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException) { return null; }
    }
    private (bool SceneSaved, bool WorldCurrent) statusCache;
    private int statusFrame = -1000;

    private string SceneDocumentKey(GameWorld world) => SceneKey(CollectProject(world));
    // The saved file and the edited document are compared in the form the game runs: migrated and normalised alike.
    private static string SceneKey(RuleProject project)
    {
        project = project.Clone(); project.Algorithms = null;
        try { project = GameWorld.NormalizeRuleProject(project); } catch (Exception e) when (Expected(e)) { }
        project.Authoring = null;
        return project.ContentRevision();
    }

    private void SaveAll(GameWorld world) => world.AtTickBoundary(() => Try(() => SaveAllAtBoundary(world)));

    private void SaveAllAtBoundary(GameWorld world)
    {
        SaveProjectDraft(world);
        if (scenario == null) return;
        SaveScene(world);
        message = "Mentve: munkapéldány és jelenet (" + System.IO.Path.GetFileName(scenePath) + "). A játék következő indulása ezt futtatja.";
    }

    private void RefreshStatus(GameWorld world)
    {
        // Serializing the whole scene is not free: refresh twice a second.
        if (ImGui.GetFrameCount() - statusFrame > 30) {
            statusFrame = ImGui.GetFrameCount();
            statusCache = (SceneFileKey() is string saved && saved == SceneDocumentKey(world), appliedScenario == ScenarioKey());
        }
    }


    private bool scenarioBuilt;
    // ── Map ──────────────────────────────────────────────────────────────
    private static readonly (MapLayer Layer, string Name, string Help)[] Layers = {
        (MapLayer.Soil, "Talaj", "Talajtípus csempénként. Festhető."),
        (MapLayer.Elevation, "Domborzat", "Magasság. Festhető: emelés, süllyesztés, simítás."),
        (MapLayer.Zones, "Magassági övek", "Tengerpart, síkság, dombvidék, hegyvidék a terep szintje szerint; a meredek csempe sötétebb. Az övek határa itt állítható."),
        (MapLayer.Water, "Víz", "Tavak és folyók. Festhető: a vízszint alá süllyesztett terep megtelik."),
        (MapLayer.GroundWater, "Talajvíz", "Gyökérzóna telítettsége."),
        (MapLayer.Forest, "Erdő", "Fafaj és fatérfogat. Festhető: kezdő erdő fajjal, vagy irtás."),
        (MapLayer.Tracks, "Utak és épületek", "Kezdő utak, fűrészmalmok és telephely. Szerkeszthető: út két kattintással, épület egy kattintással."),
        (MapLayer.Trafficability, "Járhatóság", "A járhatósági szabály eredménye most, burkolatlan csempéken.") };

    private static readonly int[] MapSizes = { 32, 48, 64, 96, 128 };

    private void MapTab(GameWorld world)
    {
        brushSoil = Math.Clamp(brushSoil, 0, scenario.SoilTypes.Count - 1);
        float left = 250, right = 320;
        EditorStyle.BeginPanel("map-left", new Vec2(left, 0));
        EditorStyle.Section("Réteg");
        foreach (var (layer, layerName, help) in Layers) {
            if (ImGui.Selectable(layerName, mapLayer == layer)) mapLayer = layer;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        }
        EditorStyle.Section("Generálás");
        bool captured = scenario.InitialState != null;
        ImGui.TextWrapped(captured ? "Rögzített jelenet: a talaj és a festés szerkeszthető; az eredeti terep és az érintetlen fák megmaradnak. A seed és a méret rögzített."
            : "A pálya seedből generálódik, utána festéssel alakítod.");
        ImGui.BeginDisabled(captured);
        ImGui.SetNextItemWidth(-1); ImGui.InputInt("##seed", ref seedEdit);
        if (ImGui.Button("Új seed")) seedEdit = new Random().Next(1, 999999);
        ImGui.SameLine();
        if (ImGui.Button("Generálás")) { scenario.Seed = seedEdit; ApplyScenario(world, $"Generálva a {seedEdit} seedből; a festés megmaradt."); }
        ImGui.EndDisabled();
        foreach (var (count, label, clear) in new (int, string, Action)[] {
            (scenario.SoilPaint.Count, "festett talajcsempe", () => scenario.SoilPaint.Clear()),
            (scenario.HeightPaint.Count, "festett terepsarok", () => scenario.HeightPaint.Clear()),
            (scenario.ForestPaint.Count, "festett erdőcsempe", () => scenario.ForestPaint.Clear()),
            (scenario.Springs.Count, "forrás", () => scenario.Springs.Clear()) }) {
            if (count == 0) continue;
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{count} {label}"); ImGui.SameLine();
            if (ImGui.SmallButton("törlés##" + label)) { clear(); ApplyScenario(world, label + ": a festés törölve."); }
        }
        if (appliedScenario != ScenarioKey() && ImGui.Button("Tesztvilág építése a pályából")) ApplyScenario(world, "");
        EditorStyle.Section("Méret");
        ImGui.BeginDisabled(captured);
        int sizeIndex = Array.IndexOf(MapSizes, scenario.Size);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##size", ref sizeIndex, MapSizes.Select(n => $"{n} × {n} csempe").ToArray(), MapSizes.Length) && sizeIndex >= 0 && MapSizes[sizeIndex] != scenario.Size) {
            scenario.Size = MapSizes[sizeIndex]; scenario.SoilPaint.Clear(); scenario.HeightPaint.Clear(); scenario.ForestPaint.Clear(); scenario.Springs.Clear();
            ApplyScenario(world, $"A pálya mérete {scenario.Size} × {scenario.Size}; a festés törlődött, mert a csempék újraszámozódtak.");
        }
        ImGui.EndDisabled();
        EditorStyle.Section("Pálya neve");
        string name = scenario.Name; ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##scenario-name", ref name, 80) && name.Trim().Length > 0) scenario.Name = name.Trim();
        ImGui.EndChild(); ImGui.SameLine();

        EditorStyle.BeginPanel("map-canvas", new Vec2(-right - 8, 0), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        int hovered = TileMap(world, "pálya", TileColour(world), painting: mapLayer is MapLayer.Soil or MapLayer.Elevation or MapLayer.Water or MapLayer.Forest);
        if (mapLayer == MapLayer.Tracks) StructureCanvas(world, hovered);
        MapLegend(world);
        ImGui.EndChild(); ImGui.SameLine();

        EditorStyle.BeginPanel("map-right", new Vec2(0, 0));
        if (mapLayer == MapLayer.Soil) {
            EditorStyle.Section("Ecset");
            ImGui.TextWrapped("Bal gombbal húzva festesz; elengedéskor a tesztvilág újraépül.");
            for (int i = 0; i < scenario.SoilTypes.Count; i++) {
                var type = scenario.SoilTypes[i];
                if (SoilSwatch(type, i == brushSoil, "brush" + i)) brushSoil = i;
            }
            ImGui.SetNextItemWidth(-60); ImGui.SliderInt("Méret", ref brushSize, 1, 6);
        }
        else if (mapLayer == MapLayer.Elevation) {
            EditorStyle.Section("Ecset");
            ImGui.TextWrapped("Húzással egy lépcsőt emel vagy süllyeszt; a simítás az átlaghoz igazít. A szomszédos sarkok legfeljebb egy lépcsőnyire térnek el.");
            ImGui.RadioButton("Emelés", ref elevationTool, 0); ImGui.SameLine(); ImGui.RadioButton("Süllyesztés", ref elevationTool, 1); ImGui.SameLine(); ImGui.RadioButton("Simítás", ref elevationTool, 2);
            ImGui.SetNextItemWidth(-60); ImGui.SliderInt("Méret", ref brushSize, 1, 6);
        }
        else if (mapLayer == MapLayer.Zones) ZonePanel(world);
        else if (mapLayer == MapLayer.Water) {
            EditorStyle.Section("Ecset");
            ImGui.TextWrapped("A víz a domborzatból adódik: a zárt mélyedés a pereméig megtelik. A tó ecset a csempéket a vízszintre süllyeszti és partot emel köréjük; a szárazföld a vízszint fölé emel.");
            ImGui.RadioButton("Tó", ref waterTool, 0); ImGui.SameLine(); ImGui.RadioButton("Szárazföld", ref waterTool, 1);
            ImGui.RadioButton("Forrás", ref waterTool, 2); ImGui.SameLine(); ImGui.RadioButton("Forrás törlése", ref waterTool, 3);
            if (waterTool == 2) {
                ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Hozam", ref springRate, 10, 20000, "%.0f mm/óra", ImGuiSliderFlags.Logarithmic);
                EditorStyle.Text(HudTheme.Muted, $"= {springRate / 1000 * world.TileArea:0.#} m³/óra csempénként ({world.TileArea:0} m²)");
                ImGui.TextWrapped("A forrás folyamatosan vizet ad a csempe felszínéhez. A víz a szintkülönbség szerint folyik: lejtőn lefelé, sík terepen szétterül, árokban végigfolyik, felfelé soha; ha a meder nem győzi, kiönt.");
            }
            if (scenario.Springs.Count > 0) EditorStyle.Text(HudTheme.Muted, $"{scenario.Springs.Count} forrás a pályán (fehér pont).");
            ImGui.SetNextItemWidth(-60); ImGui.SliderInt("Méret", ref brushSize, 1, 6);
        }
        else if (mapLayer == MapLayer.Forest) {
            EditorStyle.Section("Ecset");
            ImGui.TextWrapped("Kezdő erdő: a festett csempén a választott faj természetes, vegyes korú állománya nő; az irtás üresen hagyja.");
            if (ImGui.Selectable("Irtás (nincs erdő)", brushSpecies < 0)) brushSpecies = -1;
            for (int i = 0; i < scenario.Species.Count; i++)
                if (ColourSwatch(scenario.Species[i].Name, SpeciesColour(scenario.Species[i].Id), i == brushSpecies, "species" + i)) brushSpecies = i;
            ImGui.SetNextItemWidth(-60); ImGui.SliderInt("Méret", ref brushSize, 1, 6);
        }
        else if (mapLayer == MapLayer.Tracks) StructurePanel(world);
        int shown = hovered >= 0 ? hovered : selectedTile;
        if (shown >= 0) TileSection(world, shown, hovered >= 0 ? "A kurzor alatt" : "Kijelölt csempe");
        else { EditorStyle.Section("Csempe"); ImGui.TextWrapped("Mutass vagy kattints egy csempére: itt látod a rétegeit."); }
        ImGui.EndChild();
    }

    private static bool SoilSwatch(SoilTypeDefinition type, bool active, string id) => ColourSwatch(type.Name, Colour(type.Color), active, id);
    private static bool ColourSwatch(string name, Vec4 colour, bool active, string id)
    {
        Vec2 p = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Selectable("##" + id, active, ImGuiSelectableFlags.None, new Vec2(0, 26));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p + new Vec2(4, 4), p + new Vec2(22, 22), EditorStyle.U(colour), 3);
        draw.AddText(p + new Vec2(30, 5), EditorStyle.TextBright, name);
        return clicked;
    }
    private static Vec4 SpeciesColour(string id)
    {
        if (!Enum.TryParse(id, out ForesTycoon.Ecology.ForestSpecies s) || s == ForesTycoon.Ecology.ForestSpecies.None) return new(.4f, .4f, .4f, 1);
        var c = ForesTycoon.Ecology.ForestSpeciesTraits.For(s).Crown;
        return new(c.R / 255f, c.G / 255f, c.B / 255f, 1);
    }

    private void TileSection(GameWorld world, int tile, string title)
    {
        EditorStyle.Section($"{title} · #{tile}");
        var section = world.DescribeTile(tile);
        if (!EditorStyle.BeginFacts("tile-facts")) return;
        foreach (var (key, label) in new[] {
            ("forest.trees", "Növényzet"), ("forest.volume", "Fatérfogat"), ("forest.health", "Faegészség"),
            ("road.condition", "Út"), ("trail.trafficability", "Járhatóság"), ("trail.wear", "Nyomvályú"),
            ("soil.profile", "Talaj"), ("soil.bearing", "Teherbírás"), ("soil.fertility", "Termékenység"),
            ("water.root", "Gyökérzónavíz"), ("water.deep", "Mélyvíz"), ("water.surface", "Felszíni víz") })
            if (section.TryGetValue(key, out var value)) EditorStyle.Fact(label, value);
        EditorStyle.Fact("Magasság", $"{world.TileLevel(tile)}. szint");
        EditorStyle.Fact("Magassági öv", scenario.ElevationZones[scenario.ZoneOf(world.TileLevel(tile))].Name);
        EditorStyle.Fact("Lejtő", world.TileSlope(tile) switch { TerrainSlope.Flat => "sík", TerrainSlope.Gentle => "enyhe", _ => "meredek" });
        ImGui.EndTable();
        int soil = world.SoilIndex(tile);
        if (soil < scenario.SoilTypes.Count && ImGui.SmallButton("Talajtípus megnyitása")) { selectedSoilType = soil; ShowPage(EditorPage.Soils); }
    }

    /// <summary>The tile grid as a top-down map; returns the hovered tile. Paints soil when asked.</summary>
    private int TileMap(GameWorld world, string id, Func<int, Vec4> colour, bool painting, Vec2? fixedSize = null)
    {
        var (columns, rows) = world.TileGrid;
        Vec2 origin = ImGui.GetCursorScreenPos(), available = fixedSize ?? ImGui.GetContentRegionAvail() - new Vec2(0, 44);
        if (columns == 0 || rows == 0 || available.X < 20 || available.Y < 20) { ImGui.TextUnformatted("Nincs térkép."); return -1; }
        float cell = MathF.Floor(Math.Min(available.X / columns, available.Y / rows));
        Vec2 size = new(cell * columns, cell * rows), at = origin + new Vec2((available.X - size.X) / 2, 4);
        ImGui.SetCursorScreenPos(at);
        ImGui.InvisibleButton("map-" + id, size);
        bool hover = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var paint = painting ? StrokeColour() : default;
        for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++) {
                int tile = c * rows + r;
                Vec2 min = at + new Vec2(c, r) * cell;
                draw.AddRectFilled(min, min + new Vec2(cell, cell), EditorStyle.U(stroke.Contains(tile) && painting ? paint : colour(tile)));
            }
        int over = -1;
        var mouse = ImGui.GetIO().MousePos;
        if (hover) {
            int c = (int)((mouse.X - at.X) / cell), r = (int)((mouse.Y - at.Y) / cell);
            if (c >= 0 && c < columns && r >= 0 && r < rows) over = c * rows + r;
        }
        if (over >= 0) {
            int c = over / rows, r = over % rows, radius = painting ? brushSize - 1 : 0;
            draw.AddRect(at + new Vec2(c - radius, r - radius) * cell, at + new Vec2(c + radius + 1, r + radius + 1) * cell, EditorStyle.Selection, 2, ImDrawFlags.None, 2);
            if (painting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                for (int dc = -radius; dc <= radius; dc++)
                    for (int dr = -radius; dr <= radius; dr++)
                        if (dc * dc + dr * dr <= radius * radius + radius && c + dc >= 0 && c + dc < columns && r + dr >= 0 && r + dr < rows)
                            stroke.Add((c + dc) * rows + r + dr);
            if (!painting && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selectedTile = over;
        }
        if (id == "pálya") mapGeometry = (at, cell, rows);
        if (mapLayer == MapLayer.Water && id == "pálya")
            foreach (int spring in scenario.Springs.Keys.Where(t => t < columns * rows))
                draw.AddCircleFilled(at + new Vec2(spring / rows + .5f, spring % rows + .5f) * cell, Math.Max(2, cell * .25f), EditorStyle.U(1, 1, 1, .95f));
        if (selectedTile >= 0 && selectedTile < columns * rows) {
            int c = selectedTile / rows, r = selectedTile % rows;
            draw.AddRect(at + new Vec2(c, r) * cell, at + new Vec2(c + 1, r + 1) * cell, EditorStyle.U(1, 1, 1, .9f), 0, ImDrawFlags.None, 2);
        }
        if (painting && stroke.Count > 0 && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) { CommitStroke(world); stroke.Clear(); }
        ImGui.SetCursorScreenPos(new Vec2(origin.X, at.Y + size.Y + 8));
        ImGui.Dummy(Vec2.Zero);
        return over;
    }

    private Vec4 StrokeColour() => mapLayer switch {
        MapLayer.Elevation => elevationTool == 0 ? new(0.90f, 0.86f, 0.72f, 1) : elevationTool == 1 ? new(0.20f, 0.28f, 0.22f, 1) : new(0.6f, 0.6f, 0.55f, 1),
        MapLayer.Water => waterTool == 0 ? new(0.25f, 0.50f, 0.90f, 1) : new(0.55f, 0.48f, 0.34f, 1),
        MapLayer.Forest => brushSpecies < 0 ? new(0.30f, 0.26f, 0.20f, 1) : SpeciesColour(scenario.Species[brushSpecies].Id),
        _ => brushSoil < scenario.SoilTypes.Count ? Colour(scenario.SoilTypes[brushSoil].Color) : default
    };

    /// <summary>A finished brush stroke becomes map paint, and the test world is rebuilt from the map.</summary>
    private void CommitStroke(GameWorld world)
    {
        int tiles = stroke.Count;
        switch (mapLayer) {
            case MapLayer.Soil:
                foreach (int tile in stroke) scenario.SoilPaint[tile] = scenario.SoilTypes[brushSoil].Id;
                ApplyScenario(world, $"{tiles} csempe festve: {scenario.SoilTypes[brushSoil].Name}."); return;
            case MapLayer.Forest:
                string species = brushSpecies < 0 ? "" : scenario.Species[brushSpecies].Id;
                foreach (int tile in stroke) scenario.ForestPaint[tile] = species;
                ApplyScenario(world, $"{tiles} csempe: " + (brushSpecies < 0 ? "irtás." : scenario.Species[brushSpecies].Name + " erdő.")); return;
        }
        if (mapLayer == MapLayer.Water && waterTool >= 2) {
            foreach (int tile in stroke) if (waterTool == 2) scenario.Springs[tile] = Math.Round(springRate); else scenario.Springs.Remove(tile);
            Try(() => ApplyProject(world, false));
            message = waterTool == 2 ? $"{tiles} forrás, {springRate:0} mm/óra. Indítsd a szimulációt, és nézd, merre folyik a víz." : $"{tiles} csempéről a forrás törölve.";
            return;
        }
        if (mapLayer == MapLayer.Water) {
            var targets = waterTool == 0 ? world.LakeTargets(stroke, scenario.HeightPaint) : world.LandTargets(stroke);
            foreach (var (node, height) in targets) scenario.HeightPaint[node] = height;
            ApplyScenario(world, waterTool == 0 ? $"Tó: {tiles} csempe, parttal." : $"Szárazföld: {tiles} csempe a vízszint fölé.");
            return;
        }
        // Terrain: the stroke's tile corners get absolute target heights from the current world.
        var corners = stroke.SelectMany(world.Map.TileCorners).Distinct().ToArray();
        int average = corners.Length == 0 ? 0 : (int)Math.Round(corners.Average(world.Map.NodeHeight));
        foreach (int node in corners) {
            int height = world.Map.NodeHeight(node);
            scenario.HeightPaint[node] = elevationTool switch { 0 => Math.Min(world.Map.MaximumHeight, height + 1), 1 => Math.Max(0, height - 1), _ => average };
        }
        ApplyScenario(world, $"Domborzat: {tiles} csempe " + (elevationTool == 0 ? "emelve." : elevationTool == 1 ? "süllyesztve." : "simítva."));
    }

    private Func<int, Vec4> TileColour(GameWorld world) => mapLayer switch {
        MapLayer.Elevation => tile => Mix(new(0.18f, 0.30f, 0.20f, 1), new(0.86f, 0.82f, 0.68f, 1), world.TileElevation(tile)),
        MapLayer.Zones => tile => EditorStyle.Dim(Colour(scenario.ElevationZones[scenario.ZoneOf(world.TileLevel(tile))].Color),
            world.TileSlope(tile) switch { TerrainSlope.Flat => 1f, TerrainSlope.Gentle => .86f, _ => .66f }),
        MapLayer.Water => tile => world.IsWaterTile(tile) ? new(0.25f, 0.50f, 0.90f, 1)
            : Mix(Mix(new(0.22f, 0.24f, 0.20f, 1), new(0.62f, 0.60f, 0.52f, 1), world.TileElevation(tile)), new(0.30f, 0.62f, 0.95f, 1),
                (float)Math.Sqrt(Math.Clamp((world.TileSurfaceWater(tile) - 0.5) / 100, 0, 1))),
        MapLayer.GroundWater => tile => Mix(new(0.30f, 0.25f, 0.17f, 1), new(0.30f, 0.56f, 0.88f, 1), (float)world.TileWater(tile)),
        MapLayer.Forest => tile => world.TileSpecies(tile) is var s && s != ForesTycoon.Ecology.ForestSpecies.None
            ? Mix(new(0.17f, 0.19f, 0.14f, 1), SpeciesColour(s.ToString()), 0.45f + 0.55f * (float)Math.Clamp(world.TileForest(tile) / 30, 0, 1))
            : world.IsWaterTile(tile) ? new(0.20f, 0.32f, 0.50f, 1) : new(0.17f, 0.19f, 0.14f, 1),
        MapLayer.Tracks => tile => world.Map.IsRoadTile(tile) ? new(0.85f, 0.82f, 0.75f, 1) : world.Map.IsBuildingTile(tile) ? new(0.62f, 0.40f, 0.26f, 1) : world.Map.IsSkidTrail(tile) ? new(0.85f, 0.55f, 0.28f, 1)
            : EditorStyle.Dim(SoilColour(world, tile), .45f),
        MapLayer.Trafficability => tile => world.IsUnpaved(tile) ? TrafficColour(world.Trafficability(tile)) : new(0.45f, 0.45f, 0.45f, 1),
        _ => tile => SoilColour(world, tile)
    };
    private Vec4 SoilColour(GameWorld world, int tile)
    {
        int soil = world.SoilIndex(tile);
        var colour = soil < scenario.SoilTypes.Count ? Colour(scenario.SoilTypes[soil].Color) : new Vec4(.5f, .5f, .5f, 1);
        return EditorStyle.Dim(colour, 0.75f + 0.35f * world.TileElevation(tile));
    }
    // Blue = passable, orange = soft: told apart by lightness as well as hue.
    private static Vec4 TrafficColour(double value) => Mix(new(0.92f, 0.52f, 0.18f, 1), new(0.22f, 0.42f, 0.78f, 1), (float)Math.Clamp(value, 0, 1));
    private static Vec4 Mix(Vec4 a, Vec4 b, float t) { t = Math.Clamp(t, 0, 1); return a + (b - a) * t; }
    private static Vec4 Colour(string hex)
    {
        if (hex is { Length: 7 } && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1);
        return new(.5f, .5f, .5f, 1);
    }
    private static string Hex(Vec4 c) => $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";

    /// <summary>Colour swatches with labels in a row that wraps to the next line when the panel is full.</summary>
    private static void Legend(IEnumerable<(Vec4 Colour, string Label)> items)
    {
        var draw = ImGui.GetWindowDrawList();
        float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        bool first = true;
        foreach (var (colour, label) in items) {
            float width = 22 + ImGui.CalcTextSize(label).X;
            if (!first) {
                ImGui.SameLine(0, 18);
                if (ImGui.GetCursorScreenPos().X + width > right) ImGui.NewLine();
            }
            first = false;
            Vec2 p = ImGui.GetCursorScreenPos();
            draw.AddRectFilled(p + new Vec2(0, 3), p + new Vec2(14, 17), EditorStyle.U(colour), 2);
            ImGui.Dummy(new Vec2(18, 20)); ImGui.SameLine(0, 4);
            ImGui.TextUnformatted(label);
        }
    }

    /// <summary>The elevation zones from low to high: name, colour and the highest level each one covers.</summary>
    private void ZonePanel(GameWorld world)
    {
        EditorStyle.Section("Magassági övek");
        ImGui.TextWrapped($"Egy öv a lenti övtől a felső szintjéig tart; a legfelső felfelé nyitott. A terep 0–{world.Map.MaximumHeight}. szintig emelhető, a víz a {world.Map.WaterHeight}. szintig áll.");
        var zones = scenario.ElevationZones;
        int remove = -1;
        for (int i = 0; i < zones.Count; i++) {
            var zone = zones[i];
            ImGui.PushID("zone" + i);
            var c = Colour(zone.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
            if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) zone.Color = Hex(new Vec4(rgb, 1));
            ImGui.SameLine();
            string name = zone.Name; ImGui.SetNextItemWidth(zones.Count > 1 ? -(EditorStyle.ButtonWidth("Törlés") + 8) : -1);
            if (ImGui.InputText("##name", ref name, 40) && name.Trim().Length > 0) zone.Name = name.Trim();
            if (zones.Count > 1) { ImGui.SameLine(); if (EditorStyle.QuietButton("Törlés")) remove = i; }
            int low = i == 0 ? 0 : zones[i - 1].MaxLevel + 1;
            if (i < zones.Count - 1) {
                int top = zone.MaxLevel; ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{low}. szinttől"); ImGui.SameLine(); ImGui.SetNextItemWidth(ImGui.GetFontSize() * 6);
                // The upper bound stays between this zone's floor and the next zone's top, so the order never breaks.
                if (ImGui.InputInt("szintig##top", ref top)) zone.MaxLevel = Math.Clamp(top, low, zones[i + 1].MaxLevel - 1);
            } else { ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{low}. szinttől felfelé"); }
            ImGui.PopID();
            ImGui.Spacing();
        }
        if (remove >= 0) {
            // The removed zone's levels go to the zone above it; removing the top zone opens the one below upwards.
            if (remove == zones.Count - 1) zones[remove - 1].MaxLevel = zones[remove].MaxLevel;
            zones.RemoveAt(remove);
        }
        ImGui.BeginDisabled(zones.Count >= 8 || zones[^1].MaxLevel - (zones.Count > 1 ? zones[^2].MaxLevel : -1) < 2);
        if (ImGui.Button("Új öv a legfelső alá", new Vec2(-1, 0))) {
            int floor = zones.Count > 1 ? zones[^2].MaxLevel + 1 : 0;
            zones.Insert(zones.Count - 1, new ElevationZoneDefinition { Id = "zone" + Guid.NewGuid().ToString("N")[..6], Name = "Új öv", Color = "#9A9A9A", MaxLevel = floor });
        }
        ImGui.EndDisabled();
        if (ImGui.Button("Alapértelmezett övek", new Vec2(-1, 0))) scenario.ElevationZones = ElevationZoneDefinition.Defaults();
    }

    private void MapLegend(GameWorld world)
    {
        var draw = ImGui.GetWindowDrawList();
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        string Share(int count) => $"{100.0 * count / Math.Max(1, tiles):0}%";
        if (mapLayer == MapLayer.Forest) {
            var counts = Enumerable.Range(0, tiles).Select(world.TileSpecies).Where(s => s != ForesTycoon.Ecology.ForestSpecies.None)
                .GroupBy(s => s).OrderByDescending(g => g.Count()).Take(6).ToArray();
            Legend(counts.Select(g => (SpeciesColour(g.Key.ToString()),
                $"{scenario.Species.Find(x => x.Id == g.Key.ToString())?.Name ?? g.Key.ToString()} · {Share(g.Count())}")));
            if (counts.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs erdő a pályán.");
            return;
        }
        if (mapLayer is MapLayer.Soil or MapLayer.Tracks) {
            var counts = new int[scenario.SoilTypes.Count];
            for (int t = 0; t < tiles; t++) { int s = world.SoilIndex(t); if (s < counts.Length) counts[s]++; }
            Legend(scenario.SoilTypes.Select((type, i) => (Colour(type.Color), $"{type.Name} · {Share(counts[i])}")));
            if (mapLayer == MapLayer.Tracks) { EditorStyle.Text(HudTheme.Muted, "világos: út · barna: épület · narancs: közelítőnyom"); }
            return;
        }
        if (mapLayer == MapLayer.Zones) {
            var counts = new int[scenario.ElevationZones.Count];
            for (int t = 0; t < tiles; t++) counts[scenario.ZoneOf(world.TileLevel(t))]++;
            Legend(scenario.ElevationZones.Select((zone, i) => (Colour(zone.Color), $"{zone.Name} · {Share(counts[i])}")));
            EditorStyle.Text(HudTheme.Muted, "sötétebb: enyhe vagy meredek lejtő");
            return;
        }
        (string Low, string High, Vec4 A, Vec4 B) = mapLayer switch {
            MapLayer.Elevation => ("alacsony", "magas", new Vec4(0.18f, 0.30f, 0.20f, 1), new Vec4(0.86f, 0.82f, 0.68f, 1)),
            MapLayer.GroundWater => ("száraz", "telített", new Vec4(0.30f, 0.25f, 0.17f, 1), new Vec4(0.30f, 0.56f, 0.88f, 1)),
            MapLayer.Water => ("száraz", "100+ mm felszíni víz · sötétkék: tó", new Vec4(0.42f, 0.42f, 0.36f, 1), new Vec4(0.30f, 0.62f, 0.95f, 1)),
            _ => ("nehezen járható (0)", "jól járható (1)", TrafficColour(0), TrafficColour(1))
        };
        ImGui.TextUnformatted(Low); ImGui.SameLine();
        Vec2 at = ImGui.GetCursorScreenPos() + new Vec2(0, 3);
        draw.AddRectFilledMultiColor(at, at + new Vec2(200, 14), EditorStyle.U(A), EditorStyle.U(B), EditorStyle.U(B), EditorStyle.U(A));
        ImGui.Dummy(new Vec2(204, 20)); ImGui.SameLine(); ImGui.TextUnformatted(High);
        if (mapLayer == MapLayer.Trafficability) { ImGui.SameLine(0, 18); EditorStyle.Text(HudTheme.Muted, "szürke: burkolt út"); }
    }

    // ── Types ────────────────────────────────────────────────────────────
    /// <summary>Rules whose numbers are a vehicle's technical data: they get the Vehicles tab, not the general parameters.</summary>
    private static bool IsVehicleRule(string rule) =>
        rule.StartsWith("vehicle.", StringComparison.Ordinal) || rule.StartsWith("machine.", StringComparison.Ordinal) || rule.StartsWith("upkeep.", StringComparison.Ordinal);

    private void VehiclesTab(GameWorld world)
    {
        EditorStyle.BeginPanel("vehicles", Vec2.Zero);
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "Tömeg, teljesítmény, vonóerő, menetellenállás, fogyasztás, kapacitás, sebesség és karbantartás. "
            + "A szabályok járművenként is olvassák őket (Rönkszállító, Erdészeti gép objektum).");
        ImGui.PopTextWrapPos();
        currentRules.DrawTuning(world, IsVehicleRule);
        if (currentRules.UnappliedTuning(world) > 0 && EditorStyle.PrimaryButton("Járműadatok alkalmazása")) Try(() => ApplyProject(world, false));
        EditorStyle.EndPanel();
    }

    private void ParametersTab(GameWorld world)
    {
        EditorStyle.BeginPanel("parameters", Vec2.Zero);
        currentRules.DrawTuning(world, rule => !IsVehicleRule(rule));
        if (currentRules.UnappliedTuning(world) > 0 && EditorStyle.PrimaryButton("Paraméterek alkalmazása")) Try(() => ApplyProject(world, false));
        EditorStyle.EndPanel();
    }

    private void SoilTypesTab(GameWorld world)
    {
        EditorStyle.BeginPanel("types-list", new Vec2(ImGui.GetFontSize() * 15.5f, 0));
        EditorStyle.Section("Talajtípusok");
        selectedSoilType = Math.Clamp(selectedSoilType, 0, scenario.SoilTypes.Count - 1);
        for (int i = 0; i < scenario.SoilTypes.Count; i++)
            if (SoilSwatch(scenario.SoilTypes[i], i == selectedSoilType, "type" + i)) selectedSoilType = i;
        ImGui.Spacing();
        if (ImGui.Button("Új talajtípus", new Vec2(-1, 0))) {
            var copy = RuleProject.FromJson(new RuleProject { World = new WorldScenario { SoilTypes = { scenario.SoilTypes[selectedSoilType] } } }.ToJson()).World.SoilTypes[0];
            copy.Id = "soil" + Guid.NewGuid().ToString("N")[..6]; copy.Name = scenario.SoilTypes[selectedSoilType].Name + " (másolat)";
            scenario.SoilTypes.Add(copy); selectedSoilType = scenario.SoilTypes.Count - 1;
            message = "Új talajtípus. Festéssel kerül a térképre.";
        }
        var type = scenario.SoilTypes[selectedSoilType];
        bool used = scenario.SoilPaint.ContainsValue(type.Id) || (scenario.InitialState != null &&
            Enumerable.Range(0, world.Map.Tiles.Count).Any(t => world.Soils.Profile(t).Id == type.Id));
        ImGui.BeginDisabled(scenario.SoilTypes.Count == 1 || used);
        if (ImGui.Button("Törlés", new Vec2(-1, 0))) { scenario.SoilTypes.RemoveAt(selectedSoilType); selectedSoilType = 0; ApplyScenario(world, "Talajtípus törölve."); }
        ImGui.EndDisabled();
        if (used) ImGui.TextWrapped("Használatban van a térképen, ezért nem törölhető.");
        EditorStyle.EndPanel(); ImGui.SameLine();

        EditorStyle.BeginPanel("type-form", Vec2.Zero);
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
            Row("Név", "Megjelenik a térképen és a játékban.", () => { string n = type.Name; if (ImGui.InputText("##name", ref n, 80) && n.Trim().Length > 0) type.Name = n.Trim(); Done(); });
            Row("Szín", "A térkép színe.", () => { var c = Colour(type.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
                if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) type.Color = Hex(new Vec4(rgb, 1)); Done(); });
            Row("Teherbírás (0–1)", "Földút és közelítőnyom járhatósága átázva: talaj.teherbírás a járhatósági szabályban.", () => { float v = (float)type.BearingCapacity;
                if (ImGui.SliderFloat("##bearing", ref v, 0, 1, "%.2f")) type.BearingCapacity = Math.Round(v, 3); Done(); });
            Row("Termékenység (0–1)", "Termőhelyi alkalmasság: a fák növekedése és egészsége.", () => { float v = (float)type.Fertility;
                if (ImGui.SliderFloat("##fertility", ref v, 0, 1, "%.2f")) type.Fertility = Math.Round(v, 3); Done(); });
            Row("Telítettség (mm)", "A talaj teljes víztartása; e fölött pangó víz.", () => { double v = type.Saturation; if (ImGui.InputDouble("##sat", ref v, 5, 20, "%.0f")) type.Saturation = v; Done(); });
            Row("Szabadföldi vízkapacitás (mm)", "E fölött a víz elszivárog a mélybe.", () => { double v = type.FieldCapacity; if (ImGui.InputDouble("##fc", ref v, 5, 20, "%.0f")) type.FieldCapacity = v; Done(); });
            Row("Hervadáspont (mm)", "Ez alatt a fák nem jutnak vízhez: aszály.", () => { double v = type.WiltingPoint; if (ImGui.InputDouble("##wp", ref v, 1, 10, "%.0f")) type.WiltingPoint = v; Done(); });
            Row("Beszivárgás (mm/óra)", "Milyen gyorsan nyeli el a csapadékot; a többi lefolyik.", () => { double v = type.InfiltrationPerHour; if (ImGui.InputDouble("##inf", ref v, 1, 5, "%.1f")) type.InfiltrationPerHour = v; Done(); });
            Row("Drénezés (mm/óra)", "Milyen gyorsan szivárog a többletvíz a mélybe.", () => { double v = type.DrainagePerHour; if (ImGui.InputDouble("##dr", ref v, .1, 1, "%.2f")) type.DrainagePerHour = v; Done(); });
            Row("Generálás: nedvesség (0–1)", "Hová teszi a generátor: nedvesebb vagy szárazabb helyekre.", () => { float v = (float)type.PreferredMoisture;
                if (ImGui.SliderFloat("##pm", ref v, 0, 1, "%.2f")) type.PreferredMoisture = Math.Round(v, 3); Done(); });
            Row("Generálás: textúra (0–1)", "Durva (homok) vagy finom (agyag) foltokba kerül.", () => { float v = (float)type.PreferredTexture;
                if (ImGui.SliderFloat("##pt", ref v, 0, 1, "%.2f")) type.PreferredTexture = Math.Round(v, 3); Done(); });
            ImGui.EndTable();
        }
        if (changed) ApplyScenario(world, $"{type.Name} módosítva.");
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "A típusok új világ létrehozásakor kerülnek a játékba; a futó játék világa a régi talajjal fut tovább. A szerkesztőben a tesztvilág minden módosításkor újraépül.");
        if (scenario.InitialState != null) EditorStyle.Text(HudTheme.Muted, "A meglévő talajfoltok a helyükön maradnak; kisebb telítettségnél a többletvíz a felszínre kerül.");
        ImGui.PopTextWrapPos();
        EditorStyle.EndPanel();
    }

    /// <summary>A three-column parameter table: name, value, where it acts. Returns true when an edit was finished.</summary>
    private static bool ParameterTable(string id, Action<Action<string, string, Action>> rows)
    {
        bool changed = false;
        if (!ImGui.BeginTable(id, 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) return false;
        ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthFixed, 230);
        ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthFixed, 240);
        ImGui.TableSetupColumn("Hol hat");
        rows((label, effect, field) => {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
            ImGui.TableSetColumnIndex(1); ImGui.SetNextItemWidth(-1); field();
            if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;
            ImGui.TableSetColumnIndex(2); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, effect); ImGui.PopTextWrapPos();
        });
        ImGui.EndTable();
        return changed;
    }
    private static void Number(string id, Func<double> get, Action<double> set, double step, string format, double min = 0, double max = 1e9)
    {
        double v = get(); if (ImGui.InputDouble(id, ref v, step, step * 10, format)) set(Math.Clamp(v, min, max));
    }
    private static void Unit(string id, Func<double> get, Action<double> set)
    {
        float v = (float)get(); if (ImGui.SliderFloat(id, ref v, 0, 1, "%.2f")) set(Math.Round(v, 3));
    }

    private void SpeciesTab(GameWorld world)
    {
        EditorStyle.BeginPanel("species-list", new Vec2(ImGui.GetFontSize() * 15.5f, 0));
        EditorStyle.Section("Fafajok");
        selectedSpecies = Math.Clamp(selectedSpecies, 0, scenario.Species.Count - 1);
        for (int i = 0; i < scenario.Species.Count; i++)
            if (ColourSwatch(scenario.Species[i].Name, SpeciesColour(scenario.Species[i].Id), i == selectedSpecies, "sp" + i)) selectedSpecies = i;
        ImGui.Spacing();
        if (ImGui.Button("Alapértékek visszaállítása", new Vec2(-1, 0))) {
            var builtin = GameWorld.DefaultScenario().Species.Find(x => x.Id == scenario.Species[selectedSpecies].Id);
            if (builtin != null) { scenario.Species[selectedSpecies] = builtin; Try(() => ApplyProject(world, false)); message = builtin.Name + ": a beépített értékek."; }
        }
        ImGui.TextWrapped("Új fafajhoz famodell is kell; ezek a meglévő fajok paraméterei.");
        ImGui.EndChild(); ImGui.SameLine();

        var sp = scenario.Species[selectedSpecies];
        EditorStyle.BeginPanel("species-form", new Vec2(0, 0));
        EditorStyle.Title(sp.Name);
        int count = 0, tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int t = 0; t < tiles; t++) if (world.TileSpecies(t).ToString() == sp.Id) count++;
        EditorStyle.Text(HudTheme.Muted, $"A tesztvilágban {count} csempén áll ({100.0 * count / Math.Max(1, tiles):0}%) · azonosító: {sp.Id}");
        ImGui.Spacing();
        EditorStyle.Tabs("species-tab", new[] { "Élettan és gazdaság", "Megjelenés", "Színek és kéreg" }, ref speciesTab);
        if (speciesTab == 1) { TreeShapeArea(world, sp); ImGui.EndChild(); return; }
        if (speciesTab == 2) { TreeColoursArea(world, sp); ImGui.EndChild(); return; }
        bool changed = ParameterTable("species-fields", Row => {
            Row("Név", "Megjelenik a játékban és a listákban.", () => { string n = sp.Name; if (ImGui.InputText("##name", ref n, 80) && n.Trim().Length > 0) sp.Name = n.Trim(); });
            Row("Leírás", "A játék fafajleírása (súgó és erdőnézet).", () => { string d = sp.Description ?? ""; if (ImGui.InputTextMultiline("##description", ref d, 600, new Vec2(-1, ImGui.GetTextLineHeight() * 3.4f))) sp.Description = d.Trim(); });
            Row("Faanyagár (eFt/m³)", "Gazdaság: ennyit ér a kitermelt fa a malomban (a piaci árszorzóval).", () => Number("##price", () => sp.TimberPrice, v => sp.TimberPrice = v, 1, "%.1f", 0, 10000));
            Row("Éves növekedési ráta", "Milyen gyorsan gyarapszik a fatömeg kedvező termőhelyen.", () => Number("##growth", () => sp.AnnualGrowthRate, v => sp.AnnualGrowthRate = v, .005, "%.3f", .001, 1));
            Row("Legnagyobb fatömeg", "Ennyi fatömeget érhet el egy teljes állomány (relatív egység).", () => Number("##biomass", () => sp.MaximumBiomass, v => sp.MaximumBiomass = v, .05, "%.2f", .01, 10));
            Row("Érett kor (év)", "Ekkortól termel magot és ad kitermelhető fát.", () => Number("##mature", () => sp.MatureAgeYears, v => sp.MatureAgeYears = v, 1, "%.0f", 1, 1999));
            Row("Legnagyobb kor (év)", "Elöregedés és elhalás.", () => Number("##maxage", () => sp.MaximumAgeYears, v => sp.MaximumAgeYears = v, 5, "%.0f", 2, 2000));
            Row("Kedvelt nedvesség (0–1)", "Termőhelyi alkalmasság: a talaj nedvessége szerint.", () => Unit("##pm", () => sp.PreferredMoisture, v => sp.PreferredMoisture = v));
            Row("Nedvességtűrés (0–1)", "Mennyire tér el a kedvelttől, amit még elvisel.", () => Unit("##mt", () => sp.MoistureTolerance, v => sp.MoistureTolerance = Math.Max(.01, v)));
            Row("Kedvelt magasság (0–1)", "Termőhelyi alkalmasság: síkság vagy hegyvidék.", () => Unit("##pe", () => sp.PreferredElevation, v => sp.PreferredElevation = v));
            Row("Magasságtűrés (0–1)", "Mennyire tér el a kedvelttől, amit még elvisel.", () => Unit("##et", () => sp.ElevationTolerance, v => sp.ElevationTolerance = Math.Max(.01, v)));
            Row("Árnyéktűrés (0–1)", "Fény- és koronatér-versengés: 0 fényigényes úttörő, 1 zárt lomb alatt is újul.", () => Unit("##shade", () => sp.ShadeTolerance, v => sp.ShadeTolerance = v));
            Row("Szárazságérzékenység", "A vízválasz kitevője: kisebb érték, jobb aszálytűrés.", () => Number("##dry", () => sp.DrySensitivity, v => sp.DrySensitivity = v, .05, "%.2f", .01, 5));
            Row("Érett magasság (m)", "A kifejlett fa jellemző magassága.", () => Number("##mh", () => sp.MatureHeight, v => sp.MatureHeight = v, 1, "%.1f", .5, 100));
            Row("Legnagyobb magasság (m)", "Eddig nőhet; a növekedés itt áll meg.", () => Number("##xh", () => sp.MaxHeight, v => sp.MaxHeight = v, 1, "%.1f", .5, 100));
            Row("Egyedszám csempénként", "Ennyi fa fér el egy teljes állományban.", () => { int v = sp.Capacity; if (ImGui.InputInt("##cap", ref v)) sp.Capacity = Math.Clamp(v, 1, 64); });
            Row("Fasűrűség", "Kinyerhető faanyag a fatömegből (lucfenyőhöz viszonyítva).", () => Number("##density", () => sp.WoodDensity, v => sp.WoodDensity = v, .05, "%.2f", .01, 5));
        });
        if (changed) {
            sp.MaxHeight = Math.Max(sp.MaxHeight, sp.MatureHeight); sp.MaximumAgeYears = Math.Max(sp.MaximumAgeYears, sp.MatureAgeYears + 1);
            Try(() => ApplyProject(world, false)); message = sp.Name + " módosítva; a tesztvilág fái már ezzel nőnek.";
        }
        ImGui.Spacing();
        ImGui.TextWrapped("A fafajok paraméterei a futó világra is hatnak: a fák ezután ezekkel nőnek. A kezdő erdő kiosztása új világnál változik.");
        ImGui.EndChild();
    }

    private static readonly (string Name, string Help)[] SurfaceNames = {
        ("Aszfalt", "Burkolt út: drága, alig kopik és romlik."), ("Makadám", "Zúzottkő út: olcsóbb, esőben gyorsan romlik."),
        ("Földút", "Burkolatlan terep: a járhatóságát a talaj és a talajvíz adja."), ("Közelítőnyom", "Ideiglenes nyom a gépeknek; a forgalom vájja, idővel benő.") };
    private static readonly (string Label, string Effect, string[] Keys)[] SurfaceRows = {
        ("Építési költség (eFt/csempe)", "Gazdaság: kiadás építéskor.", new[] { "AsphaltBuild", "MacadamBuild", null, "TrailCost" }),
        ("Forgalmi kopás szorzója", "Útkopás: mennyit veszít az állapotából áthaladásonként (nyomnál az útkopás többszöröse).", new[] { "AsphaltFactor", "MacadamFactor", null, "TrailLoadMultiplier" }),
        ("Romlás szárazon (állapot/év)", "Időjárási útromlás csapadék nélkül.", new[] { "AsphaltWeather", "MacadamWeather", null, null }),
        ("Romlás esőben (állapot/év/eső)", "Időjárási útromlás az eső erősségével.", new[] { "AsphaltRainWeather", "MacadamRainWeather", null, null }),
        ("Gördülési ellenállás (Crr)", "Jármű: menetellenállás és fogyasztás. A földút és a nyom közös értéke.", new[] { "RollingAsphalt", "RollingGravel", "RollingDirt", "RollingDirt" }),
        ("Javítás az építési ár arányában", "Gazdaság: útjavítás költsége teljes kárnál (aszfalt és makadám közös).", new[] { "RoadRepairShare", "RoadRepairShare", null, null }),
        ("Benövési idő (erdőév)", "A használatlan nyom ennyi idő alatt nő be.", new[] { null, null, null, "SkidTrailOvergrowYears" }) };

    private void SurfacesTab(GameWorld world)
    {
        EditorStyle.BeginPanel("surfaces", new Vec2(0, 0));
        EditorStyle.Title("Burkolatok");
        ImGui.TextWrapped("Az utak és nyomok paraméterei. A futó játékra is azonnal hatnak az alkalmazás után. A földút járhatóságát a Szabályok › Földút járhatósága képlet adja a talaj és a talajvíz alapján.");
        bool changed = false;
        if (ImGui.BeginTable("surface-table", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV)) {
            ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthFixed, 230);
            foreach (var (name, _) in SurfaceNames) ImGui.TableSetupColumn(name, ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Hol hat");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); EditorStyle.Text(HudTheme.Muted, "");
            for (int i = 0; i < SurfaceNames.Length; i++) {
                ImGui.TableSetColumnIndex(i + 1); EditorStyle.Title(SurfaceNames[i].Name);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(SurfaceNames[i].Help);
            }
            foreach (var (label, effect, keys) in SurfaceRows) {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
                for (int i = 0; i < keys.Length; i++) {
                    ImGui.TableSetColumnIndex(i + 1);
                    if (keys[i] == null) { ImGui.AlignTextToFramePadding(); ImGui.TextDisabled("—"); }
                    else if (currentRules.TuningField(keys[i], label + i)) changed = true;
                }
                ImGui.TableSetColumnIndex(5); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, effect); ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }
        if (changed) { Try(() => ApplyProject(world, false)); message = "Burkolat módosítva; a tesztvilágban már ez érvényes."; }
        ImGui.EndChild();
    }

    // ── Trial ────────────────────────────────────────────────────────────
    private void TrialTab(GameWorld world)
    {
        EditorStyle.BeginPanel("trial-settings", new Vec2(300, 0));
        EditorStyle.Section("Esős próba");
        ImGui.TextWrapped("Két világ épül ugyanebből a pályából: az egyik a játék alapszabályaival, a másik a te szabályaiddal. Mindkettőre ugyanannyi eső esik.");
        ImGui.SetNextItemWidth(-90); ImGui.SliderInt("Eső", ref trialIntensity, 0, 40);
        if (scenario.Springs.Count > 0) EditorStyle.Text(HudTheme.Muted, $"A pálya {scenario.Springs.Count} forrása is folyik. 0 eső: csak a források.");
        ImGui.SetNextItemWidth(-90); ImGui.SliderInt("Időtartam (s)", ref trialSeconds, 10, 180);
        ImGui.TextWrapped($"{trialSeconds} játékmásodperc, kb. {trialSeconds / 900.0 * 12:0.#} erdőhónap.");
        bool busy = worldTrial is { IsCompleted: false };
        if (EditorStyle.PrimaryButton(busy ? "Fut…" : "Próba indítása", !busy)) {
            var project = CollectProject(world); project.Algorithms = null; project.World = RuleProject.FromJson(new RuleProject { World = scenario }.ToJson()).World;
            var baseline = GameWorld.DefaultRuleProject(); baseline.Algorithms = null; baseline.World = project.World;
            int intensity = trialIntensity, seconds = trialSeconds;
            worldTrial = Task.Run(() => RunWorldTrial(baseline, project, intensity, seconds));
        }
        if (worldTrial is { IsCompleted: true }) {
            if (worldTrial.IsFaulted) message = "A próba hibát jelzett: " + worldTrial.Exception?.GetBaseException().Message;
            else worldTrialResult = worldTrial.Result;
            worldTrial = null;
        }
        if (worldTrialResult != null) { EditorStyle.Section("Eredmény"); ImGui.TextWrapped(worldTrialResult.Summary); }
        ImGui.EndChild(); ImGui.SameLine();

        EditorStyle.BeginPanel("trial-maps", new Vec2(0, 0), ImGuiWindowFlags.NoScrollbar);
        if (worldTrialResult == null) { ImGui.TextWrapped("Indítsd el a próbát: itt jelenik meg a járhatóság az eső végén, az alapszabályokkal és a tieiddel."); ImGui.EndChild(); return; }
        var r = worldTrialResult;
        float width = (ImGui.GetContentRegionAvail().X - 20) / 3;
        var (columns, rows) = world.TileGrid;
        var mapSize = new Vec2(width, Math.Min(width * rows / Math.Max(1, columns), ImGui.GetContentRegionAvail().Y - 80));
        void Panel(string title, Func<int, Vec4> colour, string id) {
            ImGui.BeginGroup(); EditorStyle.Text(HudTheme.Parchment, title);
            TileMap(world, id, colour, false, mapSize); ImGui.EndGroup();
        }
        Panel("Felszíni víz (elöntés) a végén", t => world.IsWaterTile(t) ? new(0.25f, 0.50f, 0.90f, 1)
            : Mix(new(0.30f, 0.27f, 0.20f, 1), new(0.30f, 0.62f, 0.95f, 1), (float)Math.Sqrt(Math.Clamp((r.Water[t] - 0.5) / 100, 0, 1))), "trial-water"); ImGui.SameLine(0, 10);
        Panel("Járhatóság · alapszabály", t => r.Unpaved[t] ? TrafficColour(r.Baseline[t]) : new(0.45f, 0.45f, 0.45f, 1), "trial-base"); ImGui.SameLine(0, 10);
        Panel("Járhatóság · a te szabályaid", t => r.Unpaved[t] ? TrafficColour(r.Edited[t]) : new(0.45f, 0.45f, 0.45f, 1), "trial-edit");
        MapLegendTraffic();
        ImGui.EndChild();
    }

    private static void MapLegendTraffic()
    {
        var draw = ImGui.GetWindowDrawList();
        ImGui.TextUnformatted("nehezen járható (0)"); ImGui.SameLine();
        Vec2 at = ImGui.GetCursorScreenPos() + new Vec2(0, 3);
        draw.AddRectFilledMultiColor(at, at + new Vec2(200, 14), EditorStyle.U(TrafficColour(0)), EditorStyle.U(TrafficColour(1)), EditorStyle.U(TrafficColour(1)), EditorStyle.U(TrafficColour(0)));
        ImGui.Dummy(new Vec2(204, 20)); ImGui.SameLine(); ImGui.TextUnformatted("jól járható (1) · szürke: burkolt út");
    }

    /// <summary>Same map, same rain, two rule sets; trafficability of every unpaved tile at the end.</summary>
    internal static WorldTrialResult RunWorldTrial(RuleProject baseline, RuleProject edited, int intensity, int seconds)
    {
        (double[] Traffic, double[] Water, bool[] Unpaved, double Mean, double Soft) Run(RuleProject project)
        {
            using var world = WorldLoader.CreateProject(project);
            if (intensity > 0) { world.QueueWeather(ForesTycoon.Ecology.WeatherPreset.Rain, intensity, seconds); world.ExecutePendingCommands(); }
            for (int i = 0; i < seconds * 30; i++) world.Update(1.0 / 30);
            var (columns, rows) = world.TileGrid; int n = columns * rows;
            var traffic = new double[n]; var water = new double[n]; var unpaved = new bool[n];
            for (int t = 0; t < n; t++) { unpaved[t] = world.IsUnpaved(t); traffic[t] = world.Trafficability(t); water[t] = world.TileSurfaceWater(t); }
            var open = Enumerable.Range(0, n).Where(t => unpaved[t]).ToArray();
            return (traffic, water, unpaved, open.Average(t => traffic[t]), open.Count(t => traffic[t] < .5) / (double)Math.Max(1, open.Length));
        }
        int Flooded((double[] Traffic, double[] Water, bool[] Unpaved, double Mean, double Soft) r) => r.Water.Count(w => w > 50);
        var a = Run(baseline); var b = Run(edited);
        string summary = $"Átlagos járhatóság: alap {a.Mean:0.00}, tiéd {b.Mean:0.00}\n"
            + $"Nehezen járható (< 0,5) burkolatlan csempe: alap {a.Soft * 100:0}%, tiéd {b.Soft * 100:0}%\n"
            + $"Elöntött csempe (50 mm-nél több felszíni víz): alap {Flooded(a)}, tiéd {Flooded(b)}\n"
            + $"Eső: {intensity}, {seconds} s. A térképek a próba végét mutatják.";
        return new(a.Traffic, b.Traffic, b.Water, b.Unpaved, summary);
    }
}
