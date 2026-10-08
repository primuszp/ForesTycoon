using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ForesTycoon
{
    // Isolated art-review scene: no quicksave, simulation clock or production generator changes.
    internal sealed class ForestPreviewWindow : GameWindow
    {
        private readonly string captureDirectory;
        private Terrain terrain;
        private TerrainRenderer renderer;
        private readonly IsometricCamera camera = new IsometricCamera();
        private int captureIndex;
        private static readonly float[] Tilts = { -30, -45, -60 };
        private static readonly float[] Scales = { 10, 5, 2 };
        internal ForestPreviewWindow(string captureDirectory = null) : base(
            new GameWindowSettings { UpdateFrequency = 60 },
            RenderBackendSelection.Window.CreateSettings("Erdominta | drag: rotate | wheel: zoom | arrows: rotate/tilt | Esc: close",
                new Vector2i(1280,900), captureDirectory == null))
        { this.captureDirectory = captureDirectory; }

        protected override void OnLoad()
        {
            base.OnLoad();
            RenderDevice.Initialize();
            RenderDevice.InitializeFrameState();
            terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 20260913), ForestVisualFixture.Height);
            var forest = new ForestSystem(terrain.Map, ForestVisualFixture.CreateStands());
            renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest);
            if (captureDirectory != null) Directory.CreateDirectory(captureDirectory);
        }
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            float tilt = captureDirectory == null ? camera.Tilt : Tilts[(captureIndex / 3) % 3];
            float yaw = captureDirectory == null ? camera.Yaw : -45 + (captureIndex / 9) * 90;
            float zoom = captureDirectory == null ? camera.Zoom : Scales[captureIndex % 3];
            float halfX = FramebufferSize.X / (2f * zoom), halfY = FramebufferSize.Y / (2f * zoom);
            var view = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(yaw)) * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(tilt));
            var projection = Matrix4.CreateOrthographicOffCenter(-halfX, halfX, -halfY + 4, halfY + 4, -1000, 1000);
            RenderDevice.SetViewport(FramebufferSize.X, FramebufferSize.Y);
            RenderDevice.Clear(new Vector4(0.1725f, 0.2078f, 0.251f, 1));
            RenderDevice.SetCamera(view * projection);
            RenderMetrics.BeginFrame();
            renderer.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, tilt, yaw,
                -halfX, -halfY + 4, halfX, halfY + 4, zoom));
            RenderDevice.CheckErrors("Forest preview");
            if (captureDirectory != null)
            {
                string path = Path.Combine(captureDirectory, $"forest-yaw{yaw}-tilt{tilt}-zoom{zoom}.png");
                FramebufferCapture.SavePng(path, FramebufferSize.X, FramebufferSize.Y);
                if (++captureIndex == 36) { Console.WriteLine($"Captured 36 forest views to {Path.GetFullPath(captureDirectory)}"); Close(); }
            }
            RenderBackendSelection.Window.Present(this);
        }
        protected override void OnKeyDown(KeyboardKeyEventArgs args)
        {
            base.OnKeyDown(args);
            if (args.Key == Keys.Escape) Close();
            if (args.Key == Keys.Left) camera.Yaw -= 45;
            if (args.Key == Keys.Right) camera.Yaw += 45;
            if (args.Key == Keys.Up) camera.StepTilt(-1);
            if (args.Key == Keys.Down) camera.StepTilt(1);
        }
        protected override void OnMouseMove(MouseMoveEventArgs args)
        {
            base.OnMouseMove(args);
            if (MouseState.IsButtonDown(MouseButton.Left)) camera.Yaw += args.DeltaX * 0.35f;
        }
        protected override void OnMouseWheel(MouseWheelEventArgs args)
        {
            base.OnMouseWheel(args);
            camera.Zoom = Math.Clamp(camera.Zoom * MathF.Pow(1.15f, args.OffsetY), 1.5f, 24);
        }
        protected override void OnUnload()
        {
            renderer?.Dispose(); terrain?.Dispose(); RenderDevice.Dispose();
            base.OnUnload();
        }
    }
}
