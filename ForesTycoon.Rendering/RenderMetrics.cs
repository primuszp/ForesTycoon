using System.Threading;

namespace ForesTycoon.Rendering
{
    /// <summary>Low-overhead counters populated by renderer entry points.</summary>
    static class RenderMetrics
    {
        private sealed class Counters { internal int DrawCalls, SubmittedVertices; }
        private static readonly object stateKey = new();
        private static Counters State => RenderDevice.GetState(stateKey, () => new Counters());

        public static void BeginFrame()
        {
            State.DrawCalls = 0;
            State.SubmittedVertices = 0;
        }

        public static void RecordDraw(int vertexCount = 0)
        {
            State.DrawCalls++;
            State.SubmittedVertices += vertexCount;
        }

        public static int DrawCalls => Volatile.Read(ref State.DrawCalls);
        public static int SubmittedVertices => Volatile.Read(ref State.SubmittedVertices);
    }
}
