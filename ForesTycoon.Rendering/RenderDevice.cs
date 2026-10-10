using System;
using OpenTK.Mathematics;
using ForesTycoon.Rendering.OpenGl;
namespace ForesTycoon.Rendering
{
    /// <summary>Application render context. Native resources belong to the configured backend.</summary>
    internal static class RenderDevice
    {
        [ThreadStatic] private static RenderEnvironment active;
        [ThreadStatic] private static RenderEnvironment defaultEnvironment;
        internal static RenderEnvironment Environment => active ?? (defaultEnvironment ??= new(new OpenGlGraphicsBackend()));
        internal static T GetState<T>(object key, Func<T> create) where T : class => Environment.GetState(key, create);
        private static RenderTransformState transforms => Environment.Transforms;
        private static IGraphicsBackend backend { get => Environment.Backend; set => Environment.Backend = value; }
        private static bool backendLocked { get => Environment.BackendLocked; set => Environment.BackendLocked = value; }
        internal static IGraphicsBackend Backend { get { Environment.VerifyAccess(); backendLocked = true; Environment.BackendReleased = false; return backend; } }
        private static bool initialized { get => Environment.Initialized; set => Environment.Initialized = value; }
        public static Matrix4 ViewProjection => transforms.ViewProjection;
        public static Matrix4 Model => transforms.Model;
        internal static Vector2 LodRange { get => Environment.LodRange; set => Environment.LodRange = value; }
        internal static ISurfaceVisuals Visuals { get => Environment.Visuals; set => Environment.Visuals = value; }
        internal static event Action Disposing { add => Environment.Disposing += value; remove => Environment.Disposing -= value; }
        internal static IDisposable Activate(RenderEnvironment environment)
        {
            var scope = new ActivationScope(active, environment);
            active = environment; return scope;
        }
        private sealed class ActivationScope : IDisposable
        {
            private readonly RenderEnvironment previous, selected;
            private bool disposed;
            internal ActivationScope(RenderEnvironment previous, RenderEnvironment selected) { this.previous = previous; this.selected = selected; }
            public void Dispose()
            {
                if (disposed) return;
                if (!ReferenceEquals(active, selected)) throw new InvalidOperationException("Render environment scopes must close in reverse order.");
                active = previous; disposed = true;
            }
        }
        // Configure at startup, before any backend resources are created; live switching is unsupported.
        internal static void Configure(IGraphicsBackend backend)
        {
            Environment.VerifyAccess();
            if (initialized || backendLocked) throw new InvalidOperationException("Configure the graphics backend before creating resources or initializing.");
            ArgumentNullException.ThrowIfNull(backend);
            if (!ReferenceEquals(RenderDevice.backend, backend) && !Environment.BackendReleased) RenderDevice.backend.Dispose();
            RenderDevice.backend = backend;
            Environment.BackendReleased = false;
        }
        public static void Initialize() { if (initialized) return; Backend.Initialize(); initialized = true; }
        public static void SetCamera(Matrix4 camera) { EnsureInitialized(); transforms.SetCamera(camera); }
        internal static void SetViewProjection(Matrix4 camera) => transforms.SetViewProjection(camera);
        public static void UseGeometryShader() { EnsureInitialized(); Backend.UseGeometryShader(); }
        internal static void UseScreenLineShader(float widthPixels, Vector4 colorOverride = default) => Backend.UseScreenLineShader(widthPixels, colorOverride);
        public static void PushModel() => transforms.PushModel();
        public static void PopModel() => transforms.PopModel();
        internal static void SetModel(Matrix4 model) => transforms.SetModel(model);
        public static void Translate(float x, float y, float z) => transforms.Translate(x, y, z);
        internal static RenderStateScope CreateStateScope() => Backend.CreateStateScope();
        internal static IForestStateBuffer CreateForestStateBuffer() => Backend.CreateForestStateBuffer();
        internal static void InitializeFrameState() => Backend.InitializeFrameState();
        internal static void SetViewport(int width, int height) => Backend.SetViewport(width, height);
        internal static void Clear(Vector4 color) => Backend.Clear(color);
        internal static void ReadPixels(int x, int y, int width, int height, byte[] rgba) => Backend.ReadPixels(x, y, width, height, rgba);
        internal static void CheckErrors(string operation) => Backend.CheckErrors(operation);
        public static void Dispose()
        {
            Environment.ReleaseResources();
        }
        private static void EnsureInitialized() { if (!initialized) throw new InvalidOperationException("RenderDevice.Initialize must be called first."); }
    }
}
