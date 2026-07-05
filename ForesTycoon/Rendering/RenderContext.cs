namespace ForesTycoon
{
    public readonly struct RenderContext
    {
        public RenderContext(double totalTimeSeconds, float deltaTimeSeconds, ulong frameIndex, bool showNodeMarker, bool showTileHighlight, float nodeMarkerRadius)
        {
            TotalTimeSeconds = totalTimeSeconds;
            DeltaTimeSeconds = deltaTimeSeconds;
            FrameIndex = frameIndex;
            ShowNodeMarker = showNodeMarker;
            ShowTileHighlight = showTileHighlight;
            NodeMarkerRadius = nodeMarkerRadius;
        }

        public double TotalTimeSeconds { get; }
        public float DeltaTimeSeconds { get; }
        public ulong FrameIndex { get; }
        public bool ShowNodeMarker { get; }
        public bool ShowTileHighlight { get; }
        public float NodeMarkerRadius { get; }
    }
}
