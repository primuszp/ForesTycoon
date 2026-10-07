using System.Threading;

namespace ForesTycoon.Rendering
{
    /// <summary>Low-overhead counters populated by renderer entry points.</summary>
    static class RenderMetrics
    {
        private static int drawCalls;
        private static int submittedVertices;

        public static void BeginFrame()
        {
            drawCalls = 0;
            submittedVertices = 0;
        }

        public static void RecordDraw(int vertexCount = 0)
        {
            drawCalls++;
            submittedVertices += vertexCount;
        }

        public static int DrawCalls => Volatile.Read(ref drawCalls);
        public static int SubmittedVertices => Volatile.Read(ref submittedVertices);
    }
}
