using System;
using ForesTycoon.Rules;
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
            if (RuleProjectCli.Run(args)) return;
            if (args.Length == 1 && args[0] == "--rules-benchmark") { RuleOptimizationBenchmark.Run(); return; }
            if (args.Length == 1 && args[0] == "--check-editor-coverage")
            {
                using var world = WorldLoader.CreateProject(GameWorld.DefaultRuleProject());
                var report = EditorCoverage.Check(world, world.BehaviorDocument);
                Console.WriteLine(EditorCoverage.Format(report));
                Environment.ExitCode = report.Gaps.Count == 0 ? 0 : 1; return;
            }
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
            string file = System.IO.File.Exists(RuleProject.DefaultPath) ? RuleProject.DefaultPath : null, worldFile = null;
            for (int i = 0; i < args.Length; i += 2) {
                if (i + 1 >= args.Length) throw new ArgumentException("Használat: ForesTycoon.Editor [--model projekt.json] [--world játékmentés.json]");
                if (args[i] is "--model" or "--project") file = args[i + 1];
                else if (args[i] == "--world") worldFile = args[i + 1];
                else throw new ArgumentException("Ismeretlen kapcsoló: " + args[i]);
            }
            using var window = new EditorWindow(file, worldFile: worldFile); window.Run();
        }
    }

    /// <summary>Standalone authoring host. Its sandbox runs the same GameWorld adapter as the game.</summary>
    internal sealed class EditorWindow : RenderGameWindow
    {
        private ImGuiController ui;
        protected override ImGuiController UiController => ui;
        private GameWorld sandbox;
        private System.Collections.Generic.IEnumerator<int> startupWork;
        private readonly WorkResult<GameWorld> startupResult = new();
        private readonly RuleEditorView editor;
        private bool open = true;
        private readonly bool smoke;
        private int frames;
        private readonly string worldFile;
        private System.Collections.Generic.SortedDictionary<string, string> openingReference;
        internal EditorWindow(string file, bool smoke = false, bool visible = true, string worldFile = null) : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            Title = "ForesTycoon – Világszerkesztő", ClientSize = new(1600, 960),
            API = ContextAPI.OpenGL, APIVersion = new(3, 3), Profile = ContextProfile.Core, StartVisible = !smoke && visible
        }) { editor = new RuleEditorView(file); this.smoke = smoke; this.worldFile = worldFile; if (!smoke) FitToScreen(); }

        // A laptop screen (e.g. a 1512×982 pt MacBook) cannot hold the 1600×960 window: open inside the work area, and
        // shrink text and spacing a little so the panels and previews keep their room.
        private float uiScale = 1;
        private void FitToScreen()
        {
            var work = Monitors.GetPrimaryMonitor().ClientArea;
            int width = work.Size.X, height = work.Size.Y;
            if (width <= 0 || height <= 0) return;
            uiScale = Math.Clamp(Math.Min(width / 1800f, height / 1100f), .78f, 1);
            ClientSize = new(Math.Min(1600, width - 40), Math.Min(960, (int)(height * .86f)));
            CenterWindow();
        }

        protected override void LoadScene()
        {
            VSync = VSyncMode.On;
            // The editor opens the scene the game starts from, missions and rules included.
            startupWork = WorldLoader.CreateStartWork(startupResult, enableRendering: false).GetEnumerator();
            float pixelsPerPoint = (float)FramebufferSize.X / Math.Max(1, ClientSize.X);
            ui = new ImGuiController(ImGuiController.Authoring.Scaled(uiScale), rasterScale: MathF.Round(pixelsPerPoint));
            EditorStyle.Apply(); if (uiScale < 1) ImGui.GetStyle().ScaleAllSizes(uiScale);
        }
        protected override void RenderScene(FrameEventArgs args)
        {
            if (sandbox == null) {
                long started = System.Diagnostics.Stopwatch.GetTimestamp(); int slices = 0;
                while (slices++ < 4096 && System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 4) {
                    if (startupWork.MoveNext()) continue;
                    startupWork.Dispose(); startupWork = null; sandbox = startupResult.Value;
                    if (worldFile != null) { EditorWorldLoader.Load(sandbox, worldFile); editor.UseWorldFile(worldFile); }
                    if (smoke) openingReference = OpeningSceneReference.Fingerprints(sandbox);
                    break;
                }
                if (sandbox == null) {
                    ui.Update(ClientSize.X, ClientSize.Y, FramebufferSize.X, FramebufferSize.Y, Vec2.One, (float)Math.Max(.001, args.Time));
                    ImGui.Begin("Pálya előkészítése"); ImGui.TextUnformatted("A tesztvilág szakaszosan épül fel…"); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit); ui.Render(); return;
                }
            }
            editor.AdvanceSandbox(sandbox, args.Time);
            Vec2 scale = new((float)FramebufferSize.X / Math.Max(1, ClientSize.X), (float)FramebufferSize.Y / Math.Max(1, ClientSize.Y));
            ui.Update(ClientSize.X, ClientSize.Y, FramebufferSize.X, FramebufferSize.Y, scale, (float)Math.Max(0.001, args.Time));
            ImGui.SetNextWindowPos(Vec2.Zero);
            ImGui.SetNextWindowSize(new Vec2(ClientSize.X, ClientSize.Y));
            editor.Draw(sandbox, ref open, true);
            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
            GL.ClearColor(.08f, .1f, .12f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            ui.Render();
            if (smoke && !sandbox.BoundaryPending && ++frames == 3)
            {
                var actual = OpeningSceneReference.Fingerprints(sandbox);
                foreach (string field in new[] { "Terrain", "Ecology", "Graphics" })
                    if (openingReference[field] != actual[field]) throw new InvalidOperationException("Editor changed opening scene: " + field);
                if (sandbox.RuleProjectDocument.World?.InitialState == null)
                    throw new InvalidOperationException("Editor did not retain the opening scene snapshot.");
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
            editor?.StopBackgroundOperations(sandbox);
            startupWork?.Dispose(); startupWork = null;
            ui?.Dispose(); sandbox?.Dispose(); ui = null; sandbox = null; }
    }
}
