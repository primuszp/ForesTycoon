using System;
using ImGuiNET;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    /// <summary>Each application window owns its backend bundle and runs every callback in its context.</summary>
    internal abstract class RenderGameWindow : GameWindow
    {
        private readonly RenderBackendBundle bundle;
        private readonly RenderEnvironment environment;
        private bool loaded, released;
        protected virtual ImGuiController UiController => null;
        internal RenderEnvironment OwnedEnvironment => environment;
        protected RenderGameWindow(GameWindowSettings game, NativeWindowSettings native) : base(game, native)
        {
            try {
                bundle = RenderBackendSelection.CreateWindowBundle();
                environment = new RenderEnvironment(bundle.Graphics, () => bundle.Window.VerifyCurrent(this));
            }
            catch { base.Dispose(); throw; }
        }

        private void Dispatch(Action action)
        {
            environment.VerifyThreadAccess();
            using var native = bundle.Window.Activate(this);
            using var managed = environment.Activate();
            var ui = UiController; var previousUi = ImGui.GetCurrentContext();
            try { ui?.MakeCurrent(); action(); }
            finally { ImGui.SetCurrentContext(ui != null && ui.IsDisposed && previousUi == ui.Context ? IntPtr.Zero : previousUi); }
        }
        protected sealed override void OnLoad()
        {
            if (loaded || released) throw new InvalidOperationException("An application window can only load once.");
            Dispatch(() => {
                try {
                    RenderBackendSelection.Configure(bundle);
                    RenderDevice.Initialize(); RenderDevice.InitializeFrameState();
                    base.OnLoad(); LoadScene(); loaded = true;
                }
                catch {
                    try { UnloadScene(); } finally { environment.Dispose(); released = true; }
                    throw;
                }
            });
        }
        protected sealed override void OnRenderFrame(FrameEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnRenderFrame(e); RenderScene(e); }); }
        protected sealed override void OnUnload()
        {
            if (released || environment == null) return;
            Dispatch(() => {
                try { UnloadScene(); base.OnUnload(); }
                finally { environment.Dispose(); released = true; loaded = false; }
            });
        }
        protected sealed override void OnResize(ResizeEventArgs e) { if (loaded) Dispatch(() => { base.OnResize(e); ResizeScene(e); }); else base.OnResize(e); }
        protected sealed override void OnMouseMove(MouseMoveEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnMouseMove(e); MouseMoveScene(e); }); }
        protected sealed override void OnMouseDown(MouseButtonEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnMouseDown(e); MouseDownScene(e); }); }
        protected sealed override void OnMouseUp(MouseButtonEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnMouseUp(e); MouseUpScene(e); }); }
        protected sealed override void OnMouseWheel(MouseWheelEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnMouseWheel(e); MouseWheelScene(e); }); }
        protected sealed override void OnKeyDown(KeyboardKeyEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnKeyDown(e); KeyDownScene(e); }); }
        protected sealed override void OnKeyUp(KeyboardKeyEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnKeyUp(e); KeyUpScene(e); }); }
        protected sealed override void OnTextInput(TextInputEventArgs e) { if (loaded && !released) Dispatch(() => { base.OnTextInput(e); TextInputScene(e); }); }
        protected abstract void LoadScene();
        protected abstract void RenderScene(FrameEventArgs e);
        protected abstract void UnloadScene();
        protected virtual void ResizeScene(ResizeEventArgs e) { }
        protected virtual void MouseMoveScene(MouseMoveEventArgs e) { }
        protected virtual void MouseDownScene(MouseButtonEventArgs e) { }
        protected virtual void MouseUpScene(MouseButtonEventArgs e) { }
        protected virtual void MouseWheelScene(MouseWheelEventArgs e) { }
        protected virtual void KeyDownScene(KeyboardKeyEventArgs e) { }
        protected virtual void KeyUpScene(KeyboardKeyEventArgs e) { }
        protected virtual void TextInputScene(TextInputEventArgs e) { }

        // Diagnostics use the actual callback dispatch without entering three nested event loops.
        internal void LoadForSmoke() => OnLoad();
        internal void FrameForSmoke() => OnRenderFrame(new FrameEventArgs(1.0 / 60));
        internal void UnloadForSmoke() => OnUnload();
        internal void InspectForSmoke(Action callback) => Dispatch(callback);
        internal void InputForSmoke() {
            OnMouseMove(new MouseMoveEventArgs(4, 5, 1, 1)); OnMouseWheel(new MouseWheelEventArgs(0, 1));
            OnTextInput(new TextInputEventArgs('ő')); OnResize(new ResizeEventArgs(ClientSize));
        }
    }
}
