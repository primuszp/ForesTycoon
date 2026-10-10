using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    // Diagnostic only. Timestamp results are read after the benchmark's GL.Finish;
    // the production renderer never waits for a query or creates these objects.
    internal sealed class RenderPassProfiler : IDisposable
    {
        private readonly RenderEnvironment owner = RenderDevice.Environment;
        private bool disposed;
        internal readonly record struct Timing(string Name, double CpuMs, double GpuMs);
        private sealed class Pass
        {
            internal string Name;
            internal int StartQuery = GL.GenQuery(), EndQuery = GL.GenQuery();
            internal long Started;
            internal double CpuMs;
        }
        private readonly Dictionary<string, Pass> passes = new();
        private readonly List<Pass> frame = new();
        private readonly Action<string, bool> previous = RenderPipeline.PassProbe;
        internal RenderPassProfiler() { owner.VerifyAccess(); RenderPipeline.PassProbe = Probe; }
        internal void BeginFrame() { VerifyAccess(); frame.Clear(); }
        internal int[] CaptureQueries() {
            var result = new int[passes.Count * 2]; int i = 0;
            foreach (var pass in passes.Values) { result[i++] = pass.StartQuery; result[i++] = pass.EndQuery; }
            return result;
        }
        private void VerifyAccess() { ObjectDisposedException.ThrowIf(disposed, this); owner.VerifyAccess(); }
        private void Probe(string name, bool begin)
        {
            VerifyAccess();
            previous?.Invoke(name, begin);
            if (!passes.TryGetValue(name, out var pass))
                passes.Add(name, pass = new Pass { Name = name });
            if (begin)
            {
                frame.Add(pass);
                pass.Started = Stopwatch.GetTimestamp();
                GL.QueryCounter(pass.StartQuery, QueryCounterTarget.Timestamp);
            }
            else
            {
                GL.QueryCounter(pass.EndQuery, QueryCounterTarget.Timestamp);
                pass.CpuMs = Stopwatch.GetElapsedTime(pass.Started).TotalMilliseconds;
            }
        }
        internal Timing[] ReadCompletedFrame()
        {
            VerifyAccess();
            var result = new Timing[frame.Count];
            for (int i = 0; i < frame.Count; i++)
            {
                var pass = frame[i];
                GL.GetQueryObject(pass.StartQuery, GetQueryObjectParam.QueryResult, out long start);
                GL.GetQueryObject(pass.EndQuery, GetQueryObjectParam.QueryResult, out long end);
                result[i] = new(pass.Name, pass.CpuMs, (end - start) / 1_000_000.0);
            }
            return result;
        }
        public void Dispose()
        {
            if (disposed) return; VerifyAccess();
            if (RenderPipeline.PassProbe != Probe) throw new InvalidOperationException("Render pass profilers must close in reverse order.");
            RenderPipeline.PassProbe = previous;
            foreach (var pass in passes.Values)
            {
                GL.DeleteQuery(pass.StartQuery); GL.DeleteQuery(pass.EndQuery);
            }
            passes.Clear(); frame.Clear(); disposed = true;
        }
    }
}
