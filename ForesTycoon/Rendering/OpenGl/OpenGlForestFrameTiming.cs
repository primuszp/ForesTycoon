using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.OpenGl
{
    /// <summary>Sample a frame and its forest pass without waiting on the render thread.</summary>
    internal sealed class OpenGlForestFrameTiming : IForestFrameTiming
    {
        private const int SampleStride = 4;
        private const long MiB = 1024L * 1024;
        private const GetPName TotalMemoryNvx = (GetPName)0x9048;
        private const GetPName AvailableMemoryNvx = (GetPName)0x9049;

        private sealed class Slot
        {
            internal readonly int Start = GL.GenQuery(), ForestStart = GL.GenQuery(),
                ForestEnd = GL.GenQuery(), End = GL.GenQuery();
            internal bool ForestMarked;
            internal int Epoch;
            internal double Time;
            internal ForestFrameWorkload Workload;

            internal void Delete()
            {
                GL.DeleteQuery(Start);
                GL.DeleteQuery(ForestStart);
                GL.DeleteQuery(ForestEnd);
                GL.DeleteQuery(End);
            }
        }

        private readonly RenderResourceOwner owner = new();
        private readonly Slot[] slots = { new(), new(), new(), new() };
        private readonly long totalMemory;
        private Slot current;
        private int read, write, count, epoch;
        private bool disposed;

        internal OpenGlForestFrameTiming()
        {
            owner.Check();
            GL.GetInteger(GetPName.NumExtensions, out int extensions);
            for (int i = 0; i < extensions; i++) {
                if (GL.GetString(StringNameIndexed.Extensions, i) != "GL_NVX_gpu_memory_info") continue;
                GL.GetInteger(TotalMemoryNvx, out int kiB);
                totalMemory = kiB * 1024L;
                break;
            }
        }

        public bool Recording => current != null;

        public void BeginFrame(ulong frame)
        {
            if (frame % SampleStride != 0 || count == slots.Length) return;
            owner.Check();
            current = slots[write];
            current.Epoch = epoch;
            current.ForestMarked = false;
            Stamp(current.Start);
        }

        public void MarkForest(bool begin)
        {
            if (current == null) return;
            Stamp(begin ? current.ForestStart : current.ForestEnd);
            if (!begin) current.ForestMarked = true;
        }

        public void EndFrame(double time, ForestFrameWorkload workload)
        {
            if (current == null) return;
            Stamp(current.End);
            current.Time = time;
            current.Workload = workload;
            current = null;
            write = (write + 1) % slots.Length;
            count++;
        }

        public bool TryRead(out ForestGpuSample sample)
        {
            sample = default;
            while (count != 0) {
                owner.Check();
                var slot = slots[read];
                GL.GetQueryObject(slot.End, GetQueryObjectParam.QueryResultAvailable, out int available);
                if (available == 0) return false;
                read = (read + 1) % slots.Length;
                count--;
                if (slot.Epoch != epoch || !slot.ForestMarked) continue;
                sample = new(slot.Time, Duration(slot.Start, slot.End), Duration(slot.ForestStart, slot.ForestEnd),
                    slot.Workload.Lod, slot.Workload.Ready, slot.Workload.NearFits);
                return true;
            }
            return false;
        }

        public long NearBudgetBytes(long forestGpuBytes)
        {
            if (totalMemory <= 0) return ForestQualityController.DefaultNearBudget;
            owner.Check();
            GL.GetInteger(AvailableMemoryNvx, out int available);
            long headroom = available >= 0 ? forestGpuBytes + available * 768L : totalMemory / 2;
            return Math.Max(128 * MiB, Math.Min(totalMemory / 2, headroom));
        }

        public void Discard() => epoch++;

        public void Dispose()
        {
            if (disposed) return;
            owner.CheckIfBound();
            foreach (var slot in slots) slot.Delete();
            disposed = true;
        }

        private static void Stamp(int query) => GL.QueryCounter(query, QueryCounterTarget.Timestamp);
        private static double Duration(int start, int end)
        {
            // End-of-frame availability also covers the three earlier timestamps in this slot.
            GL.GetQueryObject(start, GetQueryObjectParam.QueryResult, out long a);
            GL.GetQueryObject(end, GetQueryObjectParam.QueryResult, out long b);
            return (b - a) / 1_000_000.0;
        }
    }
}
