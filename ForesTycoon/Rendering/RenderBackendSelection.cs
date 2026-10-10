using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal interface IRenderWindowPlatform
    {
        NativeWindowSettings CreateSettings(string title, Vector2i size, bool visible);
        void MakeCurrent(GameWindow window);
        void SetSwapInterval(GameWindow window, int interval);
        void Present(GameWindow window);
        IDisposable Activate(GameWindow window);
        void VerifyCurrent(GameWindow window);
    }

    /// <summary>One coherent graphics implementation, installed before constructing any render resources.</summary>
    internal sealed record RenderBackendBundle(IGraphicsBackend Graphics,
        Func<AnimatedGlbModel, IModelRenderBackend> Models,
        IEffectRenderBackendFactory Effects, ISceneRenderBackendFactory Scene, IRenderWindowPlatform Window,
        Func<RenderBackendBundle> CreateSibling = null);

    internal static class RenderBackendSelection
    {
        private sealed class WindowState {
            internal IRenderWindowPlatform Platform = new OpenGl.OpenGlWindowPlatform();
            internal Func<RenderBackendBundle> CreateSibling = OpenGl.OpenGlRenderBackendBundle.Create;
        }
        private static readonly object windowKey = new();
        internal static IRenderWindowPlatform Window {
            get => RenderDevice.GetState(windowKey, () => new WindowState()).Platform;
            private set => RenderDevice.GetState(windowKey, () => new WindowState()).Platform = value;
        }

        internal static void UseOpenGl() => Configure(OpenGl.OpenGlRenderBackendBundle.Create());
        internal static RenderBackendBundle CreateWindowBundle() =>
            (RenderDevice.GetState(windowKey, () => new WindowState()).CreateSibling
                ?? throw new InvalidOperationException("The selected backend must provide a fresh bundle factory for application windows."))();

        internal static void Configure(RenderBackendBundle bundle)
        {
            ArgumentNullException.ThrowIfNull(bundle);
            ArgumentNullException.ThrowIfNull(bundle.Graphics);
            ArgumentNullException.ThrowIfNull(bundle.Models);
            ArgumentNullException.ThrowIfNull(bundle.Effects);
            ArgumentNullException.ThrowIfNull(bundle.Scene);
            ArgumentNullException.ThrowIfNull(bundle.Window);
            // Core configuration validates the lifecycle before changing any other factory.
            RenderDevice.Configure(bundle.Graphics);
            ModelRenderBackends.Create = bundle.Models;
            EffectRenderBackends.Current = bundle.Effects;
            SceneRenderBackends.Current = bundle.Scene;
            Window = bundle.Window;
            RenderDevice.GetState(windowKey, () => new WindowState()).CreateSibling = bundle.CreateSibling;
        }
    }
}
