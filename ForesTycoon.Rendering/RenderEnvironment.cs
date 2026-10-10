using System;
using System.Collections.Generic;

namespace ForesTycoon.Rendering
{
    /// <summary>One thread-owned graphics device, its frame state and render services.</summary>
    internal sealed class RenderEnvironment : IDisposable
    {
        private readonly int threadId = Environment.CurrentManagedThreadId;
        private readonly Action validateNativeContext;
        private readonly Dictionary<object, object> services = new();
        private bool disposed;
        internal IGraphicsBackend Backend;
        internal bool BackendLocked, Initialized, BackendReleased;
        internal readonly RenderTransformState Transforms = new();
        internal OpenTK.Mathematics.Vector2 LodRange = new(0, 1);
        internal ISurfaceVisuals Visuals;
        internal Action Disposing;

        internal RenderEnvironment(IGraphicsBackend backend, Action validateNativeContext = null)
        {
            Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.validateNativeContext = validateNativeContext;
        }

        internal IDisposable Activate()
        {
            VerifyThreadAccess();
            validateNativeContext?.Invoke();
            return RenderDevice.Activate(this);
        }

        internal void VerifyAccess()
        {
            VerifyThread();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!ReferenceEquals(RenderDevice.Environment, this))
                throw new InvalidOperationException("The resource's render environment must be active.");
            validateNativeContext?.Invoke();
        }
        internal void VerifyThreadAccess() { VerifyThread(); ObjectDisposedException.ThrowIf(disposed, this); }

        internal T GetState<T>(object key, Func<T> create) where T : class
        {
            VerifyThread();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!services.TryGetValue(key, out var state)) services.Add(key, state = create());
            return (T)state;
        }

        internal void ReleaseResources()
        {
            VerifyAccess();
            // Cleanup callbacks run with the owning native context still current.
            Disposing?.Invoke();
            if (!BackendReleased) Backend.Dispose();
            BackendReleased = true;
            Disposing = null; Initialized = BackendLocked = false;
            Transforms.Reset(); Visuals = null; LodRange = new(0, 1); services.Clear();
        }

        public void Dispose()
        {
            VerifyThread();
            if (disposed) return;
            ReleaseResources(); disposed = true;
        }

        private void VerifyThread()
        {
            if (Environment.CurrentManagedThreadId != threadId)
                throw new InvalidOperationException("Render environments cannot move between threads.");
        }
    }
}
