namespace ForesTycoon
{
    public readonly struct RenderContext
    {
        public RenderContext(double totalTimeSeconds, float deltaTimeSeconds, ulong frameIndex,
            double simulationTimeSeconds, ulong simulationTick, float interpolationAlpha,
            bool showNodeMarker, bool showTileHighlight, float nodeMarkerRadius,
            float cameraTilt, float cameraYaw, double viewMinX, double viewMinY, double viewMaxX, double viewMaxY,
            float pixelsPerWorldUnit = 8f)
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
            CameraTilt = cameraTilt;
            CameraYaw = cameraYaw;
            ViewMinX = viewMinX;
            ViewMinY = viewMinY;
            ViewMaxX = viewMaxX;
            ViewMaxY = viewMaxY;
            PixelsPerWorldUnit = pixelsPerWorldUnit;
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
        public float CameraTilt { get; }
        public float CameraYaw { get; }
        public double ViewMinX { get; }
        public double ViewMinY { get; }
        public double ViewMaxX { get; }
        public double ViewMaxY { get; }
        public float PixelsPerWorldUnit { get; }
    }
}
