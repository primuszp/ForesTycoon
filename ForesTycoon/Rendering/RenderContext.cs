namespace ForesTycoon
{
    public readonly struct RenderContext
    {
        public RenderContext(double totalTimeSeconds, float deltaTimeSeconds, ulong frameIndex,
            double simulationTimeSeconds, ulong simulationTick, float interpolationAlpha,
            bool showNodeMarker, bool showTileHighlight, float nodeMarkerRadius)
        {
            TotalTimeSeconds = totalTimeSeconds;
            DeltaTimeSeconds = deltaTimeSeconds;
            FrameIndex = frameIndex;
            SimulationTimeSeconds = simulationTimeSeconds;
            SimulationTick = simulationTick;
            InterpolationAlpha = interpolationAlpha;
            ShowNodeMarker = showNodeMarker;
            ShowTileHighlight = showTileHighlight;
            NodeMarkerRadius = nodeMarkerRadius;
        }

        public double TotalTimeSeconds { get; }
        public float DeltaTimeSeconds { get; }
        public ulong FrameIndex { get; }
        public double SimulationTimeSeconds { get; }
        public ulong SimulationTick { get; }
        public float InterpolationAlpha { get; }
        public bool ShowNodeMarker { get; }
        public bool ShowTileHighlight { get; }
        public float NodeMarkerRadius { get; }
    }
}
