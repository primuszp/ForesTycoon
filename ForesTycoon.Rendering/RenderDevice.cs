using System;
using OpenTK.Mathematics;
using ForesTycoon.Rendering.OpenGl;
namespace ForesTycoon.Rendering
{
    /// <summary>Application render context. Native resources belong to the configured backend.</summary>
    internal static class RenderDevice
    {
        private static readonly RenderTransformState transforms = new();
        private static IGraphicsBackend backend = new OpenGlGraphicsBackend();
        private static bool backendLocked;
        internal static IGraphicsBackend Backend { get { backendLocked = true; return backend; } }
        private static bool initialized;
        public static Matrix4 ViewProjection => transforms.ViewProjection;
        public static Matrix4 Model => transforms.Model;
        internal static Vector2 LodRange = new(0, 1);
        internal static ISurfaceVisuals Visuals { get; set; }
        internal static event Action Disposing;
        // Configure at startup, before any backend resources are created; live switching is unsupported.
        internal static void Configure(IGraphicsBackend backend)
        {
            if (initialized || backendLocked) throw new InvalidOperationException("Configure the graphics backend before creating resources or initializing.");
            ArgumentNullException.ThrowIfNull(backend);
            if (!ReferenceEquals(RenderDevice.backend, backend)) RenderDevice.backend.Dispose();
            RenderDevice.backend = backend;
        }
        public static void Initialize() { if (initialized) return; Backend.Initialize(); initialized = true; }
        public static void SetCamera(Matrix4 camera) { EnsureInitialized(); transforms.SetCamera(camera); }
        internal static void SetViewProjection(Matrix4 camera) => transforms.SetViewProjection(camera);
        public static void UseGeometryShader() { EnsureInitialized(); Backend.UseGeometryShader(); }
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
            if (!initialized && !backendLocked) return;
            Disposing?.Invoke(); Disposing = null;
            Backend.Dispose(); initialized = false; backendLocked = false; transforms.Reset(); Visuals = null; LodRange = new(0, 1);
        }
        private static void EnsureInitialized() { if (!initialized) throw new InvalidOperationException("RenderDevice.Initialize must be called first."); }
    }
}
