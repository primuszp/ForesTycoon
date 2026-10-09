using System;
using System.Drawing;
using System.IO;
using OpenTK.Mathematics;
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
    sealed partial class Viewport : GameWindow
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
        private readonly DioramaPostProcess postProcess = new DioramaPostProcess();
        private readonly FrameClock frameClock = new FrameClock();
        private readonly SimulationFrameRunner simulation = new SimulationFrameRunner(
            ticksPerSecond: 30.0, maximumTicksPerFrame: 2048, maximumWorkMilliseconds: 8);
        private FixedStepClock simulationClock => simulation.Clock;
        private readonly FramePerformanceMonitor performance = new FramePerformanceMonitor();
        private ulong frameIndex;
        private int currentMapTiles = 64;
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
        public Viewport(ulong? smokeTestFrameLimit = null, string captureDirectory = null) : base(
            new GameWindowSettings
            {
                UpdateFrequency = 0
            },
            RenderBackendSelection.Window.CreateSettings("ForesTycoon", new Vector2i(1280,720), true))
        {
            this.smokeTestFrameLimit = smokeTestFrameLimit;
            this.captureDirectory = captureDirectory;
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            try
            {
                RenderBackendSelection.Window.MakeCurrent(this);
                RenderBackendSelection.Window.SetSwapInterval(this, 0);   // vsync ki (OpenTK 3 VSync=false megfelelője)

                RenderDevice.Initialize();
                RenderDevice.InitializeFrameState();

                world = new GameWorld(TerrainSettings.Default);
                interaction = new WorldInteractionController(world) { TargetPicked = SendTargetPicked };
                imgui = new ImGuiController();
                HudTheme.Apply();
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

            RenderDevice.SetViewport(fbWidth, fbHeight);
            projection = Matrix4.CreateOrthographicOffCenter(
                (float)(screenX - pc), (float)(screenX + wc + pc),
                (float)(screenY - pc), (float)(screenY + hc + pc),
                (float)Z_NEAR, (float)Z_FAR);
        }

        // ── Rajzolás ─────────────────────────────────────────────────────────
        private void Render()
        {
            bool diorama = postProcess.Begin(world.Graphics, FramebufferWidth, FramebufferHeight);
            RenderDevice.Clear(new Vector4(BG_COLOR.R/255f, BG_COLOR.G/255f, BG_COLOR.B/255f, BG_COLOR.A/255f));
            if (diorama)
                postProcess.DrawBackdrop(world.Graphics, new Vector3(BG_COLOR.R / 255f, BG_COLOR.G / 255f, BG_COLOR.B / 255f));

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
            if (diorama)
                postProcess.End(world.Graphics, renderContext.PixelsPerWorldUnit, DpiScale, (float)frameClock.TotalTimeSeconds);

            DrawImGui();

            if (smokeTestFrameLimit.HasValue && frameIndex == 10)
                ValidateSmokeFramebuffer();
            if (captureDirectory != null)
                RunCaptureScript();

            RenderBackendSelection.Window.Present(this);
        }

        private void ValidateSmokeFramebuffer()
        {
            int width = Math.Min(64, FramebufferWidth);
            int height = Math.Min(64, FramebufferHeight);
            byte[] pixels = new byte[width * height * 4];
            RenderDevice.ReadPixels((FramebufferWidth - width) / 2, (FramebufferHeight - height) / 2,
                width, height, pixels);

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
                RenderBackendSelection.Window.MakeCurrent(this);
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

        private void SelectTool(TerrainEditTool tool)
        {
            interaction.SelectTool(tool);
            world.Graphics.SawmillPreview=tool==TerrainEditTool.PlaceSawmill||tool==TerrainEditTool.PlaceDepot;
            RequestFrame();
        }

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
                SaveGameFile.Write(path, stream => world.Save(stream, 1.0 / simulationClock.StepSeconds));
                ShowToast($"Mentve: {path}", HudTheme.Good);
            }
            catch (Exception ex)
            {
                ShowToast("Mentési hiba: " + ex.Message, HudTheme.Bad);
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
                ShowToast($"Betöltve: {path}", HudTheme.Good);
            }
            catch (Exception ex)
            {
                ShowToast("Betöltési hiba: " + ex.Message, HudTheme.Bad);
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
                RenderDevice.CheckErrors($"Game smoke test at frame {frameIndex}");
                if (frameIndex >= smokeTestFrameLimit.Value)
                {
                    Console.WriteLine($"Game smoke: smoothed full frame={performance.FrameMilliseconds:F2}ms, render={performance.RenderMilliseconds:F2}ms, simulation={performance.SimulationMilliseconds:F2}ms");
                    Close();
                }
            }
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
                RenderBackendSelection.Window.MakeCurrent(this);

                imgui?.Dispose();
                postProcess.Dispose();
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

            if (HandleHotkey(e)) { RequestFrame(); return; }

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
