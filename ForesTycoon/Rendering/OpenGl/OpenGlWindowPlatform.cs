using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

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
    }
}
