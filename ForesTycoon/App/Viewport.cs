using System;
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

        // Pan kezdőpont (jobb gomb lenyomásakor rögzítve)
        private double panStartX = 0;
        private double panStartY = 0;

        // ── OpenGL mátrixok (koordináta-visszaszámításhoz) ───────────────────
        private double[] projMatrix  = new double[16];
        private double[] modelMatrix = new double[16];
        private int[]    viewMatrix  = new int[4];
        private Vector3  worldPos    = new Vector3();

        // ── Játékállapot ─────────────────────────────────────────────────────
        private MouseButtons activeButton = MouseButtons.None;
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
        private System.Windows.Forms.Timer waterTimer;

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
                SetupViewport();
                Focus();

                waterTimer = new System.Windows.Forms.Timer { Interval = 120 };
                waterTimer.Tick += (s, e) => { if (isLoaded) Invalidate(); };
                waterTimer.Start();
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

            terrain.Draw(activeTool == TerrainEditTool.Raise || activeTool == TerrainEditTool.Lower);

            DrawImGui();

            SwapBuffers();
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
            Invalidate();
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
            Invalidate();
        }

        // ── OpenGL mátrixok kiolvasása (egér → világ koordináta) ────────────
        private void ReadGLMatrices()
        {
            GL.GetDouble(GetPName.ProjectionMatrix,  projMatrix);
            GL.GetDouble(GetPName.ModelviewMatrix,   modelMatrix);
            GL.GetInteger(GetPName.Viewport,         viewMatrix);
        }

        private void UpdateWorldPosition(MouseEventArgs e)
        {
            float[] depth = new float[1];
            ReadGLMatrices();
            float scale = DpiScale;
            int px = Math.Max(0, Math.Min(viewMatrix[2] - 1, (int)Math.Round(e.X * scale)));
            int py = Math.Max(0, Math.Min(viewMatrix[3] - 1, (int)Math.Round(e.Y * scale)));
            int fy = viewMatrix[3] - 1 - py;
            GL.ReadPixels(px, fy, 1, 1, PixelFormat.DepthComponent, PixelType.Float, depth);
            Vector3 win = new Vector3(px, fy, depth[0]);
            CustomUnProject(win, modelMatrix, projMatrix, viewMatrix, out worldPos);
        }

        private void UpdateHover(MouseEventArgs e)
        {
            int screenPxY = Height - e.Y;
            mouseX = screenX + e.X       / zoom;
            mouseY = screenY + screenPxY / zoom;
            UpdateWorldPosition(e);
            nodeHovered = terrain.SearchPoint(worldPos.X, worldPos.Y, 5);
            terrain.SearchTile(worldPos.X, worldPos.Y);
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
            Invalidate();
        }

        private void CustomUnProject(Vector3 win, double[] model, double[] proj, int[] view, out Vector3 obj)
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

            Matrix4d viewProjInv = Matrix4d.Invert(modelM * projM);

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
                return;
            }

            obj = new Vector3((float)(x / w), (float)(y / w), (float)(z / w));
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
                Invalidate();   // következő frame
            }

            float rotDiff = targetRotY - roty;
            if (Math.Abs(rotDiff) > 0.01f)
            {
                roty += rotDiff * 0.12f;
                Invalidate();
            }
            else
            {
                roty = targetRotY;
            }

            SetupViewport();
            Render();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            waterTimer?.Stop();
            waterTimer?.Dispose();
            imgui?.Dispose();
            base.OnHandleDestroyed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!isLoaded) return;
            SetupViewport();
            Invalidate();
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
                Invalidate();
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
                        roty += 0.5f * dx;
                        targetRotY = roty;
                    }
                    break;

                case MouseButtons.Right:
                    screenX = panStartX - e.X       / zoom;
                    screenY = panStartY - (Height - e.Y) / zoom;
                    break;
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isLoaded) imgui?.MouseButton(MapMouseButton(e.Button), false);
            if (!isLoaded || e.Button != MouseButtons.Left) return;

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
                Invalidate();
                return;
            }

            if (activeTool != TerrainEditTool.Inspect) return;
            // Snap a legközelebbi 90°-ra
            targetRotY = SnapRotation(roty);
            Invalidate();
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
                { targetRotY = SnapRotation(targetRotY) - 90f; Invalidate(); }
            if (e.KeyCode == Keys.Right)
                { targetRotY = SnapRotation(targetRotY) + 90f; Invalidate(); }

            // Fel/Le: dőlésszög váltás (30° → 45° → 60°)
            if (e.KeyCode == Keys.Up)
            {
                tiltIndex = Math.Max(0, tiltIndex - 1);
                rotx = TILT_ANGLES[tiltIndex];
                Invalidate();
            }
            if (e.KeyCode == Keys.Down)
            {
                tiltIndex = Math.Min(TILT_ANGLES.Length - 1, tiltIndex + 1);
                rotx = TILT_ANGLES[tiltIndex];
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!isLoaded) return;
            Focus();

            imgui?.MouseButton(MapMouseButton(e.Button), true);
            if (imgui != null && imgui.WantCaptureMouse) { Invalidate(); return; }

            UpdateHover(e);
            activeButton = e.Button;
            panStartX = mouseX;
            panStartY = mouseY;

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
            if (imgui != null && imgui.WantCaptureMouse) { Invalidate(); return; }

            if (e.Delta > 0)
                targetZoom = Math.Min(targetZoom * 1.25f, 10000f);
            else
                targetZoom = Math.Max(targetZoom / 1.25f, 0.005f);

            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!isLoaded) return;
            if (imgui != null && imgui.WantCaptureMouse) return;

            // Az út-eszközöket a drag-build (OnMouseDown/Up) kezeli, nem a klikk.
            if (activeTool != TerrainEditTool.Inspect && !IsRoadTool(activeTool))
            {
                ApplyActiveTerrainTool();
                Refresh();
                return;
            }

            Refresh();
        }

    }
}
