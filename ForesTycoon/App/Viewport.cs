using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
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
    class Viewport : OpenTK.GLControl.GLControl
    {
        // ── Vetítési paraméterek ─────────────────────────────────────────────
        private const double Z_NEAR = -1000.0;
        private const double Z_FAR  = +1000.0;

        // ── Kamera állapot ───────────────────────────────────────────────────
        private float zoom       = 10f;
        private float targetZoom = 10f;   // smooth zoom célérték

        // Tilt szintek: fel/le nyíllal lép köztük
        private static readonly float[] TILT_ANGLES = { -30f, -45f, -60f };
        private int   tiltIndex = 2;       // alapértelmezett: -60°
        private float rotx      = -60f;
        private float roty      = -45f;
        private float targetRotY = -45f;

        private double screenX = 0;
        private double screenY = 0;
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
        private bool pickMatricesReady;
        private int[]    viewMatrix  = new int[4];
        private Vector3  worldPos    = new Vector3();

        // ── Játékállapot ─────────────────────────────────────────────────────
        private MouseButtons activeButton = MouseButtons.None;
        private bool mouseDownCapturedByImGui;
        private bool         nodeHovered  = false;
        private Terrain      terrain      = null;
        private TerrainEditTool activeTool = TerrainEditTool.Inspect;
        private int brushSize = 1;       // 1 = egy node, nagyobb = korong sugár
        private int brushStrength = 1;   // szintlépések száma kattintásonként
        private Tile roadDragStartTile;  // úthálózat: drag-build kezdő csempéje
        private bool roadDragging;       // épp utat húzunk-e
        private bool roadDragRemove;     // bontás (true) vagy építés (false)

        private static bool IsRoadTool(TerrainEditTool t) =>
            t == TerrainEditTool.Road || t == TerrainEditTool.RoadRemove;

        private bool         isLoaded     = false;
        private ImGuiController imgui;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private double lastTime;
        private const int FrameTimerIntervalMs = 33;
        private System.Windows.Forms.Timer frameTimer;
        private readonly Stopwatch renderClock = new Stopwatch();
        private double lastRenderTimeSeconds;
        private ulong frameIndex;

        private float DpiScale => DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        private int FramebufferWidth => Math.Max(1, (int)Math.Round(ClientSize.Width * DpiScale));
        private int FramebufferHeight => Math.Max(1, (int)Math.Round(ClientSize.Height * DpiScale));

        private static int MapMouseButton(MouseButtons b)
        {
            if (b == MouseButtons.Right) return 1;
            if (b == MouseButtons.Middle) return 2;
            return 0; // Left / egyéb
        }

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
            if (terrain == null) return true;

            terrain.GetWorldBounds(out Vector3 min, out Vector3 max);
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
            if (activeButton == MouseButtons.Left) return;
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
        public Viewport() : base(new OpenTK.GLControl.GLControlSettings
        {
            // Compatibility profil: a jelenlegi immediate-mode (fixed-function)
            // renderer így OpenTK 4 alatt is fut. Core-profile shaderekre a
            // következő migrációs körben térünk át.
            Profile = OpenTK.Windowing.Common.ContextProfile.Compatability,
            APIVersion = new System.Version(3, 3)
        })
        {
            this.TabStop = true;   // billentyűzet fókusz
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                MakeCurrent();
                Context.SwapInterval = 0;   // vsync ki (OpenTK 3 VSync=false megfelelője)

                // OpenGL alapbeállítások
                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);         // Flat, pixel-art stílus
                GL.Disable(EnableCap.CullFace);         // Mindkét oldal látszódjon (skirt)
                GL.ShadeModel(ShadingModel.Flat);
                GL.LineWidth(2.0f);

                terrain = new Terrain();
                imgui = new ImGuiController();
                isLoaded = true;
                renderClock.Start();
                SetupViewport();
                Focus();

                frameTimer = new System.Windows.Forms.Timer { Interval = FrameTimerIntervalMs };
                frameTimer.Tick += (s, e) => RequestFrame();
                frameTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Viewport initialization failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        // ── Vetítési mátrix beállítása ───────────────────────────────────────
        private void SetupViewport()
        {
            if (!isLoaded) return;
            if (Height == 0) ClientSize = new Size(Width, 1);

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            int fbWidth = FramebufferWidth;
            int fbHeight = FramebufferHeight;
            float wc = (Width  - 1.0f) / zoom;
            float hc = (Height - 1.0f) / zoom;
            float pc = 0.5f / zoom;

            GL.Viewport(0, 0, fbWidth, fbHeight);
            GL.Ortho(screenX - pc, screenX + wc + pc,
                     screenY - pc, screenY + hc + pc,
                     Z_NEAR, Z_FAR);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
        }

        // ── Rajzolás ─────────────────────────────────────────────────────────
        private void Render()
        {
            GL.ClearColor(BG_COLOR);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.Rotate(rotx, 1f, 0f, 0f);
            GL.Rotate(roty, 0f, 0f, 1f);
            CapturePickMatrices();

            double now = renderClock.Elapsed.TotalSeconds;
            float deltaTime = (float)Math.Max(0.0, now - lastRenderTimeSeconds);
            lastRenderTimeSeconds = now;
            frameIndex++;

            bool isTerrainEditTool = activeTool == TerrainEditTool.Raise || activeTool == TerrainEditTool.Lower;
            bool isRotating = activeTool == TerrainEditTool.Inspect && activeButton == MouseButtons.Left;
            bool showTileHighlight = !isTerrainEditTool && !isRotating;
            float markerPixelRadius = Math.Max(4.0f, Math.Min(zoom * 0.55f, 9.0f));
            float nodeMarkerRadius = markerPixelRadius / Math.Max(zoom, 0.001f);

            RenderContext renderContext = new RenderContext(
                now,
                deltaTime,
                frameIndex,
                isTerrainEditTool,
                showTileHighlight,
                nodeMarkerRadius);

            terrain.Draw(renderContext);

            DrawImGui();

            SwapBuffers();
        }

        private void RequestFrame()
        {
            if (isLoaded && !DesignMode) Invalidate();
        }

        private void DrawImGui()
        {
            if (imgui == null) return;

            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)(now - lastTime);
            lastTime = now;

            float scale = DpiScale;
            imgui.Update(Width, Height, FramebufferWidth, FramebufferHeight, new NVec2(scale, scale), delta);

            DrawMainMenu();
            DrawToolbar();
            DrawStatusPanel();

            imgui.Render();
        }

        // ── Felső menüsor (Transport Tycoon stílus) ──────────────────────────
        private void DrawMainMenu()
        {
            if (!ImGui.BeginMainMenuBar()) return;

            if (ImGui.BeginMenu("Fájl"))
            {
                if (ImGui.MenuItem("Új terep (seed)")) RegenerateTerrain();
                ImGui.Separator();
                if (ImGui.MenuItem("Kilépés"))
                    BeginInvoke((MethodInvoker)(() => FindForm()?.Close()));
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Nézet"))
            {
                if (ImGui.MenuItem("Kamera alaphelyzet")) ResetCamera();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Eszközök"))
            {
                ToolMenuItem("Vizsgálat", TerrainEditTool.Inspect);
                ToolMenuItem("Emelés", TerrainEditTool.Raise);
                ToolMenuItem("Süllyesztés", TerrainEditTool.Lower);
                ToolMenuItem("Út építés", TerrainEditTool.Road);
                ToolMenuItem("Út bontás", TerrainEditTool.RoadRemove);
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
            ToolButton("Bontás", TerrainEditTool.RoadRemove);

            ImGui.PushItemWidth(150);
            ImGui.SliderInt("Méret", ref brushSize, 1, 5);
            ImGui.SliderInt("Erő", ref brushStrength, 1, 5);
            ImGui.PopItemWidth();

            ImGui.End();
        }

        private void DrawStatusPanel()
        {
            ImGui.SetNextWindowPos(new NVec2(8, Math.Max(80, Height - 64)), ImGuiCond.Always);
            ImGui.Begin("##status",
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize);

            ImGui.Text($"Eszköz: {ToolName(activeTool)}");
            if (IsRoadTool(activeTool))
            {
                ImGui.Text($"Út-csempék: {terrain.RoadCount}");
                if (roadDragging)
                    ImGui.Text($"Hossz: {terrain.RoadPreviewCount}");
            }
            ImGui.Text($"{ImGui.GetIO().Framerate:F0} FPS");

            ImGui.End();
        }

        private void ToolButton(string label, TerrainEditTool tool)
        {
            bool active = activeTool == tool;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new NVec4(0.34f, 0.48f, 0.28f, 1f));
            if (ImGui.Button(label, new NVec2(54, 40))) SelectTool(tool);
            if (active) ImGui.PopStyleColor();
        }

        private void ToolMenuItem(string label, TerrainEditTool tool)
        {
            if (ImGui.MenuItem(label, "", activeTool == tool)) SelectTool(tool);
        }

        private void SelectTool(TerrainEditTool tool)
        {
            activeTool = tool;
            roadDragStartTile = null;   // úthálózat drag megszakítása eszközváltáskor
            roadDragging = false;
            terrain?.ClearRoadPreview();
            RequestFrame();
        }

        private static string ToolName(TerrainEditTool tool) => tool switch
        {
            TerrainEditTool.Raise => "Emelés",
            TerrainEditTool.Lower => "Süllyesztés",
            TerrainEditTool.Road => "Út építés",
            TerrainEditTool.RoadRemove => "Út bontás",
            _ => "Vizsgálat"
        };

        private void ResetCamera()
        {
            tiltIndex = 2;
            rotx = TILT_ANGLES[tiltIndex];
            roty = targetRotY = -45f;
            targetZoom = 10f;
            screenX = screenY = 0;
            RequestFrame();
        }

        // ── OpenGL mátrixok kiolvasása (egér → világ koordináta) ────────────
        private void ReadGLMatrices()
        {
            GL.GetDouble(GetPName.ProjectionMatrix,  projMatrix);
            GL.GetDouble(GetPName.ModelviewMatrix,   modelMatrix);
            GL.GetInteger(GetPName.Viewport,         viewMatrix);
        }

        private void CapturePickMatrices()
        {
            ReadGLMatrices();
            Array.Copy(projMatrix, pickProjMatrix, projMatrix.Length);
            Array.Copy(modelMatrix, pickModelMatrix, modelMatrix.Length);
            Array.Copy(viewMatrix, pickViewMatrix, viewMatrix.Length);
            pickMatricesReady = pickViewMatrix[2] > 0 && pickViewMatrix[3] > 0;
        }

        private bool UpdateWorldPosition(MouseEventArgs e)
        {
            if (!pickMatricesReady) return false;

            float scale = DpiScale;
            int px = Math.Max(0, Math.Min(pickViewMatrix[2] - 1, (int)Math.Round(e.X * scale)));
            int py = Math.Max(0, Math.Min(pickViewMatrix[3] - 1, (int)Math.Round(e.Y * scale)));
            int fy = pickViewMatrix[3] - 1 - py;

            if (!CustomUnProject(new Vector3(px, fy, 0.0f), pickModelMatrix, pickProjMatrix, pickViewMatrix, out Vector3 rayNear)) return false;
            if (!CustomUnProject(new Vector3(px, fy, 1.0f), pickModelMatrix, pickProjMatrix, pickViewMatrix, out Vector3 rayFar)) return false;

            Vector3 ray = rayFar - rayNear;
            if (Math.Abs(ray.Z) < 0.0001f) return false;

            float targetZ = 0.0f;
            for (int i = 0; i < 4; i++)
            {
                float t = (targetZ - rayNear.Z) / ray.Z;
                worldPos = rayNear + ray * t;
                if (terrain == null || !terrain.TryGetSurfaceZ(worldPos.X, worldPos.Y, out float surfaceZ))
                    break;

                if (Math.Abs(surfaceZ - targetZ) < 0.01f)
                    break;

                targetZ = surfaceZ;
            }

            return true;
        }

        private void UpdateHover(MouseEventArgs e)
        {
            int screenPxY = Height - e.Y;
            mouseX = screenX + e.X       / zoom;
            mouseY = screenY + screenPxY / zoom;

            if (!pickMatricesReady)
            {
                nodeHovered = false;
                terrain.ClearHover();
                return;
            }

            float scale = DpiScale;
            int px = Math.Max(0, Math.Min(pickViewMatrix[2] - 1, (int)Math.Round(e.X * scale)));
            int py = Math.Max(0, Math.Min(pickViewMatrix[3] - 1, (int)Math.Round(e.Y * scale)));
            int fy = pickViewMatrix[3] - 1 - py;
            nodeHovered = terrain.SearchScreenPoint(px, fy, 14.0 * scale, pickModelMatrix, pickProjMatrix, pickViewMatrix);

            if (UpdateWorldPosition(e))
                terrain.SearchTile(worldPos.X, worldPos.Y);
            else
                terrain.ClearTileHover();
        }

        private void ApplyActiveTerrainTool()
        {
            if (!nodeHovered) return;

            int radius = brushSize - 1;
            if (activeTool == TerrainEditTool.Raise)
                terrain.EditElevation(+1, radius, brushStrength);
            else if (activeTool == TerrainEditTool.Lower)
                terrain.EditElevation(-1, radius, brushStrength);
        }

        private void RegenerateTerrain()
        {
            if (!isLoaded) return;
            // A GL-kontextus a render alatt aktuális, így a buffer-csere itt biztonságos.
            terrain.Dispose();
            int seed = new Random().Next();
            terrain = new Terrain(TerrainSettings.Default.WithSeed(seed));
            RequestFrame();
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

        // ── WinForms override-ok ─────────────────────────────────────────────
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!isLoaded || DesignMode) return;

            // Smooth zoom: exponenciális közelítés a célértékhez
            float diff = targetZoom - zoom;
            if (Math.Abs(diff) > 0.01f)
            {
                float prevZoom = zoom;
                zoom += diff * 0.18f;
                // Zoom a kurzor körül tartva
                screenX = mouseX - (mouseX - screenX) * (prevZoom / zoom);
                screenY = mouseY - (mouseY - screenY) * (prevZoom / zoom);
                RequestFrame();   // következő frame
            }

            float rotDiff = targetRotY - roty;
            if (Math.Abs(rotDiff) > 0.01f)
            {
                roty += rotDiff * 0.12f;
                ApplyRotationPivotCompensation();
                RequestFrame();
            }
            else
            {
                roty = targetRotY;
                ApplyRotationPivotCompensation();
                EndRotationPivotIfSettled();
            }

            SetupViewport();
            Render();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            frameTimer?.Stop();
            frameTimer?.Dispose();
            imgui?.Dispose();
            base.OnHandleDestroyed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!isLoaded) return;
            SetupViewport();
            RequestFrame();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!isLoaded) return;

            imgui?.MouseMove(e.X, e.Y);

            int dx = e.X - lastMouseX;
            int dy = e.Y - lastMouseY;
            lastMouseX = e.X;
            lastMouseY = e.Y;

            if (imgui != null && imgui.WantCaptureMouse)
            {
                terrain.ClearHover();
                RequestFrame();
                return;
            }

            UpdateHover(e);

            if (roadDragging)
                terrain.SetRoadPreview(roadDragStartTile, terrain.HoveredTile, roadDragRemove);

            switch (e.Button)
            {
                case MouseButtons.None:
                    break;

                case MouseButtons.Left:
                    if (activeTool == TerrainEditTool.Inspect)
                    {
                        SetRotationTargetAroundPivot(targetRotY + 0.5f * dx, e.X, e.Y, worldPos);
                    }
                    break;

                case MouseButtons.Right:
                    screenX = panStartX - e.X       / zoom;
                    screenY = panStartY - (Height - e.Y) / zoom;
                    break;
            }

            RequestFrame();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isLoaded) imgui?.MouseButton(MapMouseButton(e.Button), false);
            if (activeButton == e.Button) activeButton = MouseButtons.None;
            if (!isLoaded || e.Button != MouseButtons.Left) return;

            if (mouseDownCapturedByImGui)
            {
                mouseDownCapturedByImGui = false;
                RequestFrame();
                return;
            }

            if (IsRoadTool(activeTool))
            {
                if (roadDragging && roadDragStartTile != null)
                {
                    if (roadDragRemove) terrain.RemoveRoadTilePath(roadDragStartTile, terrain.HoveredTile);
                    else terrain.BuildRoadTilePath(roadDragStartTile, terrain.HoveredTile);
                }
                terrain.ClearRoadPreview();
                roadDragging = false;
                roadDragStartTile = null;
                RequestFrame();
                return;
            }

            if (activeTool != TerrainEditTool.Inspect)
            {
                ApplyActiveTerrainTool();
                RequestFrame();
                return;
            }

            // Snap a legközelebbi 90°-ra
            SetRotationTargetAroundPivot(SnapRotation(targetRotY), e.X, e.Y, worldPos);
            RequestFrame();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left  || keyData == Keys.Right) return true;
            if (keyData == Keys.Up    || keyData == Keys.Down)  return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!isLoaded) return;

            // Bal/Jobb: kamera forgatás 90°-os lépésekkel
            if (e.KeyCode == Keys.Left)
                { SetRotationTargetAroundPivot(SnapRotation(targetRotY) - 90f, lastMouseX, lastMouseY, worldPos); RequestFrame(); }
            if (e.KeyCode == Keys.Right)
                { SetRotationTargetAroundPivot(SnapRotation(targetRotY) + 90f, lastMouseX, lastMouseY, worldPos); RequestFrame(); }

            // Fel/Le: dőlésszög váltás (30° → 45° → 60°)
            if (e.KeyCode == Keys.Up)
            {
                tiltIndex = Math.Max(0, tiltIndex - 1);
                rotx = TILT_ANGLES[tiltIndex];
                RequestFrame();
            }
            if (e.KeyCode == Keys.Down)
            {
                tiltIndex = Math.Min(TILT_ANGLES.Length - 1, tiltIndex + 1);
                rotx = TILT_ANGLES[tiltIndex];
                RequestFrame();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!isLoaded) return;
            Focus();

            imgui?.MouseButton(MapMouseButton(e.Button), true);
            mouseDownCapturedByImGui = imgui != null && imgui.WantCaptureMouse;
            if (mouseDownCapturedByImGui) { RequestFrame(); return; }

            UpdateHover(e);
            activeButton = e.Button;
            if (e.Button == MouseButtons.Right)
                ClearRotationPivot();

            panStartX = mouseX;
            panStartY = mouseY;

            if (activeTool == TerrainEditTool.Inspect && e.Button == MouseButtons.Left)
                BeginRotationPivot(e.X, e.Y, worldPos);

            if (IsRoadTool(activeTool) && e.Button == MouseButtons.Left)
            {
                roadDragStartTile = terrain.HoveredTile;
                roadDragRemove = activeTool == TerrainEditTool.RoadRemove;
                roadDragging = roadDragStartTile != null;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!isLoaded) return;

            imgui?.MouseScroll(e.Delta / 120f);
            if (imgui != null && imgui.WantCaptureMouse) { RequestFrame(); return; }

            if (e.Delta > 0)
                targetZoom = Math.Min(targetZoom * 1.25f, 10000f);
            else
                targetZoom = Math.Max(targetZoom / 1.25f, 0.005f);

            RequestFrame();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!isLoaded) return;
            if (imgui != null && imgui.WantCaptureMouse) return;

            RequestFrame();
        }

    }
}


