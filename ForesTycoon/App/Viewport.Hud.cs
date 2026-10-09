using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>
    /// Game HUD: the calm edge panels of Viewport.Lodge.cs (estate card, ledger, journal, tempo,
    /// verb dock, minimap, season card), the hover inspector, and closable information windows;
    /// every developer function sits in one window behind F12.
    /// </summary>
    sealed partial class Viewport
    {
        private bool showVehicles, showForestry, showGraphics, showDeveloper, showHelp;
        private bool showEnvironment, showManagement;
        private readonly ManagementView management = new();
        private int ecologyRasterLayer, ecologyRasterSelection = -1;
        private int environmentPreset, environmentIntensity = 12, environmentDuration = 90;
        private float toolbarBottom = 60f;
        // Room kept free above the bottom edge for the tempo panel, verb dock and minimap.
        private const float StatusBarHeight = 150f;

        private const double ToastSeconds = 4.5;
        private (ForestryActionResult Action, int Applied, int Tiles) lastForestryToast;

        private void ShowToast(string text, NVec4 color)
        {
            AddJournal(text, color);
        }

        private void DrawImGui()
        {
            if (imgui == null) return;

            float scale = DpiScale;
            imgui.Update(Width, Height, FramebufferWidth, FramebufferHeight, new NVec2(scale, scale), frameClock.DeltaTimeSeconds);

            TrackForestryResult();
            TrackSeason();
            DrawEstateCard();
            DrawLedger();
            DrawJournal();
            DrawTimeControl();
            DrawVerbDock();
            DrawMinimap();
            DrawHoverInspector();
            DrawVehiclesWindow();
            DrawForestryWindow();
            DrawEnvironmentWindow();
            DrawManagementWindow();
            DrawGraphicsWindow();
            DrawDeveloperWindow();
            DrawHelpWindow();
            DrawSeasonCard();

            imgui.Render();
        }

        private const ImGuiWindowFlags BarFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;

        private void DrawGameMenuPopup()
        {
            if (!ImGui.BeginPopup("game-menu")) return;
            ImGui.SeparatorText("Új térkép");
            if (MenuIconItem(GameIcon.NewMap, "Új terep (véletlen seed)")) RegenerateTerrain();
            if (ImGui.BeginMenu("Új nagy erdős térkép"))
            {
                if (ImGui.MenuItem("Fenyves és lombos erdő")) RegenerateTerrain(forestPattern: ForestPattern.LargeMixed);
                if (ImGui.MenuItem("Nagy fenyves")) RegenerateTerrain(forestPattern: ForestPattern.LargeSpruce);
                if (ImGui.MenuItem("Nagy lombos erdő")) RegenerateTerrain(forestPattern: ForestPattern.LargeBroadleaf);
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Térképméret"))
            {
                MapSizeMenuItem(64);
                MapSizeMenuItem(128);
                MapSizeMenuItem(256);
                ImGui.EndMenu();
            }
            ImGui.SeparatorText("Mentés");
            if (MenuIconItem(GameIcon.Save, "Gyorsmentés", "Ctrl+S")) QuickSave();
            if (MenuIconItem(GameIcon.Load, "Gyorsbetöltés", "Ctrl+L")) QuickLoad();
            ImGui.Separator();
            if (MenuIconItem(GameIcon.Exit, "Kilépés")) Close();
            ImGui.EndPopup();
        }

        private static bool MenuIconItem(GameIcon icon, string label, string shortcut = null, bool selected = false, bool enabled = true)
        {
            float size = ImGui.GetTextLineHeight() + 2f;
            NVec2 p = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.MenuItem("      " + label, shortcut ?? "", selected, enabled);
            GameIcons.Draw(ImGui.GetWindowDrawList(), icon, p + new NVec2(0, 0), size, GameIcons.Color(HudTheme.Parchment));
            return clicked;
        }

        private void SpeedButton(string id, GameIcon icon, double speed, float size, string title)
        {
            bool active = !simulationClock.IsPaused && Math.Abs(simulationClock.Speed - speed) < 0.001;
            if (HudTheme.IconButton(id, icon, size, active, title, null, "A szimuláció, a növekedés és a szállítás sebessége."))
            {
                simulationClock.Speed = speed;
                simulationClock.IsPaused = false;
            }
        }

        private void DrawFastSpeedButton(float size)
        {
            NVec2 menuPosition = ImGui.GetCursorScreenPos() + new NVec2(0, size + 6);
            if (captureDirectory != null && frameIndex == 284) ImGui.OpenPopup("simulation-speed");
            bool active = !simulationClock.IsPaused && simulationClock.Speed >= 4;
            if (HudTheme.IconButton("fast-forward", GameIcon.Faster, size, active,
                $"Időgyorsítás ({simulationClock.Speed:0}×)", null,
                "Válassz 4×–256× sebességet az erdő fejlődésének megfigyeléséhez. Nagy terhelésnél az elért gyorsítás kisebb lehet."))
                ImGui.OpenPopup("simulation-speed");
            if (ImGui.IsPopupOpen("simulation-speed"))
                ImGui.SetNextWindowPos(menuPosition, ImGuiCond.Appearing);
            if (ImGui.BeginPopup("simulation-speed"))
            {
                foreach (int speed in new[] { 4, 8, 16, 32, 64, 128, 256 })
                    if (ImGui.MenuItem($"{speed}×", "", !simulationClock.IsPaused && simulationClock.Speed == speed))
                    {
                        simulationClock.Speed = speed;
                        simulationClock.IsPaused = false;
                    }
                ImGui.EndPopup();
            }
        }

        private void ToolIcon(GameIcon icon, TerrainEditTool tool, float size, string title, string shortcut, string description)
        {
            if (HudTheme.IconButton("tool" + (int)tool, icon, size, interaction.ActiveTool == tool, title, shortcut, description))
                SelectTool(tool);
        }

        private static void WindowToggle(string id, GameIcon icon, ref bool open, float size, string title, string shortcut, string description)
        {
            if (HudTheme.IconButton(id, icon, size, open, title, shortcut, description)) open = !open;
        }

        // ── Contextual tool options (shown in the verb strip) ────────────────
        private void DrawToolOptions(TerrainEditTool tool)
        {
            ImGui.SameLine();
            switch (tool)
            {
                case TerrainEditTool.PlantForest:
                    SpeciesButton(ForestSpecies.Spruce, GameIcon.Spruce, "Lucfenyő", "Gyorsan nő, árnyéktűrő, sűrű állomány. Nedves, hűvös termőhely.");
                    ImGui.SameLine();
                    SpeciesButton(ForestSpecies.Birch, GameIcon.Birch, "Nyír", "Úttörő fafaj: gyors kezdés, rövidebb élet, fényigényes.");
                    ImGui.SameLine();
                    SpeciesButton(ForestSpecies.Oak, GameIcon.Oak, "Tölgy", "Lassú, hosszú életű, értékes faanyag. Szárazságtűrő.");
                    ImGui.SameLine();
                    SpeciesButton(ForestSpecies.Beech, GameIcon.Beech, "Bükk", "Árnyéktűrő, zárt lombkorona, közepes növekedés.");
                    ImGui.SameLine();
                    MoreSpeciesPicker();
                    break;
                case TerrainEditTool.Raise:
                case TerrainEditTool.Lower:
                    ImGui.PushItemWidth(130);
                    int brushSize = interaction.BrushSize;
                    int brushStrength = interaction.BrushStrength;
                    if (ImGui.SliderInt("Méret", ref brushSize, 1, 5)) interaction.BrushSize = brushSize;
                    ImGui.SameLine();
                    if (ImGui.SliderInt("Erő", ref brushStrength, 1, 5)) interaction.BrushStrength = brushStrength;
                    ImGui.PopItemWidth();
                    break;
                case TerrainEditTool.Road:
                    SurfaceButton(RoadPaving.Macadam, GameIcon.Macadam, "Makadám",
                        $"Olcsó ({RoadCosts.Build(RoadPaving.Macadam):0} eFt/csempe), de gyorsan romlik, főleg esőben és vizes talajon.");
                    ImGui.SameLine();
                    SurfaceButton(RoadPaving.Asphalt, GameIcon.Road, "Aszfalt",
                        $"Drága ({RoadCosts.Build(RoadPaving.Asphalt):0} eFt/csempe), de sokáig bírja.");
                    ImGui.SameLine();
                    ImGui.TextDisabled("Húzd az egeret a nyomvonalon.");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(interaction.IsRoadDragging
                        ? $"Hossz: {world.RoadPreviewCount} csempe · ~{world.RoadPreviewCount * RoadCosts.Build(interaction.RoadSurface):0} eFt"
                        : $"Út-csempék: {world.RoadCount}");
                    break;
                case TerrainEditTool.RoadRemove:
                    ImGui.TextDisabled("Húzd végig a bontandó szakaszon.");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(interaction.IsRoadDragging ? $"Hossz: {world.RoadPreviewCount} csempe" : $"Út-csempék: {world.RoadCount}");
                    break;
                case TerrainEditTool.RoadRepair:
                    ImGui.TextDisabled("Húzd végig a javítandó szakaszon; a sárga csempék kopottak.");
                    if (interaction.IsRoadDragging && interaction.RoadDragStartTileId >= 0 && world.HoveredTileId >= 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"Javítás: ~{world.RoadRepairCost(interaction.RoadDragStartTileId, world.HoveredTileId):0} eFt");
                    }
                    break;
                case TerrainEditTool.HarvestForest:
                    ImGui.TextDisabled("Húzással jelöld ki az erdőterületet. A fák rakodás közben fogynak.");
                    if (interaction.IsForestryDragging) { ImGui.SameLine(); ImGui.TextUnformatted($"{world.ForestryPreviewCount} csempe"); }
                    break;
                case TerrainEditTool.PlaceSawmill:
                    ImGui.TextDisabled("Zöld keret: építhető. 2×2 sík, üres, száraz csempe; mellé út kell.");
                    break;
            }
        }

        private void SurfaceButton(RoadPaving surface, GameIcon icon, string label, string description)
        {
            if (HudTheme.LabeledIconButton(label, icon, interaction.RoadSurface == surface, 30f, description))
                interaction.RoadSurface = surface;
        }

        private void SpeciesButton(ForestSpecies species, GameIcon icon, string label, string description)
        {
            if (HudTheme.LabeledIconButton(label, icon, interaction.PlantingSpecies == species, 30f, description))
                interaction.PlantingSpecies = species;
        }

        // Maple, ash, further oaks, pines and shrubs live in a grouped drop-down beside the four classic buttons.
        private void MoreSpeciesPicker()
        {
            var current = interaction.PlantingSpecies;
            bool extra = Array.IndexOf(ForestSpeciesTraits.Playable, current) >= 4;
            ImGui.PushItemWidth(200);
            ImGui.AlignTextToFramePadding();
            if (ImGui.BeginCombo("##species-more", extra ? ForestSpeciesTraits.For(current).Name : "További fajok"))
            {
                int group = -1;
                foreach (var species in ForestSpeciesTraits.Playable)
                {
                    if (Array.IndexOf(ForestSpeciesTraits.Playable, species) < 4) continue;
                    var traits = ForestSpeciesTraits.For(species);
                    int kind = traits.Shrub ? 2 : traits.Conifer ? 1 : 0;
                    if (kind != group)
                    {
                        group = kind;
                        ImGui.TextDisabled(kind == 0 ? "Lombos fák" : kind == 1 ? "Fenyők" : "Cserjék");
                    }
                    if (ImGui.Selectable($"{traits.Name}##{(int)species}", species == current)) interaction.PlantingSpecies = species;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{traits.Latin}\n{traits.Description}");
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
        }

        private static GameIcon ToolIconFor(TerrainEditTool tool) => tool switch
        {
            TerrainEditTool.Raise => GameIcon.Raise,
            TerrainEditTool.Lower => GameIcon.Lower,
            TerrainEditTool.Road => GameIcon.Road,
            TerrainEditTool.RoadRemove => GameIcon.RoadRemove,
            TerrainEditTool.RoadRepair => GameIcon.RoadRepair,
            TerrainEditTool.PlantForest => GameIcon.Plant,
            TerrainEditTool.HarvestForest => GameIcon.Harvest,
            TerrainEditTool.PlaceSawmill => GameIcon.Sawmill,
            _ => GameIcon.Inspect
        };

        private static string SeasonName(double fraction) => fraction switch
        {
            < 0.25 => "tavasz",
            < 0.50 => "nyár",
            < 0.75 => "ősz",
            _ => "tél"
        };

        private static string WeatherName(WeatherPreset preset) => preset switch
        {
            WeatherPreset.Sunny => "Napos",
            WeatherPreset.Cloudy => "Borult",
            WeatherPreset.Rain => "Eső",
            WeatherPreset.Snow => "Havazás",
            _ => "Vihar"
        };

        private static GameIcon WeatherIcon(WeatherPreset preset) => preset switch
        {
            WeatherPreset.Sunny => GameIcon.Sun,
            WeatherPreset.Cloudy => GameIcon.Cloud,
            WeatherPreset.Rain => GameIcon.Rain,
            WeatherPreset.Snow => GameIcon.Cloud,
            _ => GameIcon.Storm
        };

        // ── Toasts ───────────────────────────────────────────────────────────
        private void TrackForestryResult()
        {
            ForestryAreaSummary area = world.LastForestryArea;
            var current = (world.LastForestryAction, area.Applied, area.TileCount);
            if (current == lastForestryToast) return;
            lastForestryToast = current;
            if (world.LastForestryAction == ForestryActionResult.None) return;

            string text = ForestryActionText(world.LastForestryAction);
            if (!area.IsEmpty)
                text += $"  ({area.Applied}/{area.TileCount} csempe" + (area.TimberVolume > 0f ? $", {area.TimberVolume:F1} m³)" : ")");
            ShowToast(text, ForestryActionSucceeded(world.LastForestryAction) ? HudTheme.Good : HudTheme.Bad);
        }

        // ── Hover inspector ──────────────────────────────────────────────────
        private void DrawHoverInspector()
        {
            if (interaction.ActiveTool != TerrainEditTool.Inspect || activeButton != PointerButton.None) return;
            if (imgui.WantCaptureMouse) return;
            bool hasStand = world.TryGetForestStand(world.HoveredTileId, out ForestStand stand);
            bool planted = world.TryGetPlantationStatus(world.HoveredTileId, out var plot);
            bool road = world.IsRoadTile(world.HoveredTileId);
            if (!hasStand && !planted && !road) return;

            ImGui.BeginTooltip();
            if (road)
            {
                bool asphalt = world.GetRoadPaving(world.HoveredTileId) == RoadPaving.Asphalt;
                float condition = world.GetRoadCondition(world.HoveredTileId);
                HudTheme.IconText(asphalt ? GameIcon.Road : GameIcon.Macadam, asphalt ? "Aszfaltút" : "Makadámút", HudTheme.AmberAccent);
                HudTheme.KeyValue("Állapot", $"{condition:P0}" + (condition < 0.5f ? " · javítandó" : ""));
                ImGui.EndTooltip();
                return;
            }
            if (hasStand)
            {
                HudTheme.IconText(SpeciesIcon(stand.Species), Capitalize(ForestSpeciesName(stand.Species)), HudTheme.AmberAccent);
                HudTheme.KeyValue("Kor", $"{stand.AgeYears:F1} év");
                HudTheme.KeyValue("Egészség", $"{stand.Health:P0}");
                HudTheme.KeyValue("Faanyag", $"{ForestSystem.TimberCubicMetres(stand):F1} m³");
            }
            if (planted)
            {
                ImGui.SeparatorText("Erdőtelepítés");
                HudTheme.KeyValue("Terület", $"#{plot.Plantation.AreaId} · {Capitalize(ForestSpeciesName(plot.Plantation.Species))}");
                HudTheme.KeyValue("Soros telepítés", $"{plot.Living}/{plot.Plantation.InitialTrees} élő fa/csempe");
                HudTheme.KeyValue("Látható holtfa", $"{plot.Dead}");
                if (plot.Living > 0)
                {
                    HudTheme.KeyValue("Fény", $"{plot.Resources.Light:P0}");
                    HudTheme.KeyValue("Vízellátás", $"{plot.Resources.Water:P0}");
                    HudTheme.KeyValue("Növőtér", $"{plot.Resources.Space:P0}");
                }
            }
            var environment = world.Environment;
            if (environment != null && world.HoveredTileId < environment.CellCount)
                HudTheme.KeyValue("Víz szerinti növ.", $"{environment.Cell(world.HoveredTileId).GrowthFactor * 100:0}%");
            ImGui.EndTooltip();
        }

        private static GameIcon SpeciesIcon(ForestSpecies species) => species switch
        {
            ForestSpecies.Birch => GameIcon.Birch,
            ForestSpecies.Oak or ForestSpecies.SessileOak or ForestSpecies.TurkeyOak => GameIcon.Oak,
            ForestSpecies.Beech or ForestSpecies.Maple or ForestSpecies.Ash => GameIcon.Beech,
            _ => ForestSpeciesTraits.For(species).Conifer ? GameIcon.Spruce
                : ForestSpeciesTraits.For(species).Shrub ? GameIcon.Birch : GameIcon.Beech
        };

        private static string Capitalize(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text[1..];

        // ── Information windows ──────────────────────────────────────────────
        private bool BeginGameWindow(string title, ref bool open, NVec2 defaultPosition, float width)
        {
            if (!open) return false;
            ImGui.SetNextWindowPos(defaultPosition, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new NVec2(width, 0), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new NVec2(width * 0.8f, 0),
                new NVec2(Width, Math.Max(200f, Height - StatusBarHeight - defaultPosition.Y - 8f)));
            bool visible = ImGui.Begin(title, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);
            if (!visible) ImGui.End();
            return visible;
        }

        private void DrawVehiclesWindow()
        {
            if (!BeginGameWindow("Járművek", ref showVehicles, new NVec2(Width - 360, toolbarBottom + 10), 340)) return;

            if (HudTheme.LabeledIconButton("Új rönkszállító", GameIcon.TruckAdd, false, 30f,
                    "A kitermelési terület és a fűrészmalom közötti úton indul.")) SpawnTruck();
            if (world.Logistics != null) { ImGui.Spacing(); ImGui.TextWrapped(world.Logistics.Status); }
            ImGui.Separator();

            if (world.Vehicles.Count == 0) ImGui.TextDisabled("Még nincs jármű. Jelölj ki kitermelést, építs malmot és utat.");
            foreach (var vehicle in world.Vehicles)
            {
                (string status, NVec4 color) = VehicleStatus(vehicle);
                HudTheme.IconText(GameIcon.Truck, $"#{vehicle.Id}");
                ImGui.SameLine();
                ImGui.TextColored(color, status);
                float fill = vehicle.CargoCapacity > 0 ? (float)(vehicle.CargoAmount / vehicle.CargoCapacity) : 0f;
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new NVec4(0.67f, 0.47f, 0.26f, 1f));
                ImGui.ProgressBar(fill, new NVec2(-1, 14), $"{vehicle.CargoAmount:F1} / {vehicle.CargoCapacity:F0} m³");
                ImGui.PopStyleColor();
                if (vehicle.RoadRoute != null)
                {
                    string fuel = vehicle.FuelPer100Km > 0 ? $"{vehicle.FuelPer100Km:F0} l/100 km" : "–";
                    ImGui.TextDisabled($"{vehicle.CurrentSpeed * vehicle.MetresPerTile * 3.6:F0} km/h · {vehicle.Mass / 1000:F1} t · {fuel}");
                }
            }
            ImGui.End();
        }

        private static (string, NVec4) VehicleStatus(Vehicle vehicle)
        {
            if (vehicle.RouteBlocked) return ("Útkapcsolatra vár", HudTheme.Bad);
            return vehicle.TransportState switch
            {
                VehicleTransportState.Waiting => ("Faanyagra vár", HudTheme.AmberAccent),
                VehicleTransportState.Loading => ("Rakodik", HudTheme.Info),
                VehicleTransportState.Unloading => ("Lerakodik", HudTheme.Info),
                VehicleTransportState.Returning => ("Üres visszaút", HudTheme.Muted),
                _ => vehicle.CargoAmount > 0 ? ("Rakott menet", HudTheme.Good) : ("Üres menet", HudTheme.Muted)
            };
        }

        private void DrawForestryWindow()
        {
            if (!BeginGameWindow("Erdészet", ref showForestry, new NVec2(Width - 360, toolbarBottom + 280), 340)) return;

            ForestStatistics forest = world.ForestStatistics;
            ImGui.SeparatorText("Erdőállomány");
            HudTheme.KeyValue("Állományok", $"{forest.StandCount} ({forest.MatureStandCount} érett)");
            HudTheme.KeyValue("Faegyedek", $"{world.ForestTreeCount}");
            HudTheme.KeyValue("Élő törzskészlet (havi)", $"{forest.TotalBiomass * 100:F1} m³");
            HudTheme.KeyValue("Előző évi növedék", $"{world.LastAnnualForestGrowth:F2} m³");
            HudTheme.Meter("Átlagos egészség", forest.AverageHealth, $"{forest.AverageHealth:P0}",
                forest.AverageHealth > 0.6f ? null : HudTheme.Bad);

            ImGui.SeparatorText("Faanyag");
            HudTheme.KeyValue("Kitermelt készlet", $"{world.TimberStockpile:F1} m³");
            HudTheme.KeyValue("Leszállítva", $"{world.DeliveredTimber:F1} m³");
            if (world.Logistics != null)
            {
                HudTheme.KeyValue("Kitermelési ter.", $"{world.Logistics.Sites.Count} db · {world.Logistics.Remaining:F1} m³ hátra");
                if (world.Logistics.Mills.Count > 0) ImGui.SeparatorText("Fűrészmalmok");
                foreach (var mill in world.Logistics.Mills)
                    HudTheme.IconText(GameIcon.Sawmill, $"#{mill.TileId}: átvett {mill.Received:F1} m³ · feldolgozott {mill.Processed:F1} m³");
            }

            if (world.TryGetForestStand(world.HoveredTileId, out ForestStand stand))
            {
                ImGui.SeparatorText("Kijelölt csempe");
                HudTheme.IconText(SpeciesIcon(stand.Species),
                    $"{Capitalize(ForestSpeciesName(stand.Species))}, {stand.AgeYears:F1} év, {stand.Health:P0}, {ForestSystem.TimberCubicMetres(stand):F1} m³");
            }
            ImGui.End();
        }

        private void DrawEnvironmentWindow()
        {
            var environment = world.Environment;
            if (environment == null) return;
            if (!BeginGameWindow("Környezet", ref showEnvironment, new NVec2(Width - 360, toolbarBottom + 10), 340)) return;

            HudTheme.IconText(WeatherIcon(environment.Preset),
                $"{WeatherName(environment.Preset)} · még {Math.Max(0, environment.EventEnd - environment.Time):0} s", HudTheme.AmberAccent);
            HudTheme.KeyValue("Háttércsapadék", $"{environment.RainRate:0.0} mm/óra");
            HudTheme.KeyValue("Esemény", $"{environment.EventRain:0.00} / {environment.ExpectedEventRain:0.00} mm");
            HudTheme.KeyValue("Háttér-hőmérséklet", $"{environment.Temperature:0.0} °C");
            HudTheme.KeyValue("Szél", $"{environment.WindSpeed:0.0} m/s");
            HudTheme.KeyValue("Besugárzás", $"{environment.Radiation * 100:0}%");
            HudTheme.Meter("Gyökérzóna átlagos víztelítettsége", (float)environment.MeanSoil, $"{environment.MeanSoil * 100:0}%",
                new NVec4(0.36f, 0.62f, 0.86f, 1f));
            ImGui.TextDisabled($"1 erdőév = {environment.ForestYearSeconds / 60:0.#} játékperc");
            if (world.Soils != null && ImGui.CollapsingHeader("Talaj, víz és klíma térképe"))
                EcologyRasterView.Draw(world.Soils, environment, ref ecologyRasterLayer, ref ecologyRasterSelection);

            int id = world.HoveredTileId;
            if (id >= 0 && id < environment.CellCount)
            {
                var cell = environment.Cell(id);
                ImGui.SeparatorText($"Csempe {id}");
                if (world.Soils != null) HudTheme.KeyValue("Talaj", world.Soils.Profile(id).Name);
                var local = environment.ClimateAt(id);
                HudTheme.KeyValue("Helyi hőmérséklet", $"{local.Forcing.Temperature:0.0} °C");
                HudTheme.KeyValue("Helyi csapadék", $"{environment.RainRate * local.RainMultiplier:0.0} mm/óra");
                HudTheme.KeyValue("Páratartalom", $"{local.Forcing.Humidity:P0}");
                HudTheme.Meter("Gyökérzóna", (float)(cell.Soil / cell.Capacity), $"{cell.Soil:0.0} / {cell.Capacity:0} mm", new NVec4(0.36f, 0.62f, 0.86f, 1f));
                HudTheme.KeyValue("Felszíni víz", $"{cell.Surface:0.00} mm");
                HudTheme.KeyValue("Koronavíz", $"{cell.Canopy:0.00} mm");
                HudTheme.KeyValue("Fák vízfelvétele / igénye", $"{cell.UptakePerHour:0.000} / {cell.DemandPerHour:0.000} mm/óra");
                HudTheme.KeyValue("Aszálystressz", $"{cell.Drought * 100:0}%");
                HudTheme.KeyValue("Túlnedvesség", $"{cell.Waterlogging * 100:0}%");
                HudTheme.KeyValue("Növekedés (víz)", $"{cell.GrowthFactor * 100:0}%");
            }
            ImGui.End();
        }

        private void DrawManagementWindow()
        {
            bool projecting = showManagement && management.Project;
            world.SetManagementOverlay(projecting ? management.OverlayColours(world) : null);
            world.Graphics.HideTrees = projecting && management.ModelTrees;
            if (projecting && management.Current == ManagementView.Lens.Todo) DrawManagementMarkers();
            if (!BeginGameWindow("Erdőgazdálkodás", ref showManagement, new NVec2(20, toolbarBottom + 10), 640)) return;
            var request = management.Draw(world);
            ImGui.End();
            if (request.Kind == ManagementRequestKind.None) return;
            if (world.TryGetTileCenter(request.TileId, out var centre)) FocusOn(centre, Math.Max(zoom, 30));
            switch (request.Kind)
            {
                case ManagementRequestKind.Plant:
                    interaction.PlantingSpecies = request.Species;
                    SelectTool(TerrainEditTool.PlantForest);
                    ShowToast($"Erdőtelepítés: {ForestSpeciesName(request.Species)}. Húzással jelöld ki a területet.", HudTheme.Info);
                    break;
                case ManagementRequestKind.Harvest:
                    SelectTool(TerrainEditTool.HarvestForest);
                    ShowToast("Kitermelés: húzással jelöld ki a vágásterületet.", HudTheme.Info);
                    break;
            }
            RequestFrame();
        }

        // To-do markers above the tiles in the 3D view, thinned so they stay at least ~18 px apart.
        private void DrawManagementMarkers()
        {
            var survey = management.Survey;
            var grid = world.Soils?.Grid;
            if (survey == null || grid == null || survey.Length != grid.Count) return;
            float tilePixels = Math.Max(1f, Math.Min(world.TileWidth, world.TileHeight) * (float)zoom);
            int stride = Math.Max(1, (int)MathF.Ceiling(18f / tilePixels));
            float size = Math.Clamp(tilePixels * stride * 0.6f, 12f, 26f);
            var draw = ImGui.GetBackgroundDrawList();
            for (int column = 0; column < grid.Columns; column += stride)
                for (int row = 0; row < grid.Rows; row += stride)
                {
                    int best = -1;
                    for (int dx = 0; dx < stride && column + dx < grid.Columns; dx++)
                        for (int dy = 0; dy < stride && row + dy < grid.Rows; dy++)
                        {
                            int id = grid.TileId(column + dx, row + dy);
                            if (survey[id].Issue != ManagementIssue.None && (best < 0 || survey[id].Severity > survey[best].Severity)) best = id;
                        }
                    if (best < 0 || !world.TryGetTileCenter(best, out var centre)) continue;
                    Vector3 view = WorldToView(centre + new Vector3(0, 0, 0.5f), rotx, roty);
                    var screen = new NVec2((float)((view.X - screenX) * zoom), (float)(Height - (view.Y - screenY) * zoom));
                    if (screen.X < -size || screen.Y < -size || screen.X > Width + size || screen.Y > Height + size) continue;
                    var issue = survey[best].Issue;
                    float marker = size * (issue == ManagementIssue.Harvestable ? 0.75f : 0.85f + 0.08f * survey[best].Severity);
                    draw.AddCircleFilled(screen, marker * 0.55f, ImGui.ColorConvertFloat4ToU32(ManagementView.IssueColor(issue) with { W = 0.9f }));
                    draw.AddCircle(screen, marker * 0.55f, 0xc0ffffff, 0, 1.5f);
                    GameIcons.Draw(draw, ManagementView.Icon(issue), screen - new NVec2(marker * 0.38f), marker * 0.76f, 0xffffffff);
                }
        }

        private void DrawGraphicsWindow()
        {
            if (!BeginGameWindow("Grafika", ref showGraphics, new NVec2(Width * 0.5f - 180, toolbarBottom + 60), 360)) return;
            var g = world.Graphics;

            int quality = (int)g.Quality;
            if (ImGui.Combo("Minőség", ref quality, "Alacsony\0Közepes\0Magas\0")) g.Quality = (GraphicsQuality)quality;

            if (!g.Enhanced)
            {
                ImGui.TextColored(HudTheme.AmberAccent, "Klasszikus színalapú mód aktív.");
                if (ImGui.Button("Modern megjelenítés bekapcsolása")) g.Enhanced = true;
                ImGui.End();
                return;
            }

            ImGui.SeparatorText("Diorama hatások");
            ImGui.Checkbox("Diorama utófeldolgozás", ref g.Diorama);
            if (g.Diorama)
            {
                ImGui.Indent();
                ImGui.Checkbox("Tilt-shift mélységélesség", ref g.TiltShift);
                if (g.TiltShift) ImGui.SliderFloat("Életlenség", ref g.TiltShiftStrength, 0.1f, 1f, "%.2f");
                ImGui.Checkbox("Kontakt-árnyékolás (AO)", ref g.AmbientOcclusion);
                if (g.AmbientOcclusion)
                {
                    if (g.Quality == GraphicsQuality.Low) ImGui.TextDisabled("Alacsony minőségen kikapcsolva.");
                    else ImGui.SliderFloat("AO erőssége", ref g.AmbientOcclusionStrength, 0.2f, 1.5f, "%.2f");
                }
                ImGui.Checkbox("Színkorrekció (makró fotó)", ref g.ColorGrading);
                ImGui.Checkbox("Vignetta", ref g.Vignette);
                ImGui.Checkbox("Stúdió háttér", ref g.StudioBackdrop);
                ImGui.Unindent();
            }

            ImGui.SeparatorText("Fény és árnyék");
            ImGui.Checkbox("Napfény", ref g.Lighting);
            ImGui.SameLine(170);
            ImGui.Checkbox("Vetett árnyékok", ref g.Shadows);
            ImGui.SliderFloat("Nap iránya", ref g.SunAzimuth, 0, 360, "%.0f°");
            ImGui.SliderFloat("Nap magassága", ref g.SunElevation, 15, 80, "%.0f°");

            ImGui.SeparatorText("Részletek");
            ImGui.Checkbox("Textúrák", ref g.Textures);
            ImGui.SameLine(170);
            ImGui.Checkbox("Csemperács", ref g.ShowGrid);
            ImGui.Checkbox("Erdőtelepítések jelölése", ref g.ShowPlantations);
            ImGui.Checkbox("Járműkontúrok", ref g.VehicleOutlines);
            ImGui.SameLine(170);
            ImGui.Checkbox("Szarvaskontúrok", ref g.WildlifeOutlines);
            ImGui.Checkbox("Erdei szarvasok", ref g.Wildlife);
            ImGui.SameLine(170);
            if (ImGui.SmallButton("Szarvas megkeresése")) FocusOnDeer();

            ImGui.SeparatorText("Időjárás látványa");
            ImGui.Checkbox("Időjárás", ref g.Weather);
            if (g.Weather)
            {
                ImGui.SameLine(170);
                ImGui.Checkbox("Felhőzet", ref g.Clouds);
                ImGui.Checkbox("Villámlás", ref g.Lightning);
                ImGui.SameLine(170);
                ImGui.Checkbox("Talajköd", ref g.Fog);
                if (g.Fog) ImGui.SliderFloat("Köd sűrűsége", ref g.FogDensity, 0, 1, "%.2f");
            }

            ImGui.Separator();
            if (ImGui.SmallButton("Klasszikus színalapú mód")) g.Enhanced = false;
            ImGui.End();
        }

        // ── Developer tools: everything that is not part of playing the game ─
        private void DrawDeveloperWindow()
        {
            if (!BeginGameWindow("Fejlesztői eszközök", ref showDeveloper, new NVec2(12, toolbarBottom + 10), 380)) return;
            var g = world.Graphics;

            if (ImGui.CollapsingHeader("Teljesítmény", ImGuiTreeNodeFlags.DefaultOpen))
            {
                HudTheme.KeyValue("FPS", $"{ImGui.GetIO().Framerate:F0}");
                HudTheme.KeyValue("Képkocka", $"{performance.FrameMilliseconds:F1} ms");
                HudTheme.KeyValue("Szimuláció", $"{performance.SimulationMilliseconds:F2} ms · tick {simulationClock.Tick}");
                HudTheme.KeyValue("Renderelés", $"{performance.RenderMilliseconds:F1} ms · {performance.DrawCalls} draw");
                HudTheme.KeyValue("GC / képkocka", $"{performance.AllocatedBytes / 1024.0:F1} KiB");
                HudTheme.KeyValue("Chunkok", $"{world.VisibleChunkCount}/{world.TotalChunkCount} · erdő újraépítés {world.ForestChunkRebuilds}");
                HudTheme.KeyValue("Objektumok", $"{world.VehicleCount} jármű · {world.WildlifeCount} szarvas · {world.FishCount} hal");
            }

            if (ImGui.CollapsingHeader("Időjárás teszt"))
            {
                ImGui.Checkbox("Szimulált időjárás látványa", ref g.AutomaticWeather);
                ImGui.TextDisabled("Kikapcsolva a lenti képválasztás érvényes (csak látvány).");
                int preset = g.Preset == WeatherPreset.Storm ? 3 : Math.Min(2, (int)g.Preset);
                if (ImGui.Combo("Időjárási kép", ref preset, "Napsütés\0Borult\0Eső\0Vihar\0"))
                {
                    g.Preset = preset == 3 ? WeatherPreset.Storm : (WeatherPreset)preset;
                    g.AutomaticWeather = false;
                }
                if (HudTheme.LabeledIconButton("Villám most", GameIcon.Lightning, false, 28f))
                {
                    g.Lightning = true;
                    g.LightningRequest++;
                }

                ImGui.SeparatorText("Környezeti esemény (menthető)");
                ImGui.Combo("Esemény", ref environmentPreset, "Napos\0Borult\0Eső\0Vihar\0");
                ImGui.SliderInt("Csúcsintenzitás", ref environmentIntensity, 0, 60, "%d mm/óra");
                ImGui.SliderInt("Időtartam", ref environmentDuration, 20, 300, "%d s");
                ImGui.TextDisabled("Módosítja a vízkészletet, a mentés visszajátssza.");
                if (ImGui.Button("Esemény indítása"))
                {
                    world.QueueWeather(environmentPreset == 3 ? WeatherPreset.Storm : (WeatherPreset)environmentPreset,
                        environmentIntensity, environmentDuration);
                    g.AutomaticWeather = true;
                }
            }

            if (ImGui.CollapsingHeader("Világ"))
            {
                if (ImGui.Button("Új terep (véletlen seed)")) RegenerateTerrain();
                ImGui.SameLine();
                if (ImGui.Button("512×512 stresszteszt")) RegenerateTerrain(512);
                HudTheme.KeyValue("Térképméret", $"{currentMapTiles} × {currentMapTiles}");
                if (ImGui.Button("Szarvas megkeresése")) FocusOnDeer();
            }

            if (ImGui.CollapsingHeader("Kamera"))
            {
                HudTheme.KeyValue("Zoom", $"{zoom:F1} px/egység");
                HudTheme.KeyValue("Forgatás / dőlés", $"{roty:F0}° / {rotx:F0}°");
                if (ImGui.Button("Kamera alaphelyzet")) ResetCamera();
            }

            if (ImGui.CollapsingHeader("Megjelenítés hibakeresés"))
            {
                ImGui.Checkbox("Modern megjelenítés", ref g.Enhanced);
                ImGui.Checkbox("Csemperács", ref g.ShowGrid);
                ImGui.Checkbox("Diorama utófeldolgozás", ref g.Diorama);
                ImGui.TextDisabled("Arányok: lásd Rendering/DioramaScale.cs");
            }
            ImGui.End();
        }

        private void DrawHelpWindow()
        {
            if (!BeginGameWindow("Súgó", ref showHelp, new NVec2(Width * 0.5f - 210, toolbarBottom + 40), 420)) return;

            ImGui.SeparatorText("Első lépések");
            HudTheme.IconText(GameIcon.Harvest, "1. Jelölj ki kitermelési területet az erdőben.");
            HudTheme.IconText(GameIcon.Sawmill, "2. Építs fűrészmalmot 2×2 sík csempére.");
            HudTheme.IconText(GameIcon.Road, "3. Kösd össze úttal a területet és a malmot.");
            HudTheme.IconText(GameIcon.TruckAdd, "4. Indíts rönkszállítót.");
            HudTheme.IconText(GameIcon.Plant, "5. Telepíts új erdőt a letermelt helyekre.");

            ImGui.SeparatorText("Kamera");
            HudTheme.KeyValue("Bal húzás", "forgatás (Vizsgálat eszközzel)");
            HudTheme.KeyValue("Jobb húzás", "mozgatás");
            HudTheme.KeyValue("Görgő", "nagyítás");
            HudTheme.KeyValue("← / →", "90°-os forgatás");
            HudTheme.KeyValue("↑ / ↓", "dőlésszög");
            HudTheme.KeyValue("Home", "alaphelyzet");

            ImGui.SeparatorText("Gyorsbillentyűk");
            HudTheme.KeyValue("Q / W / E / R / T", "Megfigyel / Gondoz / Termel / Épít / Szállít");
            HudTheme.KeyValue("1 – 8", "eszközök közvetlenül");
            HudTheme.KeyValue("Esc", "vissza a Vizsgálat eszközhöz");
            HudTheme.KeyValue("Space", "szünet");
            HudTheme.KeyValue("V / F / K / M / G", "járművek / erdészet / környezet / erdőgazdálkodás / grafika");
            HudTheme.KeyValue("F12", "fejlesztői eszközök");
            HudTheme.KeyValue("Ctrl+S / Ctrl+L", "gyorsmentés / gyorsbetöltés");
            ImGui.End();
        }

        // ── Hotkeys ──────────────────────────────────────────────────────────
        private bool HandleHotkey(KeyboardKeyEventArgs e)
        {
            if (imgui != null && ImGui.GetIO().WantTextInput) return false;
            bool command = e.Modifiers.HasFlag(KeyModifiers.Control) || e.Modifiers.HasFlag(KeyModifiers.Super);
            if (command)
            {
                if (e.Key == Keys.S) { QuickSave(); return true; }
                if (e.Key == Keys.L) { QuickLoad(); return true; }
                return false;
            }

            switch (e.Key)
            {
                case Keys.Space: simulationClock.IsPaused = !simulationClock.IsPaused; return true;
                case Keys.Escape: verb = Verb.None; SelectTool(TerrainEditTool.Inspect); return true;
                case Keys.Q: ChooseVerb(Verb.Observe); return true;
                case Keys.W: ChooseVerb(Verb.Tend); return true;
                case Keys.R: ChooseVerb(Verb.Build); return true;
                case Keys.D1: SelectTool(TerrainEditTool.Inspect); return true;
                case Keys.D2: SelectTool(TerrainEditTool.Raise); return true;
                case Keys.D3: SelectTool(TerrainEditTool.Lower); return true;
                case Keys.D4: SelectTool(TerrainEditTool.Road); return true;
                case Keys.D5: SelectTool(TerrainEditTool.RoadRemove); return true;
                case Keys.D6: SelectTool(TerrainEditTool.PlantForest); return true;
                case Keys.D7: SelectTool(TerrainEditTool.HarvestForest); return true;
                case Keys.D8: SelectTool(TerrainEditTool.PlaceSawmill); return true;
                case Keys.D9: SelectTool(TerrainEditTool.RoadRepair); return true;
                case Keys.T: ChooseVerb(Verb.Transport); return true;
                case Keys.V: showVehicles = !showVehicles; return true;
                case Keys.F: showForestry = !showForestry; return true;
                case Keys.E: ChooseVerb(Verb.Produce); return true;
                case Keys.K: showEnvironment = !showEnvironment; return true;
                case Keys.M: showManagement = !showManagement; return true;
                case Keys.G: showGraphics = !showGraphics; return true;
                case Keys.F12: showDeveloper = !showDeveloper; return true;
                case Keys.F1: showHelp = !showHelp; return true;
                case Keys.Home: ResetCamera(); return true;
            }
            return false;
        }

        // ── Actions shared by toolbar, windows and hotkeys ───────────────────
        private void SpawnTruck()
        {
            world.QueueSpawnVehicle();
            ShowToast("Rönkszállító indítása kérve.", HudTheme.Info);
        }

        private void FocusOnDeer()
        {
            if (!world.TryGetWildlifePosition(out var deer))
            {
                ShowToast("Nincs látható szarvas a térképen.", HudTheme.Muted);
                return;
            }
            FocusOn(deer + new Vector3(0, 0, 1), Math.Max(zoom, 35));
        }

        private Vector3 cameraFocus;

        /// <summary>Centres the view on a world point at the given zoom.</summary>
        private void FocusOn(Vector3 point, float newZoom)
        {
            cameraFocus = point;
            rotationPivotActive = false;
            targetRotY = roty;
            zoom = targetZoom = newZoom;
            Vector3 view = WorldToView(point, rotx, roty);
            screenX = view.X - Width / (2.0 * zoom);
            screenY = view.Y - Height / (2.0 * zoom);
            pickMatricesReady = false;
        }

        private void MapSizeMenuItem(int tileCount)
        {
            if (ImGui.MenuItem($"{tileCount} × {tileCount}", "", currentMapTiles == tileCount))
                RegenerateTerrain(tileCount);
        }

        private string ToolName(TerrainEditTool tool) => tool switch
        {
            TerrainEditTool.Raise => "Emelés",
            TerrainEditTool.Lower => "Süllyesztés",
            TerrainEditTool.Road => "Útépítés",
            TerrainEditTool.RoadRemove => "Útbontás",
            TerrainEditTool.RoadRepair => "Útjavítás",
            TerrainEditTool.PlantForest => $"Ültetés ({ForestSpeciesName(interaction.PlantingSpecies)})",
            TerrainEditTool.HarvestForest => "Kitermelési terület",
            TerrainEditTool.PlaceSawmill => "Fűrészmalom építése",
            _ => "Vizsgálat"
        };

        private static string ForestSpeciesName(ForestSpecies species) =>
            species == ForestSpecies.None ? "nincs" : ForestSpeciesTraits.For(species).Name.ToLowerInvariant();

        private static bool ForestryActionSucceeded(ForestryActionResult result) =>
            result == ForestryActionResult.Designated || result == ForestryActionResult.Planted || result == ForestryActionResult.Harvested;

        private static string ForestryActionText(ForestryActionResult result) => result switch
        {
            ForestryActionResult.Planted => "Ültetés sikeres – a facsemete már látható.",
            ForestryActionResult.Designated => "Kitermelési terület kijelölve – a fák rakodáskor fogynak.",
            ForestryActionResult.Harvested => "Fakitermelés sikeres.",
            ForestryActionResult.TileOccupied => "Ültetés sikertelen: a csempe már foglalt.",
            ForestryActionResult.UnsuitableTerrain => "Ültetés sikertelen: víz, út vagy térképszél.",
            ForestryActionResult.NoForest => "Nincs kitermelhető fa ezen a csempén.",
            _ => "Érvénytelen erdészeti művelet."
        };
    }
}
