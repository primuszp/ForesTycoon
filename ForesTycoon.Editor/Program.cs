using System;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--rules-benchmark") { RuleOptimizationBenchmark.Run(); return; }
            if (args.Length == 2 && args[0] == "--export-current-rules")
            {
                string destination = System.IO.Path.GetFullPath(args[1]);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                var rules = CurrentGameRules.Build(); System.IO.File.WriteAllText(destination, rules.ToJson());
                Console.WriteLine($"Exported {rules.Rules.Count} current rules and {rules.Connections().Length} dependencies: {destination}"); return;
            }
            RenderBackendSelection.UseOpenGl();
            if (Array.Exists(args, a => a == "--window-lifecycle-smoke-test")) { WindowLifecycleSmokeTest.Run(); return; }
            if (Array.Exists(args, a => a == "--smoke-test"))
            {
                RuleEditorSmokeTest.Run();
                using var smokeWindow = new EditorWindow(null, true); smokeWindow.Run(); return;
            }
            string file = null;
            if (args.Length > 0)
            {
                if (args.Length != 2 || args[0] != "--model") throw new ArgumentException("Használat: ForesTycoon.Editor [--model modell.json] vagy --smoke-test");
                file = args[1];
            }
            using var window = new EditorWindow(file); window.Run();
        }
    }

    /// <summary>Standalone authoring host. Its sandbox runs the same GameWorld adapter as the game.</summary>
    internal sealed class EditorWindow : RenderGameWindow
    {
        private ImGuiController ui;
        protected override ImGuiController UiController => ui;
        private GameWorld sandbox;
        private readonly RuleEditorView editor;
        private bool open = true;
        private readonly bool smoke;
        private int frames;
        internal EditorWindow(string file, bool smoke = false, bool visible = true) : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            Title = "ForesTycoon – Szabályrendszer editor", ClientSize = new(1280, 900),
            API = ContextAPI.OpenGL, APIVersion = new(3, 3), Profile = ContextProfile.Core, StartVisible = !smoke && visible
        }) { editor = new RuleEditorView(file); this.smoke = smoke; }

        protected override void LoadScene()
        {
            VSync = VSyncMode.On;
            sandbox = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
            ui = new ImGuiController(); HudTheme.Apply();
        }
        protected override void RenderScene(FrameEventArgs args)
        {
            sandbox.ExecutePendingCommands();
            Vec2 scale = new((float)FramebufferSize.X / Math.Max(1, ClientSize.X), (float)FramebufferSize.Y / Math.Max(1, ClientSize.Y));
            ui.Update(ClientSize.X, ClientSize.Y, FramebufferSize.X, FramebufferSize.Y, scale, (float)Math.Max(0.001, args.Time));
            ImGui.SetNextWindowPos(Vec2.Zero);
            ImGui.SetNextWindowSize(new Vec2(ClientSize.X, ClientSize.Y));
            editor.Draw(sandbox, ref open, true);
            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
            GL.ClearColor(.08f, .1f, .12f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            ui.Render();
            if (smoke && ++frames == 3)
            {
                GL.Finish();
                if (GL.GetError() != OpenTK.Graphics.OpenGL.ErrorCode.NoError) throw new InvalidOperationException("Standalone editor OpenGL error.");
                string capture = System.IO.Path.GetFullPath("artifacts/rule-editor/standalone.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(capture));
                FramebufferCapture.SavePng(capture, FramebufferSize.X, FramebufferSize.Y);
                Console.WriteLine("Standalone editor window smoke passed: " + capture);
                Close();
            }
            SwapBuffers(); if (!open) Close();
        }
        protected override void MouseMoveScene(MouseMoveEventArgs e) { ui?.MouseMove((int)e.X, (int)e.Y); }
        protected override void MouseDownScene(MouseButtonEventArgs e) { if ((int)e.Button < 5) ui?.MouseButton((int)e.Button, true); }
        protected override void MouseUpScene(MouseButtonEventArgs e) { if ((int)e.Button < 5) ui?.MouseButton((int)e.Button, false); }
        protected override void MouseWheelScene(MouseWheelEventArgs e) { ui?.MouseScroll(e.OffsetY); }
        protected override void TextInputScene(TextInputEventArgs e) { if (ui != null) ImGui.GetIO().AddInputCharacter((uint)e.Unicode); }
        protected override void KeyDownScene(KeyboardKeyEventArgs e) { Key(e.Key, true); }
        protected override void KeyUpScene(KeyboardKeyEventArgs e) { Key(e.Key, false); }
        private void Key(Keys key, bool down)
        {
            if (ui == null) return;
            var io = ImGui.GetIO();
            var mapped = key switch
            {
                >= Keys.A and <= Keys.Z => (ImGuiKey)((int)ImGuiKey.A + (int)key - (int)Keys.A),
                >= Keys.D0 and <= Keys.D9 => (ImGuiKey)((int)ImGuiKey._0 + (int)key - (int)Keys.D0),
                Keys.Tab => ImGuiKey.Tab, Keys.Left => ImGuiKey.LeftArrow, Keys.Right => ImGuiKey.RightArrow,
                Keys.Up => ImGuiKey.UpArrow, Keys.Down => ImGuiKey.DownArrow, Keys.Home => ImGuiKey.Home,
                Keys.End => ImGuiKey.End, Keys.PageUp => ImGuiKey.PageUp, Keys.PageDown => ImGuiKey.PageDown,
                Keys.Backspace => ImGuiKey.Backspace, Keys.Delete => ImGuiKey.Delete, Keys.Insert => ImGuiKey.Insert,
                Keys.Enter => ImGuiKey.Enter, Keys.KeyPadEnter => ImGuiKey.KeypadEnter, Keys.Escape => ImGuiKey.Escape,
                Keys.Space => ImGuiKey.Space, _ => ImGuiKey.None
            };
            if (mapped != ImGuiKey.None) io.AddKeyEvent(mapped, down);
            io.AddKeyEvent(ImGuiKey.ModCtrl, KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl));
            io.AddKeyEvent(ImGuiKey.ModShift, KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift));
            io.AddKeyEvent(ImGuiKey.ModAlt, KeyboardState.IsKeyDown(Keys.LeftAlt) || KeyboardState.IsKeyDown(Keys.RightAlt));
            io.AddKeyEvent(ImGuiKey.ModSuper, KeyboardState.IsKeyDown(Keys.LeftSuper) || KeyboardState.IsKeyDown(Keys.RightSuper));
        }
        protected override void UnloadScene()
        {
            ui?.Dispose(); sandbox?.Dispose(); ui = null; sandbox = null; }
    }
}
