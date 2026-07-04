namespace ForesTycoon
{
    public readonly struct RenderContext
    {
        public RenderContext(double totalTimeSeconds, float deltaTimeSeconds, ulong frameIndex, bool showNodeMarker)
        {
            TotalTimeSeconds = totalTimeSeconds;
            DeltaTimeSeconds = deltaTimeSeconds;
            FrameIndex = frameIndex;
            ShowNodeMarker = showNodeMarker;
        }

        public double TotalTimeSeconds { get; }
        public float DeltaTimeSeconds { get; }
        public ulong FrameIndex { get; }
        public bool ShowNodeMarker { get; }
    }
}
