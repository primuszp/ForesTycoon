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

internal enum WorldTab { Map, Types, Rules, Trial }
internal enum MapLayer { Soil, Elevation, Water, GroundWater, Forest, Tracks, Trafficability }
internal enum TypeTab { Soil, Species, Surfaces }

/// <summary>
/// The world editor: one world, built from a generated and painted map, its types (soil) and its rules (readable
/// formulas run on every tile). Four workspaces: map, types, rules, trial.
/// </summary>
internal sealed partial class RuleEditorView
{
    private WorldTab worldTab;
    private MapLayer mapLayer;
    private WorldScenario scenario;
    private string appliedScenario;
    private int brushSoil = 1, brushSize = 2, selectedTile = -1, selectedSoilType, seedEdit;
    // Elevation: 0 raise, 1 lower, 2 flatten. Water: 0 lake, 1 land, 2 spring, 3 remove spring. Forest: species index, -1 clears.
    private float springRate = 300;
    private int elevationTool, waterTool, brushSpecies, selectedSpecies;
    private TypeTab typeTab;
    private readonly HashSet<int> stroke = new();
    private string selectedRuleHook = "trail.trafficability|tile", formulaText, formulaFor, formulaError;
    private readonly Dictionary<string, double> previewInputs = new(StringComparer.Ordinal);
    private int trialIntensity = 25, trialSeconds = 60;
    private Task<WorldTrialResult> worldTrial;
    private WorldTrialResult worldTrialResult;

    internal sealed record WorldTrialResult(double[] Baseline, double[] Edited, double[] Water, bool[] Unpaved, string Summary);

    internal void ShowWorldTrial(WorldTrialResult result) { worldTab = WorldTab.Trial; worldTrialResult = result; }
    internal void ShowWorld(WorldTab tab, MapLayer layer = MapLayer.Soil, int tile = -1, string rule = null)
    {
        worldTab = tab; mapLayer = layer; if (tile >= 0) selectedTile = tile;
        if (rule != null) selectedRuleHook = rule;
    }
    /// <summary>A spring on the map, then some simulated time: shows where its water goes.</summary>
    internal void AddSpring(GameWorld world, int tile, double rate, double seconds)
    {
        Scenario(world).Springs[tile] = rate; ApplyProject(world, false); world.ExecutePendingCommands();
        for (int i = 0; i < seconds * 30; i++) world.Update(1.0 / 30);
    }
    internal void ShowWorldTypes(TypeTab tab, int index = 0) { worldTab = WorldTab.Types; typeTab = tab; selectedSpecies = index; }

    private WorldScenario Scenario(GameWorld world)
    {
        if (scenario != null) return scenario;
        scenario = world.RuleProjectDocument.World ?? GameWorld.DefaultScenario(world.Map.Settings.Seed);
        // Maps made before species moved here edit the built-in species.
        if (scenario.Species.Count == 0) scenario.Species = GameWorld.DefaultScenario().Species;
        seedEdit = scenario.Seed;
        return scenario;
    }
    private string ScenarioKey() => new RuleProject { World = scenario }.ToJson();
    /// <summary>The editor's test world is rebuilt from the map whenever the map or a type changes.</summary>
    private void ApplyScenario(GameWorld world, string what)
    {
        try {
            scenario.Validate();
            world.StartScenario(scenario); appliedScenario = ScenarioKey();
            message = what + " A tesztvilág újraépült a pályából.";
        } catch (Exception e) when (e is System.IO.InvalidDataException or ArgumentException or InvalidOperationException) { message = e.Message; }
    }

    // ── Frame ────────────────────────────────────────────────────────────
    private void WorldHeader(GameWorld world)
    {
        EditorStyle.Title("Világszerkesztő", true);
        ImGui.SameLine(0, 24); ImGui.AlignTextToFramePadding();
        foreach (var (tab, label, help) in new[] {
            (WorldTab.Map, "Pálya", "A világ térképe: generálás, majd talaj festése; rétegek, csempék."),
            (WorldTab.Types, "Típusok", "Talajtípusok és paramétereik: amit a térkép fest, és amit a szabályok olvasnak."),
            (WorldTab.Rules, "Szabályok", "A világ szabályai képletként: minden csempén ugyanúgy futnak."),
            (WorldTab.Trial, "Próba", "Eső a pályán: az alap és a szerkesztett szabályok összevetése.") }) {
            ImGui.SameLine(0, 6);
            if (ViewTab(label, worldTab == tab, help)) worldTab = tab;
        }
        ImGui.SameLine(0, 30);
        if (ImGui.Button("Mentés · Ctrl+S")) Try(() => SaveProjectDraft(world));
        if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) Try(() => SaveProjectDraft(world));
        ImGui.SameLine();
        if (EditorStyle.PrimaryButton(algorithmBuild != null ? "Alkalmazás folyamatban…" : "Alkalmazás a játékban", algorithmBuild == null,
            "Közzéteszi a projektet: a futó játék átveszi a szabályokat, az új világ ebből a pályából épül.")) Try(() => ApplyProject(world, true));
        bool running = publishedProjectRevision != null && workshopVersions.Running == publishedProjectRevision && liveObjects?.Error == null;
        EditorStyle.Text(HudTheme.Muted, (workshopVersions.Unsaved ? "Mentetlen módosítás" : "Mentve") + " · "
            + (publishedProjectRevision == null ? "a játékban még az előző szabályok futnak" : running ? "a játék átvette" : "a játék átvételére vár")
            + (appliedScenario != ScenarioKey() ? " · a tesztvilág nem ebből a pályából készült" : ""));
        ImGui.Separator();
    }

    private bool scenarioBuilt;
    private void WorldBody(GameWorld world, bool standalone = false)
    {
        Scenario(world);
        if (standalone && !scenarioBuilt) { scenarioBuilt = true; if (appliedScenario != ScenarioKey()) ApplyScenario(world, "A tesztvilág a pályából épült."); }
        switch (worldTab) {
            case WorldTab.Types: TypesTab(world); break;
            case WorldTab.Rules: RulesTab(world); break;
            case WorldTab.Trial: TrialTab(world); break;
            default: MapTab(world); break;
        }
    }

    // ── Map ──────────────────────────────────────────────────────────────
    private static readonly (MapLayer Layer, string Name, string Help)[] Layers = {
        (MapLayer.Soil, "Talaj", "Talajtípus csempénként. Festhető."),
        (MapLayer.Elevation, "Domborzat", "Magasság. Festhető: emelés, süllyesztés, simítás."),
        (MapLayer.Water, "Víz", "Tavak és folyók. Festhető: a vízszint alá süllyesztett terep megtelik."),
        (MapLayer.GroundWater, "Talajvíz", "Gyökérzóna telítettsége."),
        (MapLayer.Forest, "Erdő", "Fafaj és fatérfogat. Festhető: kezdő erdő fajjal, vagy irtás."),
        (MapLayer.Tracks, "Utak és nyomok", "Burkolt utak és közelítőnyomok."),
        (MapLayer.Trafficability, "Járhatóság", "A járhatósági szabály eredménye most, burkolatlan csempéken.") };

    private static readonly int[] MapSizes = { 32, 48, 64, 96, 128 };

    private void MapTab(GameWorld world)
    {
        float left = 250, right = 320;
        ImGui.BeginChild("map-left", new Vec2(left, 0), ImGuiChildFlags.Borders);
        EditorStyle.Section("Réteg");
        foreach (var (layer, layerName, help) in Layers) {
            if (ImGui.Selectable(layerName, mapLayer == layer)) mapLayer = layer;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        }
        EditorStyle.Section("Generálás");
        ImGui.TextWrapped("A pálya seedből generálódik, utána festéssel alakítod.");
        ImGui.SetNextItemWidth(-1); ImGui.InputInt("##seed", ref seedEdit);
        if (ImGui.Button("Új seed")) seedEdit = new Random().Next(1, 999999);
        ImGui.SameLine();
        if (ImGui.Button("Generálás")) { scenario.Seed = seedEdit; ApplyScenario(world, $"Generálva a {seedEdit} seedből; a festés megmaradt."); }
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
        EditorStyle.Section("Szimuláció");
        if (ImGui.Button(behaviors.IsPaused ? "Indítás" : "Szünet")) behaviors.TogglePause();
        ImGui.SameLine();
        void Advance(double hours) { world.ExecutePendingCommands(); world.Update(hours * world.SecondsPerEnvironmentHour); }
        if (ImGui.Button("1 óra előre")) Advance(1);
        ImGui.SameLine(); if (ImGui.Button("1 nap előre")) Advance(24);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{24 * world.SecondsPerEnvironmentHour:0} játékmásodperc; nagy pályán néhány másodpercig tart.");
        EditorStyle.Text(HudTheme.Muted, (behaviors.IsPaused ? "Áll" : "Fut") + $" · forrásból eddig {world.Environment.SpringReceived / 1000 * world.TileArea:0} m³");
        EditorStyle.Section("Méret");
        int sizeIndex = Array.IndexOf(MapSizes, scenario.Size);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##size", ref sizeIndex, MapSizes.Select(n => $"{n} × {n} csempe").ToArray(), MapSizes.Length) && sizeIndex >= 0 && MapSizes[sizeIndex] != scenario.Size) {
            scenario.Size = MapSizes[sizeIndex]; scenario.SoilPaint.Clear(); scenario.HeightPaint.Clear(); scenario.ForestPaint.Clear(); scenario.Springs.Clear();
            ApplyScenario(world, $"A pálya mérete {scenario.Size} × {scenario.Size}; a festés törlődött, mert a csempék újraszámozódtak.");
        }
        EditorStyle.Section("Pálya neve");
        string name = scenario.Name; ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##scenario-name", ref name, 80) && name.Trim().Length > 0) scenario.Name = name.Trim();
        ImGui.EndChild(); ImGui.SameLine();

        ImGui.BeginChild("map-canvas", new Vec2(-right - 8, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        int hovered = TileMap(world, "pálya", TileColour(world), painting: mapLayer is MapLayer.Soil or MapLayer.Elevation or MapLayer.Water or MapLayer.Forest);
        MapLegend(world);
        ImGui.EndChild(); ImGui.SameLine();

        ImGui.BeginChild("map-right", new Vec2(0, 0), ImGuiChildFlags.Borders);
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
        EditorStyle.Fact("Magasság", world.TileElevation(tile).ToString("0.00"));
        ImGui.EndTable();
        int soil = world.SoilIndex(tile);
        if (soil < scenario.SoilTypes.Count && ImGui.SmallButton("Talajtípus megnyitása")) { selectedSoilType = soil; worldTab = WorldTab.Types; }
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
        MapLayer.Water => tile => world.IsWaterTile(tile) ? new(0.25f, 0.50f, 0.90f, 1)
            : Mix(Mix(new(0.22f, 0.24f, 0.20f, 1), new(0.62f, 0.60f, 0.52f, 1), world.TileElevation(tile)), new(0.30f, 0.62f, 0.95f, 1),
                (float)Math.Sqrt(Math.Clamp((world.TileSurfaceWater(tile) - 0.5) / 100, 0, 1))),
        MapLayer.GroundWater => tile => Mix(new(0.30f, 0.25f, 0.17f, 1), new(0.30f, 0.56f, 0.88f, 1), (float)world.TileWater(tile)),
        MapLayer.Forest => tile => world.TileSpecies(tile) is var s && s != ForesTycoon.Ecology.ForestSpecies.None
            ? Mix(new(0.17f, 0.19f, 0.14f, 1), SpeciesColour(s.ToString()), 0.45f + 0.55f * (float)Math.Clamp(world.TileForest(tile) / 30, 0, 1))
            : world.IsWaterTile(tile) ? new(0.20f, 0.32f, 0.50f, 1) : new(0.17f, 0.19f, 0.14f, 1),
        MapLayer.Tracks => tile => world.Map.IsRoadTile(tile) ? new(0.85f, 0.82f, 0.75f, 1) : world.Map.IsSkidTrail(tile) ? new(0.85f, 0.55f, 0.28f, 1)
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

    private void MapLegend(GameWorld world)
    {
        var draw = ImGui.GetWindowDrawList();
        if (mapLayer == MapLayer.Forest) {
            int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
            var counts = Enumerable.Range(0, tiles).Select(world.TileSpecies).Where(s => s != ForesTycoon.Ecology.ForestSpecies.None)
                .GroupBy(s => s).OrderByDescending(g => g.Count()).Take(6).ToArray();
            for (int i = 0; i < counts.Length; i++) {
                if (i > 0) ImGui.SameLine(0, 16);
                Vec2 p = ImGui.GetCursorScreenPos();
                draw.AddRectFilled(p + new Vec2(0, 3), p + new Vec2(14, 17), EditorStyle.U(SpeciesColour(counts[i].Key.ToString())), 2);
                ImGui.Dummy(new Vec2(18, 20)); ImGui.SameLine();
                string name = scenario.Species.Find(x => x.Id == counts[i].Key.ToString())?.Name ?? counts[i].Key.ToString();
                ImGui.TextUnformatted($"{name} · {100.0 * counts[i].Count() / Math.Max(1, tiles):0}%");
            }
            if (counts.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs erdő a pályán.");
            return;
        }
        if (mapLayer is MapLayer.Soil or MapLayer.Tracks) {
            var counts = new int[scenario.SoilTypes.Count];
            int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
            for (int t = 0; t < tiles; t++) { int s = world.SoilIndex(t); if (s < counts.Length) counts[s]++; }
            for (int i = 0; i < scenario.SoilTypes.Count; i++) {
                if (i > 0) ImGui.SameLine(0, 18);
                Vec2 p = ImGui.GetCursorScreenPos();
                draw.AddRectFilled(p + new Vec2(0, 3), p + new Vec2(14, 17), EditorStyle.U(Colour(scenario.SoilTypes[i].Color)), 2);
                ImGui.Dummy(new Vec2(18, 20)); ImGui.SameLine();
                ImGui.TextUnformatted($"{scenario.SoilTypes[i].Name} · {100.0 * counts[i] / Math.Max(1, tiles):0}%");
            }
            if (mapLayer == MapLayer.Tracks) { ImGui.SameLine(0, 18); EditorStyle.Text(HudTheme.Muted, "világos: burkolt út · narancs: közelítőnyom"); }
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
    private void TypesTab(GameWorld world)
    {
        foreach (var (tab, label, help) in new[] {
            (TypeTab.Soil, "Talajtípusok", "Amit a térkép fest: víztartás, termékenység, teherbírás."),
            (TypeTab.Species, "Fafajok", "Növekedés, termőhely, méret és faanyagár."),
            (TypeTab.Surfaces, "Burkolatok", "Utak és nyomok: költség, kopás, romlás, menetellenállás.") }) {
            if (tab != TypeTab.Soil) ImGui.SameLine(0, 6);
            if (ViewTab(label, typeTab == tab, help)) typeTab = tab;
        }
        ImGui.Separator();
        if (typeTab == TypeTab.Species) { SpeciesTab(world); return; }
        if (typeTab == TypeTab.Surfaces) { SurfacesTab(world); return; }
        ImGui.BeginChild("types-list", new Vec2(280, 0), ImGuiChildFlags.Borders);
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
        bool used = scenario.SoilPaint.ContainsValue(type.Id);
        ImGui.BeginDisabled(scenario.SoilTypes.Count == 1 || used);
        if (ImGui.Button("Törlés", new Vec2(-1, 0))) { scenario.SoilTypes.RemoveAt(selectedSoilType); selectedSoilType = 0; ApplyScenario(world, "Talajtípus törölve."); }
        ImGui.EndDisabled();
        if (used) ImGui.TextWrapped("Festve van a térképen, ezért nem törölhető.");
        ImGui.EndChild(); ImGui.SameLine();

        ImGui.BeginChild("type-form", new Vec2(0, 0), ImGuiChildFlags.Borders);
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
        ImGui.TextWrapped("A típusok új világ létrehozásakor kerülnek a játékba; a futó játék világa a régi talajjal fut tovább. A szerkesztőben a tesztvilág minden módosításkor újraépül.");
        ImGui.EndChild();
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
        ImGui.BeginChild("species-list", new Vec2(280, 0), ImGuiChildFlags.Borders);
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
        ImGui.BeginChild("species-form", new Vec2(0, 0), ImGuiChildFlags.Borders);
        EditorStyle.Title(sp.Name);
        int count = 0, tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int t = 0; t < tiles; t++) if (world.TileSpecies(t).ToString() == sp.Id) count++;
        EditorStyle.Text(HudTheme.Muted, $"A tesztvilágban {count} csempén áll ({100.0 * count / Math.Max(1, tiles):0}%) · azonosító: {sp.Id}");
        bool changed = ParameterTable("species-fields", Row => {
            Row("Név", "Megjelenik a játékban és a listákban.", () => { string n = sp.Name; if (ImGui.InputText("##name", ref n, 80) && n.Trim().Length > 0) sp.Name = n.Trim(); });
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
        ImGui.BeginChild("surfaces", new Vec2(0, 0), ImGuiChildFlags.Borders);
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

    // ── Rules ────────────────────────────────────────────────────────────
    private (BehaviorHook Hook, GameRuleDefinition Rule)[] FormulaRules() => WorldBehaviorPolicy.Hooks
        .Where(h => !h.Id.StartsWith("tuning.") && h.Kind != "dynamics")
        .Select(h => (Hook: h, Rule: currentRules.Index.Rules.FirstOrDefault(r => RuleWorkshopModel.HookBelongsTo(h, r))))
        .Where(t => t.Rule != null).OrderBy(t => RuleWorkshopModel.Topic(t.Rule)).ThenBy(t => t.Hook.Name, StringComparer.CurrentCulture).ToArray();
    private static string HookKey(BehaviorHook hook) => hook.Id + "|" + hook.Kind;
    private static string HookName(BehaviorHook hook) => hook.Name.StartsWith(hook.Kind + " ")
        ? RuleWorkshopModel.KindName(hook.Kind) + hook.Name[hook.Kind.Length..] : hook.Name;

    private string FormulaSource(BehaviorHook hook)
    {
        if (authoring.Formulas.TryGetValue(HookKey(hook), out var text)) return text;
        var graph = behaviors.Document.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
        if (graph == null || graph.Nodes.Find(n => n.Id == graph.Output)?.Operation == BehaviorOperation.Native) return RuleFormula.Default(hook);
        return RuleFormula.Decompile(graph, RuleFormula.Names(hook, dynamicsEditor.Draft.Stocks.Select(s => s.Id)));
    }
    private bool HasFormula(BehaviorHook hook) => authoring.Formulas.ContainsKey(HookKey(hook))
        || behaviors.Document.Graphs.Any(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null && g.Nodes.Find(n => n.Id == g.Output)?.Operation != BehaviorOperation.Native);

    private void RulesTab(GameWorld world)
    {
        var rules = FormulaRules();
        var selected = rules.FirstOrDefault(r => HookKey(r.Hook) == selectedRuleHook);
        if (selected.Hook == null) { selected = rules[0]; selectedRuleHook = HookKey(selected.Hook); }
        var names = RuleFormula.Names(selected.Hook, dynamicsEditor.Draft.Stocks.Select(s => s.Id));
        if (formulaFor != selectedRuleHook) { formulaText = FormulaSource(selected.Hook); formulaFor = selectedRuleHook; formulaError = null; previewInputs.Clear(); }

        ImGui.BeginChild("rules-list", new Vec2(300, 0), ImGuiChildFlags.Borders);
        int topic = -1;
        foreach (var (hook, rule) in rules) {
            int t = RuleWorkshopModel.Topic(rule);
            if (t != topic) { topic = t; EditorStyle.Section(RuleWorkshopModel.Topics[t].Name); }
            bool own = HasFormula(hook);
            if (ImGui.Selectable((own ? "• " : "   ") + HookName(hook) + "##" + HookKey(hook), HookKey(hook) == selectedRuleHook)) selectedRuleHook = HookKey(hook);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(own ? "Saját képlet fut." : "A játék beépített számítása fut; a képlet a beépített eredményt adja.");
        }
        ImGui.EndChild(); ImGui.SameLine();

        ImGui.BeginChild("rule-edit", new Vec2(-360, 0), ImGuiChildFlags.Borders);
        EditorStyle.Title(HookName(selected.Hook));
        ImGui.TextWrapped(selected.Rule.Description);
        if (EditorStyle.BeginFacts("rule-scope")) {
            EditorStyle.Fact("Hol fut?", ScopeOf(selected.Hook) + " – a világ minden ilyen elemén ugyanúgy.");
            EditorStyle.Fact("Mikor fut?", selected.Hook.Schedule);
            EditorStyle.Fact("Eredmény", $"{selected.Hook.Unit}, {selected.Hook.Min:G4}–{selected.Hook.Max:G4} közé korlátozva");
            ImGui.EndTable();
        }
        EditorStyle.Section("Képlet");
        string text = formulaText;
        if (ImGui.InputTextMultiline("##formula", ref text, 4000, new Vec2(-1, 150))) { formulaText = text; TryFormula(selected.Hook, names, apply: false); }
        if (formulaError != null) { ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Bad, formulaError); ImGui.PopTextWrapPos(); }
        else EditorStyle.Text(HudTheme.Good, "Érvényes képlet.");
        if (EditorStyle.PrimaryButton("Képlet használata", formulaError == null, "A képlet lesz a szabály a projektben és a tesztvilágban.")) TryFormula(selected.Hook, names, apply: true);
        ImGui.SameLine();
        if (ImGui.Button("Vissza a beépítettre")) {
            RememberAuthoring(); authoring.Formulas.Remove(HookKey(selected.Hook));
            behaviors.RestoreDefault(selected.Hook.Id, selected.Hook.Kind, null);
            formulaFor = null; Try(() => ApplyProject(world, false)); message = selected.Hook.Name + ": a beépített számítás fut.";
        }
        EditorStyle.Section("Használható nevek");
        ImGui.TextWrapped("Műveletek: + - * /, összehasonlítás < >, zárójel. Függvények: min(a; b), max(a; b), abs(x), ha(feltétel; akkor; különben), korlát(x; alsó; felső). Tizedesvessző vagy pont; # után megjegyzés; név = érték köztes lépés.");
        if (ImGui.BeginTable("names", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
            ImGui.TableSetupColumn("név", ImGuiTableColumnFlags.WidthFixed, 190); ImGui.TableSetupColumn("jelentés");
            foreach (var n in names.Where(n => n.Operation != BehaviorOperation.NamedValue || n.Key.StartsWith("stock.") || n.Key is "world.rain" or "world.time" or "world.balance" or "world.temperature")) {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
                if (ImGui.Selectable(n.Name + "##name" + n.Name)) { formulaText = (formulaText.TrimEnd() + " " + n.Name).TrimStart(); TryFormula(selected.Hook, names, apply: false); }
                ImGui.TableSetColumnIndex(1); ImGui.TextWrapped(n.Help);
            }
            ImGui.EndTable();
        }
        EditorStyle.Text(HudTheme.Muted, "Kattintás: a név a képlet végére kerül. További világértékek: világ.fák, világ.utak, világ.hó, világ.bevétel…");
        ImGui.EndChild(); ImGui.SameLine();

        ImGui.BeginChild("rule-preview", new Vec2(0, 0), ImGuiChildFlags.Borders);
        RulePreview(world, selected.Hook, names);
        ImGui.EndChild();
    }

    private static string ScopeOf(BehaviorHook hook) => hook.Kind switch {
        "tile" => "minden csempe", "road" => "minden útcsempe", "tree" => "minden fa", "forwarder" => "minden forwarder",
        "processor" => "minden processzor", "truck" => "minden teherautó", "mill" => "minden malom", "world" => "a világ", _ => hook.Kind };

    private BehaviorGraph TryFormula(BehaviorHook hook, IReadOnlyList<FormulaName> names, bool apply)
    {
        try {
            var target = new BehaviorGraph { Id = "formula." + hook.Kind + "." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind };
            var graph = RuleFormula.Compile(formulaText, target, names);
            _ = new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, WorldBehaviorPolicy.Hooks,
                DynamicsRuntime.Keys(dynamicsEditor.Draft, GameWorld.RuleWorldFields), _ => 0);
            formulaError = null;
            if (apply) {
                RememberAuthoring(); authoring.Formulas[HookKey(hook)] = formulaText;
                behaviors.SetTypeGraph(graph);
                worldApplyRequested = true;
                message = hook.Name + ": a képlet a szabály. A tesztvilágban már ez fut; mentés és alkalmazás után a játékban is.";
            }
            return graph;
        } catch (Exception e) when (e is FormatException or System.IO.InvalidDataException) { formulaError = e.Message; return null; }
    }
    private bool worldApplyRequested;

    private void RulePreview(GameWorld world, BehaviorHook hook, IReadOnlyList<FormulaName> names)
    {
        if (worldApplyRequested) { worldApplyRequested = false; Try(() => ApplyProject(world, false)); }
        EditorStyle.Section("Próbaszámítás");
        var graph = formulaError == null ? TryFormula(hook, names, apply: false) : null;
        if (graph == null) { ImGui.TextWrapped("Javítsd a képletet az előnézethez."); return; }
        var used = graph.Nodes.Select(n => names.FirstOrDefault(x => x.Operation == n.Operation && (n.Operation is not (BehaviorOperation.NamedValue or BehaviorOperation.Input) || x.Key == n.Key)))
            .Where(n => n != null && n.Operation != BehaviorOperation.Constant).Distinct().ToArray();
        bool traffic = hook.Id == "trail.trafficability", tile = hook.Kind == "tile";
        if (tile && selectedTile >= 0 && !previewInputs.ContainsKey("#tile" + selectedTile)) {
            previewInputs.Clear(); previewInputs["#tile" + selectedTile] = 1;
            foreach (var (key, value) in world.TileRuleInputs(selectedTile)) previewInputs[key] = value;
            if (traffic) previewInputs["beépített"] = 1 - previewInputs["átnedvesedés"] * (1 - previewInputs["talaj.teherbírás"]);
        }
        if (tile) EditorStyle.Text(HudTheme.Muted, selectedTile >= 0 ? $"Bemenetek a #{selectedTile} csempéről (Pálya fül)" : "Válassz csempét a Pálya fülön, vagy állítsd a bemeneteket.");
        foreach (var n in used) {
            double value = previewInputs.TryGetValue(n.Name, out var v) ? v : n.Operation == BehaviorOperation.NamedValue ? Safe(() => PreviewValue(world, n.Key)) : 0.5;
            float f = (float)value; ImGui.SetNextItemWidth(-130);
            if (ImGui.DragFloat(n.Name, ref f, 0.01f)) previewInputs[n.Name] = f; else previewInputs[n.Name] = value;
        }
        double Input(BehaviorOperation op) => previewInputs.TryGetValue(names.FirstOrDefault(n => n.Operation == op)?.Name ?? "", out var v) ? v : 0;
        var model = new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, WorldBehaviorPolicy.Hooks,
            DynamicsRuntime.Keys(dynamicsEditor.Draft, GameWorld.RuleWorldFields), key => used.FirstOrDefault(u => u.Key == key) is FormulaName n && previewInputs.TryGetValue(n.Name, out var v) ? v : Safe(() => PreviewValue(world, key)));
        double result = model.Evaluate(hook.Id, hook.Kind, 0, new BehaviorInputs(Input(BehaviorOperation.Native), Input(BehaviorOperation.Delta), Input(BehaviorOperation.Time),
            Input(BehaviorOperation.State), Input(BehaviorOperation.Amount), Input(BehaviorOperation.Capacity), Input(BehaviorOperation.Available),
            Input(BehaviorOperation.From), Input(BehaviorOperation.To), Input(BehaviorOperation.Surface)),
            (hook.Inputs ?? Array.Empty<string>()).Select(name => previewInputs.TryGetValue(name, out var v) ? v : 0).ToArray());
        EditorStyle.Text(HudTheme.Muted, "Eredmény");
        EditorStyle.Title(result.ToString("0.###"), true, HudTheme.AmberAccent);
        if (model.LastError != null) EditorStyle.Text(HudTheme.Bad, model.LastError);
        if (!traffic) return;
        EditorStyle.Section("A képlet a pályán, most");
        ImGui.TextWrapped("Minden burkolatlan csempe a saját talajával és vizével.");
        var (columns, rows) = world.TileGrid;
        var values = new double[columns * rows];
        for (int t = 0; t < values.Length; t++) {
            var (wetness, bearing) = world.TileTrafficInputs(t);
            values[t] = model.Evaluate(hook.Id, hook.Kind, (ulong)t, new BehaviorInputs(1 - wetness * (1 - bearing), 0, 0, wetness, bearing));
        }
        TileMap(world, "preview", tile => world.IsUnpaved(tile) ? TrafficColour(values[tile]) : new(0.45f, 0.45f, 0.45f, 1), false,
            new Vec2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().X * rows / Math.Max(1, columns)));
        ImGui.TextWrapped("Száraz időben szinte minden kék. Az eső hatását a Próba fül mutatja.");
    }
    private static double Safe(Func<double> read) { try { double v = read(); return double.IsFinite(v) ? v : 0; } catch (ArithmeticException) { return 0; } }

    // ── Trial ────────────────────────────────────────────────────────────
    private void TrialTab(GameWorld world)
    {
        ImGui.BeginChild("trial-settings", new Vec2(300, 0), ImGuiChildFlags.Borders);
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

        ImGui.BeginChild("trial-maps", new Vec2(0, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
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
            using var world = new GameWorld(GameWorld.ScenarioSettings(project.World), soils: GameWorld.SoilModel(project.World), enableRendering: false);
            world.QueueRuleProject(project); world.ExecutePendingCommands();
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
