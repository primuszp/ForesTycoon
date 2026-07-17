using System;

namespace ForesTycoon
{
    /// <summary>Keeps the camera's rendered world window bounded independently from total map size.</summary>
    static class MapViewConstraints
    {
        public static float MinimumZoomForTileWindow(int viewportWidth, int viewportHeight,
            int tileWidth, int tileHeight, int maximumVisibleTiles)
        {
            if (viewportWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
            if (viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));
            if (tileWidth <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidth));
            if (tileHeight <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeight));
            if (maximumVisibleTiles <= 0) throw new ArgumentOutOfRangeException(nameof(maximumVisibleTiles));

            float maximumWorldWidth = tileWidth * maximumVisibleTiles;
            float maximumWorldHeight = tileHeight * maximumVisibleTiles;
            return Math.Max(viewportWidth / maximumWorldWidth, viewportHeight / maximumWorldHeight);
        }
    }
}
