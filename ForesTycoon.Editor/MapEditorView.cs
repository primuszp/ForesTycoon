using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed partial class MapEditorView(EditorPageContext context)
{
    private SceneSession scene => context.Scene;
    private void ApplyScenario(GameWorld world, string text) => context.ApplyScenario(world, text);
    private void ApplyProject(GameWorld world, bool publish) => context.ApplyProject(world, publish);
    private bool Try(Action action) => context.Try(action);
    private string message { set => context.SetMessage(value); }
    private ref MapLayer mapLayer => ref context.Ui.Map.Layer;
    private ref int brushSoil => ref context.Ui.Map.BrushSoil;
    private ref int brushSize => ref context.Ui.Map.BrushSize;
    private ref int selectedTile => ref context.Ui.Map.SelectedTile;
    private ref int selectedSoilType => ref context.Ui.Soils.Selected;
    private ref int seedEdit => ref context.Ui.Map.Seed;
    private ref float springRate => ref context.Ui.Map.SpringRate;
    private ref int elevationTool => ref context.Ui.Map.ElevationTool;
    private ref int waterTool => ref context.Ui.Map.WaterTool;
    private ref int brushSpecies => ref context.Ui.Map.BrushSpecies;
    private HashSet<int> stroke => context.Ui.Map.Stroke;
    private ref bool strokeCancelled => ref context.Ui.Map.StrokeCancelled;
    // ── Map ──────────────────────────────────────────────────────────────
    private static readonly (MapLayer Layer, string Name, string Help)[] Layers = {
        (MapLayer.Soil, "Talaj", "Talajtípus csempénként. Festhető."),
        (MapLayer.Elevation, "Domborzat", "Magasság. Festhető: emelés, süllyesztés, simítás."),
        (MapLayer.Zones, "Magassági övek", "Tengerpart, síkság, dombvidék, hegyvidék a terep szintje szerint; a meredek csempe sötétebb. Az övek határa itt állítható."),
        (MapLayer.Biomes, "Biom", "A csempe biomja: az öve és – ha az övben több biom van – a nagy foltokban váltakozó részarányuk szerint. A biomok a Biomok oldalon szerkeszthetők."),
        (MapLayer.Ground, "Felszín", "A talaj felszíne, ahogy a játék rajzolja: a biom száraz, nedves és meredek felszínanyaga a csempe nedvessége és lejtése szerint."),
        (MapLayer.Water, "Víz", "Tavak és folyók. Festhető: a vízszint alá süllyesztett terep megtelik."),
        (MapLayer.GroundWater, "Talajvíz", "Gyökérzóna telítettsége."),
        (MapLayer.Forest, "Erdő", "Fafaj és fatérfogat. Festhető: kezdő erdő fajjal, vagy irtás."),
        (MapLayer.Tracks, "Utak és épületek", "Kezdő utak, fűrészmalmok és telephely. Szerkeszthető: út két kattintással, épület egy kattintással."),
        (MapLayer.Trafficability, "Járhatóság", "A járhatósági szabály eredménye most, burkolatlan csempéken.") };

    private static readonly int[] MapSizes = { 32, 48, 64, 96, 128 };

    internal void Draw(GameWorld world)
    {
        brushSoil = Math.Clamp(brushSoil, 0, scene.World.SoilTypes.Count - 1);
        float left = 250, right = 320;
        EditorStyle.BeginPanel("map-left", new Vec2(left, 0));
        EditorStyle.Section("Réteg");
        foreach (var (layer, layerName, help) in Layers) {
            if (ImGui.Selectable(layerName, mapLayer == layer)) mapLayer = layer;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        }
        EditorStyle.Section("Generálás");
        bool captured = scene.World.InitialState != null;
        ImGui.TextWrapped(captured ? "Rögzített jelenet: a talaj és a festés szerkeszthető; az eredeti terep és az érintetlen fák megmaradnak. A seed és a méret rögzített."
            : "A pálya seedből generálódik, utána festéssel alakítod.");
        ImGui.BeginDisabled(captured);
        ImGui.SetNextItemWidth(-1); ImGui.InputInt("##seed", ref seedEdit);
        if (ImGui.Button("Új seed")) seedEdit = new Random().Next(1, 999999);
        ImGui.SameLine();
        if (ImGui.Button("Generálás")) { scene.EditWorld(scene => scene.Seed = seedEdit); ApplyScenario(world, $"Generálva a {seedEdit} seedből; a festés megmaradt."); }
        ImGui.EndDisabled();
        foreach (var (count, label, clear) in new (int, string, Action<WorldScenario>)[] {
            (scene.World.SoilPaint.Count, "festett talajcsempe", world => world.SoilPaint.Clear()),
            (scene.World.HeightPaint.Count, "festett terepsarok", world => world.HeightPaint.Clear()),
            (scene.World.ForestPaint.Count, "festett erdőcsempe", world => world.ForestPaint.Clear()),
            (scene.World.ForestFelling.Count, "kivágott erdőcsempe", world => world.ForestFelling.Clear()),
            (scene.World.Springs.Count, "forrás", world => world.Springs.Clear()) }) {
            if (count == 0) continue;
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{count} {label}"); ImGui.SameLine();
            if (ImGui.SmallButton("törlés##" + label)) { scene.EditWorld(clear); ApplyScenario(world, label + ": a festés törölve."); }
        }
        if (!context.WorldCurrent() && ImGui.Button("Tesztvilág építése a pályából")) ApplyScenario(world, "");
        EditorStyle.Section("Domborzat magassága");
        ImGui.BeginDisabled(captured);
        int relief = scene.World.MaxHeight; ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##relief", ref relief, WorldScenario.MinimumRelief, WorldScenario.MaximumRelief, $"{relief} szint · {relief * 2} m");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("A legmagasabb terepszint (szintenként 2 m). 6: alföldi, dombos táj; 10–16: dombság és hegyvidék, meredek oldalakkal.\nA magassági övek, ha az alapértékükön állnak, vele együtt igazodnak.");
        if (ImGui.IsItemDeactivatedAfterEdit() && relief != scene.World.MaxHeight) {
            scene.SetRelief(relief);
            ApplyScenario(world, $"A domborzat legfeljebb {relief} szint ({relief * 2} m).");
        }
        ImGui.EndDisabled();
        EditorStyle.Section("Méret");
        ImGui.BeginDisabled(captured);
        int sizeIndex = Array.IndexOf(MapSizes, scene.World.Size);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##size", ref sizeIndex, MapSizes.Select(n => $"{n} × {n} csempe").ToArray(), MapSizes.Length) && sizeIndex >= 0 && MapSizes[sizeIndex] != scene.World.Size) {
            scene.Resize(MapSizes[sizeIndex]);
            ApplyScenario(world, $"A pálya mérete {scene.World.Size} × {scene.World.Size}; a festés törlődött, mert a csempék újraszámozódtak.");
        }
        ImGui.EndDisabled();
        EditorStyle.Section("Pálya neve");
        string name = scene.World.Name; ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##scenario-name", ref name, 80) && name.Trim().Length > 0) scene.EditWorld(scene => scene.Name = name.Trim());
        ImGui.EndChild(); ImGui.SameLine();

        EditorStyle.BeginPanel("map-canvas", new Vec2(-right - 8, 0), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        int hovered = DrawTiles(world, "pálya", TileColour(world), painting: mapLayer is MapLayer.Soil or MapLayer.Elevation or MapLayer.Water or MapLayer.Forest);
        if (mapLayer == MapLayer.Tracks) StructureCanvas(world, hovered);
        MapLegend(world);
        ImGui.EndChild(); ImGui.SameLine();

        EditorStyle.BeginPanel("map-right", new Vec2(0, 0));
        if (mapLayer == MapLayer.Soil) {
            EditorStyle.Section("Ecset");
            ImGui.TextWrapped("Bal gombbal húzva festesz; elengedéskor a tesztvilág újraépül.");
            for (int i = 0; i < scene.World.SoilTypes.Count; i++) {
                var type = scene.World.SoilTypes[i];
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
            if (scene.World.Springs.Count > 0) EditorStyle.Text(HudTheme.Muted, $"{scene.World.Springs.Count} forrás a pályán (fehér pont).");
            ImGui.SetNextItemWidth(-60); ImGui.SliderInt("Méret", ref brushSize, 1, 6);
        }
        else if (mapLayer == MapLayer.Forest) {
            EditorStyle.Section("Ecset");
            ImGui.TextWrapped("Kezdő erdő: a festett csempén a választott faj természetes, vegyes korú állománya nő; az irtás üresen hagyja.");
            if (ImGui.Selectable("Irtás (nincs erdő)", brushSpecies < 0)) brushSpecies = -1;
            for (int i = 0; i < scene.World.Species.Count; i++)
                if (ColourSwatch(scene.World.Species[i].Name, SpeciesColour(scene.World.Species[i].Id), i == brushSpecies, "species" + i)) brushSpecies = i;
            ImGui.SetNextItemWidth(-60); ImGui.SliderInt("Méret", ref brushSize, 1, 6);
        }
        else if (mapLayer == MapLayer.Tracks) StructurePanel(world);
        int shown = hovered >= 0 ? hovered : selectedTile;
        if (shown >= 0) TileSection(world, shown, hovered >= 0 ? "A kurzor alatt" : "Kijelölt csempe");
        else { EditorStyle.Section("Csempe"); ImGui.TextWrapped("Mutass vagy kattints egy csempére: itt látod a rétegeit."); }
        ImGui.EndChild();
    }

    private void TileSection(GameWorld world, int tile, string title)
    {
        EditorStyle.Section($"{title} · #{tile}");
        var section = world.DescribeTile(tile);
        if (!EditorStyle.BeginFacts("tile-facts")) return;
        foreach (var (key, label) in new[] {
            ("forest.groundTimber", "Kivágott faanyag"), ("forest.trees", "Növényzet"), ("forest.volume", "Fatérfogat"), ("forest.health", "Faegészség"),
            ("road.condition", "Út"), ("trail.trafficability", "Járhatóság"), ("trail.wear", "Nyomvályú"),
            ("soil.profile", "Talaj"), ("soil.bearing", "Teherbírás"), ("soil.fertility", "Termékenység"),
            ("water.root", "Gyökérzónavíz"), ("water.deep", "Mélyvíz"), ("water.surface", "Felszíni víz") })
            if (section.TryGetValue(key, out var value)) EditorStyle.Fact(label, value);
        if (world.Map.IsNetworkTile(tile)) {
            EditorStyle.Fact("Elérési puffer", $"{world.RoadBufferMetres(tile):0.#} m");
            EditorStyle.Fact("Elérhető élő faanyag", structureSurvey != null && structureSurvey.TimberByRoad.TryGetValue(tile, out double volume)
                ? $"{volume:F1} m³" : "Számítás…");
        }
        EditorStyle.Fact("Magasság", $"{world.TileLevel(tile)}. szint");
        EditorStyle.Fact("Magassági öv", scene.World.ElevationZones[scene.World.ZoneOf(world.TileLevel(tile))].Name);
        int biome = TileBiome(world, tile);
        EditorStyle.Fact("Biom", biome < 0 ? "nincs" : $"{scene.World.Biomes[biome].Name} (#{biome})");
        EditorStyle.Fact("Lejtő", world.TileSlope(tile) switch { TerrainSlope.Flat => "sík", TerrainSlope.Gentle => "enyhe", _ => "meredek" });
        ImGui.EndTable();
        int soil = world.SoilIndex(tile);
        if (soil < scene.World.SoilTypes.Count && ImGui.SmallButton("Talajtípus megnyitása")) { selectedSoilType = soil; context.Navigate(EditorPage.Soils); }
    }

    /// <summary>The tile grid as a top-down map; returns the hovered tile. Paints soil when asked.</summary>
    internal int DrawTiles(GameWorld world, string id, Func<int, Vec4> colour, bool painting, Vec2? fixedSize = null)
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
            if (painting && !strokeCancelled && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                for (int dc = -radius; dc <= radius; dc++)
                    for (int dr = -radius; dr <= radius; dr++)
                        if (dc * dc + dr * dr <= radius * radius + radius && c + dc >= 0 && c + dc < columns && r + dr >= 0 && r + dr < rows)
                            stroke.Add((c + dc) * rows + r + dr);
            if (!painting && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selectedTile = over;
        }
        if (id == "pálya") mapGeometry = (at, cell, rows);
        if (mapLayer == MapLayer.Water && id == "pálya")
            foreach (int spring in scene.World.Springs.Keys.Where(t => t < columns * rows))
                draw.AddCircleFilled(at + new Vec2(spring / rows + .5f, spring % rows + .5f) * cell, Math.Max(2, cell * .25f), EditorStyle.U(1, 1, 1, .95f));
        if (selectedTile >= 0 && selectedTile < columns * rows) {
            int c = selectedTile / rows, r = selectedTile % rows;
            draw.AddRect(at + new Vec2(c, r) * cell, at + new Vec2(c + 1, r + 1) * cell, EditorStyle.U(1, 1, 1, .9f), 0, ImDrawFlags.None, 2);
        }
        if (painting && ImGui.IsKeyPressed(ImGuiKey.Escape)) CancelStroke();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
            if (painting && stroke.Count > 0) { CommitStroke(world); stroke.Clear(); }
            strokeCancelled = false;
        }
        ImGui.SetCursorScreenPos(new Vec2(origin.X, at.Y + size.Y + 8));
        ImGui.Dummy(Vec2.Zero);
        return over;
    }

    private Vec4 StrokeColour() => mapLayer switch {
        MapLayer.Elevation => elevationTool == 0 ? new(0.90f, 0.86f, 0.72f, 1) : elevationTool == 1 ? new(0.20f, 0.28f, 0.22f, 1) : new(0.6f, 0.6f, 0.55f, 1),
        MapLayer.Water => waterTool == 0 ? new(0.25f, 0.50f, 0.90f, 1) : new(0.55f, 0.48f, 0.34f, 1),
        MapLayer.Forest => brushSpecies < 0 ? new(0.30f, 0.26f, 0.20f, 1) : SpeciesColour(scene.World.Species[brushSpecies].Id),
        _ => brushSoil < scene.World.SoilTypes.Count ? Colour(scene.World.SoilTypes[brushSoil].Color) : default
    };

    /// <summary>A finished brush stroke becomes map paint, and the test world is rebuilt from the map.</summary>
    internal void CancelStroke() { stroke.Clear(); strokeCancelled = true; }

    internal void CommitStroke(GameWorld world)
    {
        int tiles = stroke.Count;
        switch (mapLayer) {
            case MapLayer.Soil:
                scene.PaintSoil(stroke, scene.World.SoilTypes[brushSoil].Id);
                ApplyScenario(world, $"{tiles} csempe festve: {scene.World.SoilTypes[brushSoil].Name}."); return;
            case MapLayer.Forest:
                string species = brushSpecies < 0 ? "" : scene.World.Species[brushSpecies].Id;
                var eligible = species.Length == 0 ? stroke : stroke.Where(tile => !world.Forest.HasPhysicalObjects(tile) && ((IForestHabitat)world.Map).CanPlantForest(tile));
                scene.PaintForest(eligible, species);
                ApplyScenario(world, $"{tiles} csempe: " + (brushSpecies < 0 ? "fakivágás; a faanyag a helyszínen marad." : scene.World.Species[brushSpecies].Name + " erdő.")); return;
        }
        if (mapLayer == MapLayer.Water && waterTool >= 2) {
            scene.PaintSprings(stroke, waterTool == 2 ? springRate : null);
            if (Try(() => ApplyProject(world, false))) message = waterTool == 2 ? $"{tiles} forrás, {springRate:0} mm/óra. Indítsd a szimulációt, és nézd, merre folyik a víz." : $"{tiles} csempéről a forrás törölve.";
            return;
        }
        if (mapLayer == MapLayer.Water) {
            var targets = waterTool == 0 ? world.LakeTargets(stroke, scene.World.HeightPaint) : world.LandTargets(stroke);
            if (!world.Map.CanApplyHeightTargets(targets)) { message = "A terepmódosítás meglévő objektumot érintene."; return; }
            scene.PaintHeights(targets);
            ApplyScenario(world, waterTool == 0 ? $"Tó: {tiles} csempe, parttal." : $"Szárazföld: {tiles} csempe a vízszint fölé.");
            return;
        }
        // Terrain: the stroke's tile corners get absolute target heights from the current world.
        var corners = stroke.SelectMany(world.Map.TileCorners).Distinct().ToArray();
        int average = corners.Length == 0 ? 0 : (int)Math.Round(corners.Average(world.Map.NodeHeight));
        var heightTargets = new Dictionary<int, int>();
        foreach (int node in corners) {
            int height = world.Map.NodeHeight(node);
            heightTargets[node] = elevationTool switch { 0 => Math.Min(world.Map.MaximumHeight, height + 1), 1 => Math.Max(0, height - 1), _ => average };
        }
        if (!world.Map.CanApplyHeightTargets(heightTargets)) { message = "A terepmódosítás meglévő objektumot érintene."; return; }
        scene.PaintHeights(heightTargets);
        ApplyScenario(world, $"Domborzat: {tiles} csempe " + (elevationTool == 0 ? "emelve." : elevationTool == 1 ? "süllyesztve." : "simítva."));
    }

    private Func<int, Vec4> TileColour(GameWorld world) => mapLayer switch {
        MapLayer.Elevation => tile => Mix(new(0.18f, 0.30f, 0.20f, 1), new(0.86f, 0.82f, 0.68f, 1), world.TileElevation(tile)),
        MapLayer.Zones => tile => EditorStyle.Dim(Colour(scene.World.ElevationZones[scene.World.ZoneOf(world.TileLevel(tile))].Color),
            world.TileSlope(tile) switch { TerrainSlope.Flat => 1f, TerrainSlope.Gentle => .86f, _ => .66f }),
        MapLayer.Biomes => tile => BiomeColour(world, tile),
        MapLayer.Ground => tile => { var g = scene.World.Ground(TileBiome(world, tile), world.TileMoisture(tile), (int)world.TileSlope(tile)); return new Vec4(g.R, g.G, g.B, 1); },
        MapLayer.Water => tile => world.IsWaterTile(tile) ? new(0.25f, 0.50f, 0.90f, 1)
            : Mix(Mix(new(0.22f, 0.24f, 0.20f, 1), new(0.62f, 0.60f, 0.52f, 1), world.TileElevation(tile)), new(0.30f, 0.62f, 0.95f, 1),
                (float)Math.Sqrt(Math.Clamp((world.TileSurfaceWater(tile) - 0.5) / 100, 0, 1))),
        MapLayer.GroundWater => tile => Mix(new(0.30f, 0.25f, 0.17f, 1), new(0.30f, 0.56f, 0.88f, 1), (float)world.TileWater(tile)),
        MapLayer.Forest => tile => world.TileSpecies(tile) is var s && s != ForesTycoon.Ecology.ForestSpecies.None
            ? Mix(new(0.17f, 0.19f, 0.14f, 1), SpeciesColour(s.ToString()), 0.45f + 0.55f * (float)Math.Clamp(world.TileForest(tile) / 30, 0, 1))
            : world.IsWaterTile(tile) ? new(0.20f, 0.32f, 0.50f, 1) : new(0.17f, 0.19f, 0.14f, 1),
        MapLayer.Tracks => tile => world.Map.IsRoadTile(tile) ? world.Map.GetRoadPaving(tile) switch {
            RoadPaving.Asphalt => new(0.45f, 0.46f, 0.47f, 1), RoadPaving.Macadam => new(0.85f, 0.82f, 0.75f, 1), _ => new(0.62f, 0.43f, 0.26f, 1) } : world.Map.IsBuildingTile(tile) ? new(0.62f, 0.40f, 0.26f, 1) : world.Map.IsSkidTrail(tile) ? new(0.85f, 0.55f, 0.28f, 1)
            : EditorStyle.Dim(SoilColour(world, tile), .45f),
        MapLayer.Trafficability => tile => world.IsUnpaved(tile) ? TrafficColour(world.Trafficability(tile)) : new(0.45f, 0.45f, 0.45f, 1),
        _ => tile => SoilColour(world, tile)
    };
    private Vec4 SoilColour(GameWorld world, int tile)
    {
        int soil = world.SoilIndex(tile);
        var colour = soil < scene.World.SoilTypes.Count ? Colour(scene.World.SoilTypes[soil].Color) : new Vec4(.5f, .5f, .5f, 1);
        return EditorStyle.Dim(colour, 0.75f + 0.35f * world.TileElevation(tile));
    }
    // Blue = passable, orange = soft: told apart by lightness as well as hue.
    // The editor shows the edited scene's biomes, not the applied ones: the same lookup as the game, on the scenario.
    internal int TileBiome(GameWorld world, int tile) => scene.World.BiomeOf(world.TileLevel(tile), tile / world.TileGrid.Rows, tile % world.TileGrid.Rows);
    internal Vec4 BiomeColour(GameWorld world, int tile)
    {
        int b = TileBiome(world, tile);
        return EditorStyle.Dim(b < 0 ? new Vec4(.35f, .35f, .35f, 1) : Colour(scene.World.Biomes[b].Color),
            world.TileSlope(tile) switch { TerrainSlope.Flat => 1f, TerrainSlope.Gentle => .88f, _ => .7f });
    }
    /// <summary>Tiles per biome; the last slot counts the tiles whose zone has no biome.</summary>
    internal int[] BiomeCounts(GameWorld world)
    {
        var counts = new int[scene.World.Biomes.Count + 1];
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int t = 0; t < tiles; t++) { int b = TileBiome(world, t); counts[b < 0 ? ^1 : b]++; }
        return counts;
    }

    /// <summary>The elevation zones from low to high: name, colour and the highest level each one covers.</summary>
    private void ZonePanel(GameWorld world)
    {
        EditorStyle.Section("Magassági övek");
        ImGui.TextWrapped($"Egy öv a lenti övtől a felső szintjéig tart; a legfelső felfelé nyitott. A terep 0–{world.Map.MaximumHeight}. szintig emelhető, a víz a {world.Map.WaterHeight}. szintig áll.");
        var zones = scene.World.ElevationZones;
        int remove = -1;
        for (int i = 0; i < zones.Count; i++) {
            var zone = zones[i];
            ImGui.PushID("zone" + i);
            var c = Colour(zone.Color); var rgb = new System.Numerics.Vector3(c.X, c.Y, c.Z);
            if (ImGui.ColorEdit3("##colour", ref rgb, ImGuiColorEditFlags.NoInputs)) scene.Edit(zone, edited => edited.Color = Hex(new Vec4(rgb, 1)));
            ImGui.SameLine();
            string name = zone.Name; ImGui.SetNextItemWidth(zones.Count > 1 ? -(EditorStyle.ButtonWidth("Törlés") + 8) : -1);
            if (ImGui.InputText("##name", ref name, 40) && name.Trim().Length > 0) scene.Edit(zone, edited => edited.Name = name.Trim());
            if (zones.Count > 1) { ImGui.SameLine(); if (EditorStyle.QuietButton("Törlés")) remove = i; }
            int low = i == 0 ? 0 : zones[i - 1].MaxLevel + 1;
            if (i < zones.Count - 1) {
                int top = zone.MaxLevel; ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{low}. szinttől"); ImGui.SameLine(); ImGui.SetNextItemWidth(ImGui.GetFontSize() * 6);
                // The upper bound stays between this zone's floor and the next zone's top, so the order never breaks.
                if (ImGui.InputInt("szintig##top", ref top)) scene.Edit(zone, edited => edited.MaxLevel = Math.Clamp(top, low, zones[i + 1].MaxLevel - 1));
            } else { ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{low}. szinttől felfelé"); }
            ImGui.PopID();
            ImGui.Spacing();
        }
        if (remove >= 0) {
            // The removed zone's levels go to the zone above it; removing the top zone opens the one below upwards.
            scene.EditWorld(world => {
                var owned = world.ElevationZones;
                if (remove == owned.Count - 1) owned[remove - 1].MaxLevel = owned[remove].MaxLevel;
                owned.RemoveAt(remove);
            });
        }
        ImGui.BeginDisabled(zones.Count >= 8 || zones[^1].MaxLevel - (zones.Count > 1 ? zones[^2].MaxLevel : -1) < 2);
        if (ImGui.Button("Új öv a legfelső alá", new Vec2(-1, 0))) {
            int floor = zones.Count > 1 ? zones[^2].MaxLevel + 1 : 0;
            scene.EditWorld(world => world.ElevationZones.Insert(world.ElevationZones.Count - 1, new ElevationZoneDefinition { Id = "zone" + Guid.NewGuid().ToString("N")[..6], Name = "Új öv", Color = "#9A9A9A", MaxLevel = floor }));
        }
        ImGui.EndDisabled();
        if (ImGui.Button("Alapértelmezett övek", new Vec2(-1, 0))) scene.EditWorld(scene => scene.ElevationZones = ElevationZoneDefinition.Defaults(scene.MaxHeight));
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
                $"{scene.World.Species.Find(x => x.Id == g.Key.ToString())?.Name ?? g.Key.ToString()} · {Share(g.Count())}")));
            if (counts.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs erdő a pályán.");
            return;
        }
        if (mapLayer is MapLayer.Soil or MapLayer.Tracks) {
            var counts = new int[scene.World.SoilTypes.Count];
            for (int t = 0; t < tiles; t++) { int s = world.SoilIndex(t); if (s < counts.Length) counts[s]++; }
            Legend(scene.World.SoilTypes.Select((type, i) => (Colour(type.Color), $"{type.Name} · {Share(counts[i])}")));
            if (mapLayer == MapLayer.Tracks) { EditorStyle.Text(HudTheme.Muted, "szürke: aszfalt · világos: makadám · barna: földút/épület · narancs: közelítőnyom"); }
            return;
        }
        if (mapLayer == MapLayer.Ground) {
            var used = new HashSet<string>(scene.World.Biomes.SelectMany(b => new[] { b.DryGround, b.WetGround, b.SteepGround }));
            Legend(scene.World.GroundMaterials.Where(m => used.Contains(m.Id)).Select(m => (Colour(m.Color), m.Name)));
            EditorStyle.Text(HudTheme.Muted, "a csempék színe keveredik: száraz és nedves a nedvesség, meredek a lejtő szerint");
            return;
        }
        if (mapLayer == MapLayer.Biomes) {
            var counts = BiomeCounts(world);
            Legend(scene.World.Biomes.Select((biome, i) => (Colour(biome.Color), $"{biome.Name} · {Share(counts[i])}")));
            if (counts[^1] > 0) EditorStyle.Text(HudTheme.Muted, $"szürke: biom nélküli öv · {Share(counts[^1])}");
            return;
        }
        if (mapLayer == MapLayer.Zones) {
            var counts = new int[scene.World.ElevationZones.Count];
            for (int t = 0; t < tiles; t++) counts[scene.World.ZoneOf(world.TileLevel(t))]++;
            Legend(scene.World.ElevationZones.Select((zone, i) => (Colour(zone.Color), $"{zone.Name} · {Share(counts[i])}")));
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
}
