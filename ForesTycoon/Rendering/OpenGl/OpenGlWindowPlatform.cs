using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ForesTycoon.OpenGl
{
    internal sealed class OpenGlWindowPlatform : IRenderWindowPlatform
    {
        public NativeWindowSettings CreateSettings(string title, Vector2i size, bool visible) => new() {
            Title = title, ClientSize = size, StartVisible = visible, WindowState = WindowState.Normal,
            API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), NumberOfSamples = 4,
            Profile = ContextProfile.Core, Flags = ContextFlags.ForwardCompatible };
        public void MakeCurrent(GameWindow window) => window.Context.MakeCurrent();
        public void SetSwapInterval(GameWindow window, int interval) => window.Context.SwapInterval = interval;
        public void Present(GameWindow window) => window.SwapBuffers();
        public void VerifyCurrent(GameWindow window)
        {
            if (!window.Context.IsCurrent) throw new InvalidOperationException("The owning window's OpenGL context must be current.");
        }
        public IDisposable Activate(GameWindow window) => new ContextScope(window);
        private sealed unsafe class ContextScope : IDisposable
        {
            private readonly Window* previous;
            private readonly Window* selected;
            private bool disposed;
            internal ContextScope(GameWindow window) {
                previous = GLFW.GetCurrentContext(); window.Context.MakeCurrent(); selected = GLFW.GetCurrentContext();
            }
            public void Dispose() {
                if (disposed) return;
                if (GLFW.GetCurrentContext() != selected) throw new InvalidOperationException("Native context scopes must close in reverse order.");
                GLFW.MakeContextCurrent(previous); disposed = true;
            }
        }
    }
}
