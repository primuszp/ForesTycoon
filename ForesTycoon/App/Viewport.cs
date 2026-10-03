using System;
using System.Drawing;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>
    /// Izometrikus OpenGL viewport – Transport Tycoon stílusú terepnézet.
    /// Bal egér: forgatás (csak roty, rotx rögzített az izometrikus szögön).
    /// Jobb egér: pan (eltolás).
    /// Görgő: zoom.
    /// </summary>
    sealed class Viewport : GameWindow
    {
        // ── Vetítési paraméterek ─────────────────────────────────────────────
        private const double Z_NEAR = -1000.0;
        private const double Z_FAR  = +1000.0;

        // ── Kamera állapot ───────────────────────────────────────────────────
        private readonly IsometricCamera camera = new IsometricCamera();
        private float zoom { get => camera.Zoom; set => camera.Zoom = value; }
        private float targetZoom { get => camera.TargetZoom; set => camera.TargetZoom = value; }
        private float rotx => camera.Tilt;
        private float roty { get => camera.Yaw; set => camera.Yaw = value; }
        private float targetRotY { get => camera.TargetYaw; set => camera.TargetYaw = value; }
        private double screenX { get => camera.ScreenX; set => camera.ScreenX = value; }
        private double screenY { get => camera.ScreenY; set => camera.ScreenY = value; }
        private double mouseX  = 0;
        private double mouseY  = 0;

        private int lastMouseX = 0;
        private int lastMouseY = 0;
        private bool rotationPivotActive;
        private bool rotationPivotKeepsScreenPoint;
        private Vector3 rotationPivotWorld;
        private int rotationPivotScreenX;
        private int rotationPivotScreenY;

        // Pan kezdőpont (jobb gomb lenyomásakor rögzítve)
        private double panStartX = 0;
        private double panStartY = 0;

        // ── OpenGL mátrixok (koordináta-visszaszámításhoz) ───────────────────
        private double[] projMatrix  = new double[16];
        private double[] modelMatrix = new double[16];
        private readonly double[] pickProjMatrix = new double[16];
        private readonly double[] pickModelMatrix = new double[16];
        private readonly int[] pickViewMatrix = new int[4];
        private Matrix4 projection = Matrix4.Identity;
        private Matrix4 modelView = Matrix4.Identity;
        private bool pickMatricesReady;
        private int[]    viewMatrix  = new int[4];
        private Vector3  worldPos    = new Vector3();

        // ── Játékállapot ─────────────────────────────────────────────────────
        private enum PointerButton { None, Left, Right, Middle }
        private PointerButton activeButton = PointerButton.None;
        private bool mouseDownCapturedByImGui;
        private bool         nodeHovered  = false;
        private GameWorld    world        = null;
        private WorldInteractionController interaction;

        private bool         isLoaded     = false;
        private bool glResourcesDisposed;
        private ImGuiController imgui;
        private readonly FrameClock frameClock = new FrameClock();
        private readonly SimulationFrameRunner simulation = new SimulationFrameRunner(30.0);
        private FixedStepClock simulationClock => simulation.Clock;
        private readonly FramePerformanceMonitor performance = new FramePerformanceMonitor();
        private ulong frameIndex;
        private int currentMapTiles = 64;
        private string persistenceStatus = "";
        private const int MaximumVisibleTiles = 64;
        private bool frameInProgress;
        private readonly ulong? smokeTestFrameLimit;

        private int Width => Math.Max(1, ClientSize.X);
        private int Height => Math.Max(1, ClientSize.Y);
        private float DpiScale => Width > 0 ? Math.Max(1f, FramebufferSize.X / (float)Width) : 1f;
        private int FramebufferWidth => Math.Max(1, FramebufferSize.X);
        private int FramebufferHeight => Math.Max(1, FramebufferSize.Y);

        private static int MapMouseButton(MouseButton b)
        {
            if (b == MouseButton.Right) return 1;
            if (b == MouseButton.Middle) return 2;
            return 0; // Left / egyéb
        }

        private static PointerButton MapPointerButton(MouseButton button) => button switch
        {
            MouseButton.Left => PointerButton.Left,
            MouseButton.Right => PointerButton.Right,
            MouseButton.Middle => PointerButton.Middle,
            _ => PointerButton.None
        };

        private static float SnapRotation(float angle)
        {
            return (float)(Math.Round((angle - 45.0) / 90.0) * 90.0 + 45.0);
        }

        private static Vector3 WorldToView(Vector3 point, float tiltDegrees, float yawDegrees)
        {
            double rz = yawDegrees * Math.PI / 180.0;
            double rx = tiltDegrees * Math.PI / 180.0;
            double cosZ = Math.Cos(rz), sinZ = Math.Sin(rz);
            double cosX = Math.Cos(rx), sinX = Math.Sin(rx);

            double x1 = cosZ * point.X - sinZ * point.Y;
            double y1 = sinZ * point.X + cosZ * point.Y;
            double z1 = point.Z;

            return new Vector3(
                (float)x1,
                (float)(cosX * y1 - sinX * z1),
                (float)(sinX * y1 + cosX * z1));
        }

        private bool IsFullTerrainVisible()
        {
            if (world == null) return true;

            world.GetWorldBounds(out Vector3 min, out Vector3 max);
            Vector3[] corners =
            {
                new Vector3(min.X, min.Y, min.Z),
                new Vector3(min.X, min.Y, max.Z),
                new Vector3(min.X, max.Y, min.Z),
                new Vector3(min.X, max.Y, max.Z),
                new Vector3(max.X, min.Y, min.Z),
                new Vector3(max.X, min.Y, max.Z),
                new Vector3(max.X, max.Y, min.Z),
                new Vector3(max.X, max.Y, max.Z)
            };

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 view = WorldToView(corners[i], rotx, roty);
                minX = Math.Min(minX, view.X);
                minY = Math.Min(minY, view.Y);
                maxX = Math.Max(maxX, view.X);
                maxY = Math.Max(maxY, view.Y);
            }

            double visibleMinX = screenX;
            double visibleMinY = screenY;
            double visibleMaxX = screenX + Width / zoom;
            double visibleMaxY = screenY + Height / zoom;
            const double margin = 2.0;

            return minX >= visibleMinX - margin
                && maxX <= visibleMaxX + margin
                && minY >= visibleMinY - margin
                && maxY <= visibleMaxY + margin;
        }

        private void BeginRotationPivot(int screenPixelX, int screenPixelY, Vector3 pivotWorld)
        {
            rotationPivotActive = true;
            rotationPivotKeepsScreenPoint = !IsFullTerrainVisible();
            rotationPivotWorld = pivotWorld;
            rotationPivotScreenX = screenPixelX;
            rotationPivotScreenY = screenPixelY;
        }

        private void SetRotationTargetAroundPivot(float newYaw, int screenPixelX, int screenPixelY, Vector3 pivotWorld)
        {
            if (!rotationPivotActive)
                BeginRotationPivot(screenPixelX, screenPixelY, pivotWorld);

            targetRotY = newYaw;
        }

        private void ApplyRotationPivotCompensation()
        {
            if (!rotationPivotActive || !rotationPivotKeepsScreenPoint) return;

            Vector3 pivotView = WorldToView(rotationPivotWorld, rotx, roty);
            screenX = pivotView.X - rotationPivotScreenX / zoom;
            screenY = pivotView.Y - (Height - rotationPivotScreenY) / zoom;
        }

        private void EndRotationPivotIfSettled()
        {
            if (activeButton == PointerButton.Left) return;
            if (Math.Abs(targetRotY - roty) <= 0.01f)
            {
                rotationPivotActive = false;
                rotationPivotKeepsScreenPoint = false;
            }
        }

        private void ClearRotationPivot()
        {
            rotationPivotActive = false;
            rotationPivotKeepsScreenPoint = false;
        }

        // ── Háttérszín (referenciakép alapján) ──────────────────────────────
        private static readonly Color BG_COLOR = Color.FromArgb(44, 53, 64);

        // ────────────────────────────────────────────────────────────────────
        public Viewport(ulong? smokeTestFrameLimit = null) : base(
            new GameWindowSettings
            {
                UpdateFrequency = 0
            },
            new NativeWindowSettings
            {
                Title = "ForesTycoon",
                ClientSize = new Vector2i(1280, 720),
                // Asking GLFW for the primary monitor during construction can return
                // null on macOS background launches. Start windowed; users can maximize safely.
                WindowState = WindowState.Normal,
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                NumberOfSamples = 4,
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible
            })
        {
            this.smokeTestFrameLimit = smokeTestFrameLimit;
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            try
            {
                Context.MakeCurrent();
                Context.SwapInterval = 0;   // vsync ki (OpenTK 3 VSync=false megfelelője)

                // OpenGL alapbeállítások
                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.CullFace);         // Mindkét oldal látszódjon (skirt)
                GL.LineWidth(1.0f);
                RenderDevice.Initialize();

                world = new GameWorld(TerrainSettings.Default);
                interaction = new WorldInteractionController(world);
                imgui = new ImGuiController();
                isLoaded = true;
                frameClock.Reset();
                simulationClock.Reset();
                SetupViewport();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Viewport initialization failed:\n" + ex);
                throw;
            }
        }

        // ── Vetítési mátrix beállítása ───────────────────────────────────────
        private void SetupViewport()
        {
            if (!isLoaded) return;

            int fbWidth = FramebufferWidth;
            int fbHeight = FramebufferHeight;
            float wc = (Width  - 1.0f) / zoom;
            float hc = (Height - 1.0f) / zoom;
            float pc = 0.5f / zoom;

            GL.Viewport(0, 0, fbWidth, fbHeight);
            projection = Matrix4.CreateOrthographicOffCenter(
                (float)(screenX - pc), (float)(screenX + wc + pc),
                (float)(screenY - pc), (float)(screenY + hc + pc),
                (float)Z_NEAR, (float)Z_FAR);
        }

        // ── Rajzolás ─────────────────────────────────────────────────────────
        private void Render()
        {
            GL.ClearColor(BG_COLOR);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            modelView = camera.CreateViewMatrix();
            RenderDevice.SetCamera(modelView * projection);
            CapturePickMatrices();

            frameIndex++;

            bool isTerrainEditTool = interaction.ActiveTool == TerrainEditTool.Raise || interaction.ActiveTool == TerrainEditTool.Lower;
            bool isRotating = interaction.ActiveTool == TerrainEditTool.Inspect && activeButton == PointerButton.Left;
            bool showTileHighlight = !isTerrainEditTool && !isRotating;
            float markerPixelRadius = Math.Max(4.0f, Math.Min(zoom * 0.55f, 9.0f));
            float nodeMarkerRadius = markerPixelRadius / Math.Max(zoom, 0.001f);

            RenderContext renderContext = new RenderContext(
                frameClock.TotalTimeSeconds,
                frameClock.DeltaTimeSeconds,
                frameIndex,
                simulationClock.SimulationTimeSeconds,
                simulationClock.Tick,
                simulationClock.InterpolationAlpha,
                isTerrainEditTool,
                showTileHighlight,
                nodeMarkerRadius,
                rotx,
                roty,
                screenX,
                screenY,
                screenX + Width / zoom,
                screenY + Height / zoom,
                zoom * FramebufferWidth / Math.Max(1, Width));

            world.Draw(renderContext);

            DrawImGui();

            if (smokeTestFrameLimit.HasValue && frameIndex == 10)
                ValidateSmokeFramebuffer();

            SwapBuffers();
        }

        private void ValidateSmokeFramebuffer()
        {
            int width = Math.Min(64, FramebufferWidth);
            int height = Math.Min(64, FramebufferHeight);
            byte[] pixels = new byte[width * height * 4];
            GL.ReadPixels((FramebufferWidth - width) / 2, (FramebufferHeight - height) / 2,
                width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (Math.Abs(pixels[i] - BG_COLOR.R) > 2
                    || Math.Abs(pixels[i + 1] - BG_COLOR.G) > 2
                    || Math.Abs(pixels[i + 2] - BG_COLOR.B) > 2)
                    return;
            }

            throw new InvalidOperationException("OpenGL core smoke test rendered only the clear color in the center sample.");
        }

        private void RequestFrame()
        {
            // GameWindow renders continuously. Kept as an intent marker for event handlers.
        }

        private void RunFrame()
        {
            if (frameInProgress || !isLoaded || IsExiting || Width <= 0 || Height <= 0)
                return;

            frameInProgress = true;
            try
            {
                performance.BeginFrame();
                Context.MakeCurrent();
                frameClock.Tick();
                RefreshPointerHover();
                UpdateCameraFrame();
                performance.BeginSimulation();
                SimulationFrameResult result = simulation.Advance(
                    frameClock.DeltaTimeSeconds, world.ExecutePendingCommands, world.Update);
                performance.EndSimulation(result.Commands, result.Ticks);
                SetupViewport();
                performance.BeginRender();
                Render();
                performance.EndRender();
            }
            finally
            {
                frameInProgress = false;
            }
        }

        private void RefreshPointerHover()
        {
            if (world == null || mouseDownCapturedByImGui || (imgui != null && imgui.WantCaptureMouse)) return;

            int x = (int)Math.Round(MousePosition.X);
            int y = (int)Math.Round(MousePosition.Y);
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;

            UpdateHover(x, y);
            interaction.UpdateGesture();
        }

        private void UpdateCameraFrame()
        {
            ClampZoomToTerrainWindow();
            camera.Update(frameClock.DeltaTimeSeconds, mouseX, mouseY);
            ApplyRotationPivotCompensation();
            EndRotationPivotIfSettled();
        }

        private void ClampZoomToTerrainWindow()
        {
            if (world == null) return;

            float minimumZoom = MapViewConstraints.MinimumZoomForTileWindow(
                Math.Max(1, Width), Math.Max(1, Height),
                world.TileWidth, world.TileHeight, MaximumVisibleTiles);
            camera.ClampMinimumZoom(minimumZoom);
        }

        private void DrawImGui()
        {
            if (imgui == null) return;

            float scale = DpiScale;
            imgui.Update(Width, Height, FramebufferWidth, FramebufferHeight, new NVec2(scale, scale), frameClock.DeltaTimeSeconds);

            DrawMainMenu();
            DrawToolbar();
            DrawStatusPanel();
            DrawEnvironmentPanel();

            imgui.Render();
        }

        // ── Felső menüsor (Transport Tycoon stílus) ──────────────────────────
        private void DrawMainMenu()
        {
            if (!ImGui.BeginMainMenuBar()) return;

            if (ImGui.BeginMenu("Fájl"))
            {
                if (ImGui.MenuItem("Gyorsmentés", "Ctrl+S")) QuickSave();
                if (ImGui.MenuItem("Gyorsbetöltés", "Ctrl+L")) QuickLoad();
                ImGui.Separator();
                if (ImGui.MenuItem("Új terep (seed)")) RegenerateTerrain();
                if (ImGui.BeginMenu("Új nagy erdős térkép"))
                {
                    if(ImGui.MenuItem("Fenyves és lombos erdő")) RegenerateTerrain(forestPattern: ForestPattern.LargeMixed);
                    if(ImGui.MenuItem("Nagy fenyves")) RegenerateTerrain(forestPattern: ForestPattern.LargeSpruce);
                    if(ImGui.MenuItem("Nagy lombos erdő")) RegenerateTerrain(forestPattern: ForestPattern.LargeBroadleaf);
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Térképméret"))
                {
                    MapSizeMenuItem(64);
                    MapSizeMenuItem(128);
                    MapSizeMenuItem(256);
                    MapSizeMenuItem(512, experimental: true);
                    ImGui.EndMenu();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Kilépés"))
                    Close();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Nézet"))
            {
                if (ImGui.MenuItem("Kamera alaphelyzet")) ResetCamera();
                ImGui.Separator();
                ImGui.Checkbox("Új grafikai megjelenítés", ref world.Graphics.Enhanced);
                ImGui.Checkbox("Erdei szarvasok", ref world.Graphics.Wildlife);
                if (ImGui.MenuItem("Szarvas megkeresése", "", false, world.WildlifeCount > 0) && world.TryGetWildlifePosition(out var deer))
                {
                    rotationPivotActive = false;
                    targetRotY = roty;
                    zoom = targetZoom = Math.Max(zoom, 35);
                    Vector3 view = WorldToView(deer + new Vector3(0,0,1), rotx, roty);
                    screenX = view.X - Width / (2.0 * zoom);
                    screenY = view.Y - Height / (2.0 * zoom);
                    pickMatricesReady = false;
                    RequestFrame();
                }
                if (ImGui.MenuItem("Eredeti színalapú mód", "", !world.Graphics.Enhanced)) world.Graphics.Enhanced = false;
                if (world.Graphics.Enhanced)
                {
                    ImGui.Checkbox("Textúrázás", ref world.Graphics.Textures);
                    ImGui.Checkbox("Járműkontúrok", ref world.Graphics.VehicleOutlines);
                    ImGui.Checkbox("Szarvaskontúrok", ref world.Graphics.WildlifeOutlines);
                    ImGui.Checkbox("Napfény", ref world.Graphics.Lighting);
                    ImGui.Checkbox("Vetett árnyékok", ref world.Graphics.Shadows);
                    int quality = (int)world.Graphics.Quality;
                    if (ImGui.Combo("Effektek minősége", ref quality, "Alacsony\0Közepes\0Magas\0"))
                        world.Graphics.Quality = (GraphicsQuality)quality;
                    ImGui.Checkbox("Csemperács", ref world.Graphics.ShowGrid);
                    ImGui.SliderFloat("Nap iránya", ref world.Graphics.SunAzimuth, 0, 360, "%.0f°");
                    ImGui.SliderFloat("Nap magassága", ref world.Graphics.SunElevation, 15, 80, "%.0f°");
                    ImGui.Separator();
                    ImGui.Checkbox("Időjárás", ref world.Graphics.Weather);
                    if (world.Graphics.Weather)
                    {
                        ImGui.Checkbox("Szimulált időjárás látványa", ref world.Graphics.AutomaticWeather);
                        ImGui.TextDisabled("Az alábbi képválasztás látványteszt.");
                        int preset = world.Graphics.Preset == WeatherPreset.Storm ? 3 : Math.Min(2, (int)world.Graphics.Preset);
                        if (ImGui.Combo("Időjárási kép", ref preset, "Napsütés\0Borult\0Eső\0Vihar\0"))
                        { world.Graphics.Preset = preset == 3 ? WeatherPreset.Storm : (WeatherPreset)preset; world.Graphics.AutomaticWeather = false; }
                        ImGui.Checkbox("Felhőzet", ref world.Graphics.Clouds);
                        ImGui.Checkbox("Villámlás", ref world.Graphics.Lightning);
                        if (ImGui.Button("Villám most"))
                        { world.Graphics.Lightning = true; world.Graphics.LightningRequest++; }
                        ImGui.Checkbox("Talajköd", ref world.Graphics.Fog);
                        if (world.Graphics.Fog) ImGui.SliderFloat("Köd sűrűsége", ref world.Graphics.FogDensity, 0, 1, "%.2f");
                        ImGui.TextDisabled("A talajköd napsütésben is bekapcsolható.");
                        ImGui.TextDisabled("Eső után a talaj fokozatosan szárad.");
                    }
                }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Játék"))
            {
                if (ImGui.MenuItem("Szünet", "Space", simulationClock.IsPaused))
                    simulationClock.IsPaused = !simulationClock.IsPaused;
                ImGui.Separator();
                SimulationSpeedMenuItem("1x", 1.0);
                SimulationSpeedMenuItem("2x", 2.0);
                SimulationSpeedMenuItem("4x", 4.0);
                ImGui.Separator();
                ImGui.Checkbox("Környezeti panel",ref showEnvironment);
                if (ImGui.MenuItem("Rönkszállító indítása")) world.QueueSpawnVehicle();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Eszközök"))
            {
                ToolMenuItem("Vizsgálat", TerrainEditTool.Inspect);
                ToolMenuItem("Emelés", TerrainEditTool.Raise);
                ToolMenuItem("Süllyesztés", TerrainEditTool.Lower);
                ToolMenuItem("Út építés", TerrainEditTool.Road);
                ToolMenuItem("Út bontás", TerrainEditTool.RoadRemove);
                ImGui.Separator();
                ToolMenuItem("Erdő ültetés", TerrainEditTool.PlantForest);
                ToolMenuItem("Kitermelési terület", TerrainEditTool.HarvestForest);
                ToolMenuItem("Fűrészmalom elhelyezése",TerrainEditTool.PlaceSawmill);
                if (ImGui.BeginMenu("Ültetett fafaj"))
                {
                    SpeciesMenuItem("Lucfenyő", ForestSpecies.Spruce);
                    SpeciesMenuItem("Nyír", ForestSpecies.Birch);
                    SpeciesMenuItem("Tölgy", ForestSpecies.Oak);
                    SpeciesMenuItem("Bükk", ForestSpecies.Beech);
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }

        // ── Eszköztár (terepalakítás) ────────────────────────────────────────
        private void DrawToolbar()
        {
            ImGui.SetNextWindowPos(new NVec2(8, 30), ImGuiCond.Always);
            ImGui.Begin("##toolbar",
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize);

            ToolButton("Vizsg.", TerrainEditTool.Inspect); ImGui.SameLine();
            ToolButton("Emel", TerrainEditTool.Raise); ImGui.SameLine();
            ToolButton("Süly.", TerrainEditTool.Lower); ImGui.SameLine();
            ToolButton("Út", TerrainEditTool.Road); ImGui.SameLine();
            ToolButton("Bontás", TerrainEditTool.RoadRemove); ImGui.SameLine();
            ToolButton("Ültet", TerrainEditTool.PlantForest); ImGui.SameLine();
            ToolButton("Kitermelés", TerrainEditTool.HarvestForest); ImGui.SameLine();
            ToolButton("Fűrészmalom",TerrainEditTool.PlaceSawmill);
            if(ImGui.Button("Rönkszállító indítása"))world.QueueSpawnVehicle();
            if(world.Logistics!=null) {
                ImGui.TextWrapped(world.Logistics.Status);
                ImGui.Text($"Kitermelési területek: {world.Logistics.Sites.Count} · hátralévő {world.Logistics.Remaining:F1} m³");
                foreach(var mill in world.Logistics.Mills)ImGui.Text($"Malom #{mill.TileId}: átvett {mill.Received:F1} m³ · feldolgozott {mill.Processed:F1} m³");
            }
            if(interaction.ActiveTool==TerrainEditTool.PlaceSawmill)ImGui.TextWrapped("Kattints 2×2 sík, üres, száraz csempére. A malom mellé út szükséges.");
            if(interaction.ActiveTool==TerrainEditTool.HarvestForest)ImGui.TextWrapped("Húzással jelöld ki az erdőterületet. A fák rakodás közben, fokozatosan fogynak.");

            if (interaction.ActiveTool == TerrainEditTool.PlantForest)
            {
                SpeciesButton("Luc", ForestSpecies.Spruce); ImGui.SameLine();
                SpeciesButton("Nyír", ForestSpecies.Birch); ImGui.SameLine();
                SpeciesButton("Tölgy", ForestSpecies.Oak); ImGui.SameLine();
                SpeciesButton("Bükk", ForestSpecies.Beech);
            }

            if (interaction.ActiveTool == TerrainEditTool.Raise || interaction.ActiveTool == TerrainEditTool.Lower)
            {
                ImGui.PushItemWidth(150);
                int brushSize = interaction.BrushSize;
                int brushStrength = interaction.BrushStrength;
                if (ImGui.SliderInt("Méret", ref brushSize, 1, 5)) interaction.BrushSize = brushSize;
                if (ImGui.SliderInt("Erő", ref brushStrength, 1, 5)) interaction.BrushStrength = brushStrength;
                ImGui.PopItemWidth();
            }

            ImGui.End();
        }

        private int environmentPreset, environmentIntensity=12, environmentDuration=90;
        private bool showEnvironment=true;
        private void DrawEnvironmentPanel()
        {
            var environment=world.Environment;
            if(!showEnvironment||environment==null)return;
            ImGui.SetNextWindowPos(new NVec2(Math.Max(8,Width-345),30),ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new NVec2(330,0),ImGuiCond.FirstUseEver);
            if(ImGui.Begin("Környezet 1.0",ref showEnvironment,ImGuiWindowFlags.AlwaysAutoResize)) {
                string name=environment.Preset switch {WeatherPreset.Sunny=>"Napos",WeatherPreset.Cloudy=>"Borult",WeatherPreset.Rain=>"Eső",_=>"Vihar"};
                ImGui.Text($"{name} · hátralévő: {environment.EventEnd-environment.Time:0} s");
                ImGui.Text($"Csapadék: {environment.RainRate:0.0} mm/környezeti óra");
                ImGui.Text($"Esemény: {environment.EventRain:0.00} / {environment.ExpectedEventRain:0.00} mm");
                ImGui.Text($"Hőmérséklet: {environment.Temperature:0.0} °C · szél: {environment.WindSpeed:0.0} m/s");
                ImGui.Text($"Gyökérzóna átlaga: {environment.MeanSoil*100:0}% · év: {1+(int)(environment.Time/EnvironmentSystem.SecondsPerForestYear)}");
                ImGui.TextDisabled("1 erdőév: 20 perc · 1 játékperc: 1 vízóra");
                int id=world.HoveredTileId;
                if(id>=0&&id<environment.CellCount){var cell=environment.Cell(id);
                    ImGui.Separator();ImGui.Text($"Csempe {id}: gyökérzóna {cell.Soil:0.0} / 180 mm");
                    ImGui.Text($"Felszíni víz: {cell.Surface:0.00} mm · korona: {cell.Canopy:0.00} mm");
                    ImGui.Text($"Aszálystressz: {cell.Drought*100:0}% · túl nedves: {cell.Waterlogging*100:0}%");
                    ImGui.Text($"Víz szerinti növekedés: {cell.GrowthFactor*100:0}%");}
                if(ImGui.CollapsingHeader("Időjárási esemény indítása")){
                    ImGui.Combo("Esemény",ref environmentPreset,"Napos\0Borult\0Eső\0Vihar\0");
                    ImGui.SliderInt("Csúcsintenzitás",ref environmentIntensity,0,60,"%d mm/óra");
                    ImGui.SliderInt("Időtartam",ref environmentDuration,20,300,"%d s");
                    ImGui.TextDisabled("Ez a vízkészletet is módosítja és menthető.");
                    if(ImGui.Button("Esemény indítása")){
                        world.QueueWeather(environmentPreset==3?WeatherPreset.Storm:(WeatherPreset)environmentPreset,environmentIntensity,environmentDuration);
                        world.Graphics.AutomaticWeather=true;
                    }
                }
            }
            ImGui.End();
        }

        private void DrawStatusPanel()
        {
            ImGui.SetNextWindowPos(new NVec2(8, Math.Max(80, Height - 220)), ImGuiCond.Always);
            ImGui.Begin("##status",
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize);

            ImGui.Text($"Eszköz: {ToolName(interaction.ActiveTool)}");
            if (WorldInteractionController.IsRoadTool(interaction.ActiveTool))
            {
                ImGui.Text($"Út-csempék: {world.RoadCount}");
                if (interaction.IsRoadDragging)
                    ImGui.Text($"Hossz: {world.RoadPreviewCount}");
            }
            ImGui.Text($"{ImGui.GetIO().Framerate:F0} FPS");
            ImGui.Text($"Tick: {simulationClock.Tick}  {(simulationClock.IsPaused ? "Szünet" : $"{simulationClock.Speed:0}x")}");
            ImGui.Text($"Járművek: {world.VehicleCount}");
            ImGui.Text($"Erdei szarvasok: {world.WildlifeCount} · halak: {world.FishCount}");
            for (int i = 0; i < Math.Min(8, world.Vehicles.Count); i++)
            {
                var vehicle = world.Vehicles[i];
                string status = vehicle.TransportState switch {
                    VehicleTransportState.Waiting => "Faanyagra vár",
                    VehicleTransportState.Loading => "Rakodik",
                    VehicleTransportState.Unloading => "Lerakodik",
                    VehicleTransportState.Returning => "Üres visszaút",
                    _ => vehicle.CargoAmount > 0 ? "Rakott menet" : "Üres menet" };
                if(vehicle.RouteBlocked)status="Útkapcsolatra vár";
                ImGui.Text($"#{vehicle.Id}: {status} – {vehicle.CargoAmount:F1}/{vehicle.CargoCapacity:F0} m³");
            }
            ForestStatistics forest = world.ForestStatistics;
            ImGui.Text($"Erdő: {forest.StandCount} állomány, {forest.MatureStandCount} érett");
            ImGui.Text($"Biomassza: {forest.TotalBiomass:F1}  Egészség: {forest.AverageHealth:P0}");
            ImGui.Text($"Kitermelt faanyag: {world.TimberStockpile:F1} m³");
            ImGui.Text($"Leszállított faanyag: {world.DeliveredTimber:F1} m³");
            if (world.TryGetForestStand(world.HoveredTileId, out ForestStand stand))
                ImGui.Text($"Csempe: {ForestSpeciesName(stand.Species)}, {stand.AgeYears:F1} év, {stand.Health:P0}, {ForestSystem.TimberCubicMetres(stand):F1} m³");
            if (WorldInteractionController.IsForestryTool(interaction.ActiveTool) && interaction.IsForestryDragging)
                ImGui.Text($"Terület: {world.ForestryPreviewCount} csempe");
            ForestryAreaSummary area = world.LastForestryArea;
            if (!area.IsEmpty)
                ImGui.Text($"Terület művelet: {area.Applied}/{area.TileCount} csempe"
                    + (area.TimberVolume > 0f ? $", {area.TimberVolume:F1} m³" : string.Empty));
            if (world.LastForestryAction != ForestryActionResult.None)
                ImGui.TextColored(ForestryActionSucceeded(world.LastForestryAction)
                        ? new NVec4(0.55f, 0.90f, 0.45f, 1f)
                        : new NVec4(1.00f, 0.42f, 0.35f, 1f),
                    ForestryActionText(world.LastForestryAction));
            ImGui.Text($"Chunk: {world.VisibleChunkCount}/{world.TotalChunkCount}");
            ImGui.Text($"Forest rebuild/frame: {world.ForestChunkRebuilds}");
            ImGui.Text($"Frame: {performance.FrameMilliseconds:F1} ms  Sim: {performance.SimulationMilliseconds:F2} ms");
            ImGui.Text($"Render: {performance.RenderMilliseconds:F1} ms  Draw: {performance.DrawCalls}");
            ImGui.Text($"GC/frame: {performance.AllocatedBytes / 1024.0:F1} KiB");
            if (!string.IsNullOrEmpty(persistenceStatus)) ImGui.Text(persistenceStatus);

            ImGui.End();
        }

        private void ToolButton(string label, TerrainEditTool tool)
        {
            bool active = interaction.ActiveTool == tool;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new NVec4(0.34f, 0.48f, 0.28f, 1f));
            if (ImGui.Button(label, new NVec2(Math.Max(54,ImGui.CalcTextSize(label).X+18), 40))) SelectTool(tool);
            if (active) ImGui.PopStyleColor();
        }

        private void ToolMenuItem(string label, TerrainEditTool tool)
        {
            if (ImGui.MenuItem(label, "", interaction.ActiveTool == tool)) SelectTool(tool);
        }

        private void SpeciesButton(string label, ForestSpecies species)
        {
            bool active = interaction.PlantingSpecies == species;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new NVec4(0.30f, 0.52f, 0.25f, 1f));
            if (ImGui.Button(label)) interaction.PlantingSpecies = species;
            if (active) ImGui.PopStyleColor();
        }

        private void SpeciesMenuItem(string label, ForestSpecies species)
        {
            if (ImGui.MenuItem(label, "", interaction.PlantingSpecies == species))
                interaction.PlantingSpecies = species;
        }

        private void SelectTool(TerrainEditTool tool)
        {
            interaction.SelectTool(tool);
            world.Graphics.SawmillPreview=tool==TerrainEditTool.PlaceSawmill;
            RequestFrame();
        }

        private string ToolName(TerrainEditTool tool) => tool switch
        {
            TerrainEditTool.Raise => "Emelés",
            TerrainEditTool.Lower => "Süllyesztés",
            TerrainEditTool.Road => "Út építés",
            TerrainEditTool.RoadRemove => "Út bontás",
            TerrainEditTool.PlantForest => $"Ültetés ({ForestSpeciesName(interaction.PlantingSpecies)})",
            TerrainEditTool.HarvestForest => "Kitermelési terület",
            TerrainEditTool.PlaceSawmill => "Fűrészmalom elhelyezése",
            _ => "Vizsgálat"
        };

        private static string ForestSpeciesName(ForestSpecies species) => species switch
        {
            ForestSpecies.Spruce => "lucfenyő",
            ForestSpecies.Birch => "nyír",
            ForestSpecies.Oak => "tölgy",
            ForestSpecies.Beech => "bükk",
            _ => "nincs"
        };

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

        private void ResetCamera()
        {
            camera.Reset();
            RequestFrame();
        }

        // ── OpenGL mátrixok kiolvasása (egér → világ koordináta) ────────────
        private void CapturePickMatrices()
        {
            CopyMatrix(projection, projMatrix);
            CopyMatrix(modelView, modelMatrix);
            viewMatrix[0] = 0;
            viewMatrix[1] = 0;
            viewMatrix[2] = FramebufferWidth;
            viewMatrix[3] = FramebufferHeight;
            Array.Copy(projMatrix, pickProjMatrix, projMatrix.Length);
            Array.Copy(modelMatrix, pickModelMatrix, modelMatrix.Length);
            Array.Copy(viewMatrix, pickViewMatrix, viewMatrix.Length);
            pickMatricesReady = pickViewMatrix[2] > 0 && pickViewMatrix[3] > 0;
        }

        private static void CopyMatrix(Matrix4 matrix, double[] destination)
        {
            destination[0] = matrix.M11; destination[1] = matrix.M12;
            destination[2] = matrix.M13; destination[3] = matrix.M14;
            destination[4] = matrix.M21; destination[5] = matrix.M22;
            destination[6] = matrix.M23; destination[7] = matrix.M24;
            destination[8] = matrix.M31; destination[9] = matrix.M32;
            destination[10] = matrix.M33; destination[11] = matrix.M34;
            destination[12] = matrix.M41; destination[13] = matrix.M42;
            destination[14] = matrix.M43; destination[15] = matrix.M44;
        }

        private bool UpdateWorldPosition(int x, int y)
        {
            if (!pickMatricesReady) return false;

            float scale = DpiScale;
            int px = Math.Max(0, Math.Min(pickViewMatrix[2] - 1, (int)Math.Round(x * scale)));
            int py = Math.Max(0, Math.Min(pickViewMatrix[3] - 1, (int)Math.Round(y * scale)));
            int fy = pickViewMatrix[3] - 1 - py;

            if (!CustomUnProject(new Vector3(px, fy, 0.0f), pickModelMatrix, pickProjMatrix, pickViewMatrix, out Vector3 rayNear)) return false;
            if (!CustomUnProject(new Vector3(px, fy, 1.0f), pickModelMatrix, pickProjMatrix, pickViewMatrix, out Vector3 rayFar)) return false;

            return world != null && world.TryRaycastTerrain(rayNear, rayFar, out worldPos);
        }

        private void UpdateHover(int x, int y)
        {
            int screenPxY = Height - y;
            mouseX = screenX + x         / zoom;
            mouseY = screenY + screenPxY / zoom;

            if (!pickMatricesReady)
            {
                nodeHovered = false;
                world.ClearHover();
                return;
            }

            float scale = DpiScale;
            int px = Math.Max(0, Math.Min(pickViewMatrix[2] - 1, (int)Math.Round(x * scale)));
            int py = Math.Max(0, Math.Min(pickViewMatrix[3] - 1, (int)Math.Round(y * scale)));
            int fy = pickViewMatrix[3] - 1 - py;
            if (!UpdateWorldPosition(x, y))
                world.ClearTileHover();

            nodeHovered = world.SearchScreenPoint(px, fy, 14.0 * scale, pickModelMatrix, pickProjMatrix, pickViewMatrix);
        }

        private void RegenerateTerrain(int? tileCount = null, ForestPattern? forestPattern = null)
        {
            if (!isLoaded) return;
            // A GL-kontextus a render alatt aktuális, így a buffer-csere itt biztonságos.
            int seed = new Random().Next();
            if (tileCount.HasValue) currentMapTiles = tileCount.Value;
            interaction.CancelGesture();
            world.Regenerate(TerrainSettings.Default.WithNodeSize(currentMapTiles + 1, seed).WithForestPattern(forestPattern ?? world.InitialForestPattern));
            simulationClock.Reset();
            RequestFrame();
        }

        private void QuickSave()
        {
            try
            {
                string path = SaveGamePath.Default;
                using FileStream stream = File.Create(path);
                world.Save(stream, 1.0 / simulationClock.StepSeconds);
                persistenceStatus = $"Mentve: {path}";
            }
            catch (Exception ex)
            {
                persistenceStatus = "Mentési hiba: " + ex.Message;
            }
        }

        private void QuickLoad()
        {
            try
            {
                string path = SaveGamePath.Default;
                using FileStream stream = File.OpenRead(path);
                interaction.CancelGesture();
                world.Load(stream);
                currentMapTiles = world.MapTileColumns;
                simulationClock.Reset(world.SimulationTick);
                persistenceStatus = $"Betöltve: {path}";
            }
            catch (Exception ex)
            {
                persistenceStatus = "Betöltési hiba: " + ex.Message;
            }
        }

        private bool CustomUnProject(Vector3 win, double[] model, double[] proj, int[] view, out Vector3 obj)
        {
            Matrix4d modelM = new Matrix4d(
                model[0], model[1], model[2], model[3],
                model[4], model[5], model[6], model[7],
                model[8], model[9], model[10], model[11],
                model[12], model[13], model[14], model[15]);

            Matrix4d projM = new Matrix4d(
                proj[0], proj[1], proj[2], proj[3],
                proj[4], proj[5], proj[6], proj[7],
                proj[8], proj[9], proj[10], proj[11],
                proj[12], proj[13], proj[14], proj[15]);

            Matrix4d viewProj = modelM * projM;
            if (Math.Abs(viewProj.Determinant) < 1e-12)
            {
                obj = Vector3.Zero;
                return false;
            }

            Matrix4d viewProjInv = Matrix4d.Invert(viewProj);

            Vector4d pos = new Vector4d(
                (win.X - view[0]) / view[2] * 2.0 - 1.0,
                (win.Y - view[1]) / view[3] * 2.0 - 1.0,
                win.Z * 2.0 - 1.0,
                1.0);

            double x = pos.X * viewProjInv.Row0.X + pos.Y * viewProjInv.Row1.X + pos.Z * viewProjInv.Row2.X + pos.W * viewProjInv.Row3.X;
            double y = pos.X * viewProjInv.Row0.Y + pos.Y * viewProjInv.Row1.Y + pos.Z * viewProjInv.Row2.Y + pos.W * viewProjInv.Row3.Y;
            double z = pos.X * viewProjInv.Row0.Z + pos.Y * viewProjInv.Row1.Z + pos.Z * viewProjInv.Row2.Z + pos.W * viewProjInv.Row3.Z;
            double w = pos.X * viewProjInv.Row0.W + pos.Y * viewProjInv.Row1.W + pos.Z * viewProjInv.Row2.W + pos.W * viewProjInv.Row3.W;

            if (w == 0.0)
            {
                obj = Vector3.Zero;
                return false;
            }

            obj = new Vector3((float)(x / w), (float)(y / w), (float)(z / w));
            return true;
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
            RunFrame();
            if (smokeTestFrameLimit.HasValue)
            {
                OpenTK.Graphics.OpenGL.ErrorCode error = GL.GetError();
                if (error != OpenTK.Graphics.OpenGL.ErrorCode.NoError)
                    throw new InvalidOperationException($"OpenGL core smoke test failed with {error} at frame {frameIndex}.");
                if (frameIndex >= smokeTestFrameLimit.Value)
                {
                    Console.WriteLine($"Game smoke: smoothed full frame={performance.FrameMilliseconds:F2}ms, render={performance.RenderMilliseconds:F2}ms, simulation={performance.SimulationMilliseconds:F2}ms");
                    Close();
                }
            }
        }

        private void SimulationSpeedMenuItem(string label, double speed)
        {
            if (ImGui.MenuItem(label, "", !simulationClock.IsPaused && simulationClock.Speed == speed))
            {
                simulationClock.Speed = speed;
                simulationClock.IsPaused = false;
            }
        }

        private void MapSizeMenuItem(int tileCount, bool experimental = false)
        {
            string label = experimental ? $"{tileCount} x {tileCount} (stresszteszt)" : $"{tileCount} x {tileCount}";
            if (ImGui.MenuItem(label, "", currentMapTiles == tileCount))
                RegenerateTerrain(tileCount);
        }

        protected override void OnUnload()
        {
            DisposeGlResources();
            base.OnUnload();
        }

        private void DisposeGlResources()
        {
            if (glResourcesDisposed) return;
            glResourcesDisposed = true;

            if (!isLoaded) return;

            try
            {
                Context.MakeCurrent();

                imgui?.Dispose();
                world?.Dispose();
                RenderDevice.Dispose();
            }
            catch
            {
                // The WinForms handle may already be invalid during shutdown; do not
                // turn cleanup into an application crash.
            }
            finally
            {
                imgui = null;
                world = null;
                isLoaded = false;
            }
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            if (!isLoaded) return;
            SetupViewport();
            RequestFrame();
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            if (!isLoaded) return;

            int x = (int)Math.Round(e.Position.X);
            int y = (int)Math.Round(e.Position.Y);
            imgui?.MouseMove(x, y);

            int dx = x - lastMouseX;
            int dy = y - lastMouseY;
            lastMouseX = x;
            lastMouseY = y;

            if (imgui != null && imgui.WantCaptureMouse)
            {
                world.ClearHover();
                RequestFrame();
                return;
            }

            UpdateHover(x, y);

            interaction.UpdateGesture();

            switch (activeButton)
            {
                case PointerButton.None:
                    break;

                case PointerButton.Left:
                    if (interaction.ActiveTool == TerrainEditTool.Inspect)
                    {
                        SetRotationTargetAroundPivot(targetRotY + 0.5f * dx, x, y, worldPos);
                    }
                    break;

                case PointerButton.Right:
                    screenX = panStartX - x / zoom;
                    screenY = panStartY - (Height - y) / zoom;
                    break;
            }

            RequestFrame();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (isLoaded) imgui?.MouseButton(MapMouseButton(e.Button), false);
            PointerButton released = MapPointerButton(e.Button);
            if (activeButton == released) activeButton = PointerButton.None;
            if (!isLoaded || e.Button != MouseButton.Left) return;

            if (mouseDownCapturedByImGui)
            {
                mouseDownCapturedByImGui = false;
                RequestFrame();
                return;
            }

            if (interaction.EndPrimaryGesture(nodeHovered))
            {
                RequestFrame();
                return;
            }

            // Snap a legközelebbi 90°-ra
            int x = (int)Math.Round(MousePosition.X);
            int y = (int)Math.Round(MousePosition.Y);
            SetRotationTargetAroundPivot(SnapRotation(targetRotY), x, y, worldPos);
            RequestFrame();
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!isLoaded) return;

            if (e.Key == Keys.Space)
            {
                simulationClock.IsPaused = !simulationClock.IsPaused;
                RequestFrame();
                return;
            }

            bool commandModifier = e.Modifiers.HasFlag(KeyModifiers.Control)
                || e.Modifiers.HasFlag(KeyModifiers.Super);
            if (commandModifier && e.Key == Keys.S) { QuickSave(); return; }
            if (commandModifier && e.Key == Keys.L) { QuickLoad(); return; }

            // Bal/Jobb: kamera forgatás 90°-os lépésekkel
            if (e.Key == Keys.Left)
                { SetRotationTargetAroundPivot(SnapRotation(targetRotY) - 90f, lastMouseX, lastMouseY, worldPos); RequestFrame(); }
            if (e.Key == Keys.Right)
                { SetRotationTargetAroundPivot(SnapRotation(targetRotY) + 90f, lastMouseX, lastMouseY, worldPos); RequestFrame(); }

            // Fel/Le: dőlésszög váltás (30° → 45° → 60°)
            if (e.Key == Keys.Up)
            {
                camera.StepTilt(-1);
                RequestFrame();
            }
            if (e.Key == Keys.Down)
            {
                camera.StepTilt(1);
                RequestFrame();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (!isLoaded) return;

            imgui?.MouseButton(MapMouseButton(e.Button), true);
            mouseDownCapturedByImGui = imgui != null && imgui.WantCaptureMouse;
            if (mouseDownCapturedByImGui) { RequestFrame(); return; }

            int x = (int)Math.Round(MousePosition.X);
            int y = (int)Math.Round(MousePosition.Y);
            UpdateHover(x, y);
            activeButton = MapPointerButton(e.Button);
            if (e.Button == MouseButton.Right)
                ClearRotationPivot();

            panStartX = mouseX;
            panStartY = mouseY;

            if (interaction.ActiveTool == TerrainEditTool.Inspect && e.Button == MouseButton.Left)
                BeginRotationPivot(x, y, worldPos);

            if (e.Button == MouseButton.Left)
                interaction.BeginPrimaryGesture();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!isLoaded) return;

            imgui?.MouseScroll(e.OffsetY);
            if (imgui != null && imgui.WantCaptureMouse) { RequestFrame(); return; }

            if (e.OffsetY > 0)
                camera.ZoomBy(1.25f);
            else
                camera.ZoomBy(0.8f);

            RequestFrame();
        }

    }
}
