using System;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon.Editor
{
    internal static class WindowLifecycleSmokeTest
    {
        internal static void Run()
        {
            using var game = new Viewport(smokeTestFrameLimit: 100, visible: false);
            using var preview = new ForestPreviewWindow(visible: false);
            using var editor = new EditorWindow(null, visible: false);
            using var sentinel = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(32, 32), API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core
            });
            sentinel.Context.MakeCurrent();
            var original = RenderDevice.Environment; var originalUi = ImGui.GetCurrentContext();
            void Restored() {
                Require(sentinel.Context.IsCurrent, "Application callback did not restore the previous native context.");
                Require(ReferenceEquals(original, RenderDevice.Environment), "Application callback did not restore the previous render environment.");
                Require(ImGui.GetCurrentContext() == originalUi, "Application callback did not restore the previous UI context.");
            }
            foreach (var window in new RenderGameWindow[] { game, preview, editor }) { window.LoadForSmoke(); Restored(); }
            Exception foreignThreadError = null;
            var foreignThread = new System.Threading.Thread(() => {
                try { game.FrameForSmoke(); } catch (Exception error) { foreignThreadError = error; }
            });
            foreignThread.Start(); foreignThread.Join();
            Require(foreignThreadError is InvalidOperationException, "Foreign-thread callback was not rejected before context activation."); Restored();
            Require(!ReferenceEquals(game.OwnedEnvironment, preview.OwnedEnvironment) && !ReferenceEquals(game.OwnedEnvironment, editor.OwnedEnvironment), "Application windows share a render environment.");
            for (int i = 0; i < 10; i++) {
                game.FrameForSmoke(); Restored(); preview.FrameForSmoke(); Restored(); editor.FrameForSmoke(); Restored();
            }
            foreach (var window in new RenderGameWindow[] { game, preview, editor }) { window.InputForSmoke(); Restored(); }
            int gameProgram = 0; IntPtr gameUi = IntPtr.Zero, editorUi = IntPtr.Zero;
            game.InspectForSmoke(() => { RenderDevice.UseGeometryShader(); GL.GetInteger(GetPName.CurrentProgram, out gameProgram); gameUi = ImGui.GetCurrentContext(); }); Restored();
            editor.InspectForSmoke(() => editorUi = ImGui.GetCurrentContext()); Restored();
            Require(gameUi != IntPtr.Zero && editorUi != IntPtr.Zero && gameUi != editorUi, "Game/editor share a UI context.");
            preview.UnloadForSmoke(); preview.UnloadForSmoke(); Restored(); RequireDisposed(preview);
            game.FrameForSmoke(); Restored();
            game.InspectForSmoke(() => Require(GL.IsProgram(gameProgram), "Closing preview deleted the game's shader.")); Restored();
            game.UnloadForSmoke(); Restored(); RequireDisposed(game);
            editor.FrameForSmoke(); Restored(); editor.InputForSmoke(); Restored();
            using (var failed = new FailedLoadWindow()) {
                sentinel.Context.MakeCurrent(); bool rejected = false;
                try { failed.LoadForSmoke(); } catch (ApplicationException) { rejected = true; }
                Require(rejected, "Injected scene-load failure was not propagated."); Restored(); RequireDisposed(failed);
                using (RenderBackendSelection.Window.Activate(failed)) Require(!GL.IsProgram(failed.Program), "Failed scene load retained its shader.");
                Restored();
            }
            sentinel.Context.MakeCurrent(); editor.FrameForSmoke(); Restored();
            editor.UnloadForSmoke(); Restored(); RequireDisposed(editor);
            Console.WriteLine("Application window lifecycle: game/preview/editor interleaved load, frames, input, resize, UI/context restoration, independent close and failed-load cleanup passed.");
        }

        private sealed class FailedLoadWindow : RenderGameWindow
        {
            private ImGuiController ui;
            private VertexBuffer buffer;
            internal int Program;
            protected override ImGuiController UiController => ui;
            internal FailedLoadWindow() : base(GameWindowSettings.Default, RenderBackendSelection.Window.CreateSettings("Failure fixture", new Vector2i(32), false)) { }
            protected override void LoadScene() {
                ui = new ImGuiController(); buffer = new VertexBuffer(PrimitiveTopology.Triangles);
                buffer.SetData(new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, 0xffffffff) });
                RenderDevice.UseGeometryShader(); GL.GetInteger(GetPName.CurrentProgram, out Program);
                throw new ApplicationException("Injected scene-load failure.");
            }
            protected override void RenderScene(FrameEventArgs e) { }
            protected override void UnloadScene() { ui?.Dispose(); buffer?.Dispose(); }
        }
        private static void RequireDisposed(RenderGameWindow window) {
            bool rejected = false; try { window.OwnedEnvironment.Activate(); } catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "Closed/failed window retained an activatable render environment.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
