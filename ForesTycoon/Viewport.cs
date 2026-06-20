using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    /// <summary>
    /// Izometrikus OpenGL viewport – Transport Tycoon stílusú terepnézet.
    /// Bal egér: forgatás (csak roty, rotx rögzített az izometrikus szögön).
    /// Jobb egér: pan (eltolás).
    /// Görgő: zoom.
    /// </summary>
    class Viewport : GLControl
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
        private bool toolbarPressed = false;
        private bool suppressNextClick = false;

        private bool         isLoaded     = false;
        private System.Windows.Forms.Timer waterTimer;
        private readonly Rectangle[] toolButtons =
        {
            new Rectangle(14, 14, 42, 42),
            new Rectangle(62, 14, 42, 42),
            new Rectangle(110, 14, 42, 42)
        };

        private static float SnapRotation(float angle)
        {
            return (float)(Math.Round((angle - 45.0) / 90.0) * 90.0 + 45.0);
        }

        // ── Háttérszín (referenciakép alapján) ──────────────────────────────
        private static readonly Color BG_COLOR = Color.FromArgb(44, 53, 64);

        // ────────────────────────────────────────────────────────────────────
        public Viewport() : base()
        {
            this.TabStop = true;   // billentyűzet fókusz
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                MakeCurrent();

            // OpenGL alapbeállítások
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);         // Flat, pixel-art stílus
            GL.Disable(EnableCap.CullFace);         // Mindkét oldal látszódjon (skirt)
            GL.ShadeModel(ShadingModel.Flat);
            GL.LineWidth(2.0f);

                terrain = new Terrain();
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

            float wc = (Width  - 1.0f) / zoom;
            float hc = (Height - 1.0f) / zoom;
            float pc = 0.5f / zoom;

            GL.Viewport(0, 0, Width, Height);
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

            terrain.Draw(activeTool != TerrainEditTool.Inspect);
            DrawEditorOverlay();

            SwapBuffers();
        }

        private void DrawEditorOverlay()
        {
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, Width, Height, 0, -1, 1);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();

            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            DrawToolbarBackground();
            DrawToolButton(toolButtons[0], TerrainEditTool.Inspect);
            DrawToolButton(toolButtons[1], TerrainEditTool.Raise);
            DrawToolButton(toolButtons[2], TerrainEditTool.Lower);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);

            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
        }

        private void DrawToolbarBackground()
        {
            GL.Color4(Color.FromArgb(210, 30, 38, 46));
            DrawRect(8, 8, 150, 54);
            GL.Color4(Color.FromArgb(230, 84, 96, 108));
            DrawLineLoop(8, 8, 150, 54);
        }

        private void DrawToolButton(Rectangle bounds, TerrainEditTool tool)
        {
            bool selected = activeTool == tool;
            GL.Color4(selected
                ? Color.FromArgb(235, 88, 122, 72)
                : Color.FromArgb(220, 52, 62, 72));
            DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);

            GL.Color4(selected
                ? Color.FromArgb(255, 220, 238, 184)
                : Color.FromArgb(230, 150, 164, 176));
            DrawLineLoop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            DrawToolGlyph(bounds, tool);
        }

        private void DrawToolGlyph(Rectangle bounds, TerrainEditTool tool)
        {
            float cx = bounds.X + bounds.Width * 0.5f;
            float cy = bounds.Y + bounds.Height * 0.5f;

            GL.LineWidth(3f);
            GL.Color4(Color.FromArgb(245, 235, 240, 220));
            GL.Begin(PrimitiveType.Lines);
            if (tool == TerrainEditTool.Inspect)
            {
                GL.Vertex2(cx - 9, cy - 9); GL.Vertex2(cx + 9, cy + 9);
                GL.Vertex2(cx + 9, cy - 9); GL.Vertex2(cx - 9, cy + 9);
            }
            else
            {
                GL.Vertex2(cx - 11, cy); GL.Vertex2(cx + 11, cy);
                if (tool == TerrainEditTool.Raise)
                {
                    GL.Vertex2(cx, cy - 11); GL.Vertex2(cx, cy + 11);
                }
            }
            GL.End();
            GL.LineWidth(2f);
        }

        private void DrawRect(float x, float y, float width, float height)
        {
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex2(x, y);
            GL.Vertex2(x + width, y);
            GL.Vertex2(x + width, y + height);
            GL.Vertex2(x, y + height);
            GL.End();
        }

        private void DrawLineLoop(float x, float y, float width, float height)
        {
            GL.Begin(PrimitiveType.LineLoop);
            GL.Vertex2(x, y);
            GL.Vertex2(x + width, y);
            GL.Vertex2(x + width, y + height);
            GL.Vertex2(x, y + height);
            GL.End();
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
            int fy = viewMatrix[3] - e.Y;
            GL.ReadPixels(e.X, fy, 1, 1, PixelFormat.DepthComponent, PixelType.Float, depth);
            Vector3 win = new Vector3(e.X, fy, depth[0]);
            CustomUnProject(win, modelMatrix, projMatrix, viewMatrix, out worldPos);
        }

        private void UpdateHover(MouseEventArgs e)
        {
            if (IsToolbarPoint(e.Location))
            {
                nodeHovered = false;
                terrain.ClearHover();
                return;
            }

            int screenPxY = Height - e.Y;
            mouseX = screenX + e.X       / zoom;
            mouseY = screenY + screenPxY / zoom;
            UpdateWorldPosition(e);
            nodeHovered = terrain.SearchPoint(worldPos.X, worldPos.Y, 5);
            terrain.SearchTile(worldPos.X, worldPos.Y);
        }

        private bool IsToolbarPoint(Point point)
        {
            for (int i = 0; i < toolButtons.Length; i++)
                if (toolButtons[i].Contains(point)) return true;

            return false;
        }

        private bool TryHandleToolbarClick(Point point)
        {
            for (int i = 0; i < toolButtons.Length; i++)
            {
                if (!toolButtons[i].Contains(point)) continue;

                activeTool = (TerrainEditTool)i;
                toolbarPressed = true;
                suppressNextClick = true;
                activeButton = MouseButtons.None;
                Invalidate();
                return true;
            }

            toolbarPressed = false;
            return false;
        }

        private void ApplyActiveTerrainTool()
        {
            if (!nodeHovered) return;

            if (activeTool == TerrainEditTool.Raise)
                terrain.UpElevation();
            else if (activeTool == TerrainEditTool.Lower)
                terrain.DownElevation();
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

            int dx = e.X - lastMouseX;
            int dy = e.Y - lastMouseY;
            lastMouseX = e.X;
            lastMouseY = e.Y;
            UpdateHover(e);

            switch (e.Button)
            {
                case MouseButtons.None:
                    break;

                case MouseButtons.Left:
                    if (activeTool == TerrainEditTool.Inspect && !toolbarPressed)
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
            toolbarPressed = false;
            if (!isLoaded || e.Button != MouseButtons.Left) return;
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
            if (TryHandleToolbarClick(e.Location)) return;

            UpdateHover(e);
            activeButton = e.Button;
            panStartX = mouseX;
            panStartY = mouseY;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!isLoaded) return;

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
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return;
            }
            if (activeTool != TerrainEditTool.Inspect)
            {
                ApplyActiveTerrainTool();
                Refresh();
                return;
            }

            Refresh();
        }

    }
}
