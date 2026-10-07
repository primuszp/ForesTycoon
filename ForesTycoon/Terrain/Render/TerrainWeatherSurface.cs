using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>Presents the terrain and its canopy to the weather effects as a height field.</summary>
    internal sealed class TerrainWeatherSurface : IWeatherSurface
    {
        private readonly Terrain terrain;
        private readonly ForestSystem forest;
        internal TerrainWeatherSurface(Terrain terrain, ForestSystem forest) { this.terrain = terrain; this.forest = forest; }
        public int Columns => terrain.Settings.TileColumns;
        public int Rows => terrain.Settings.TileRows;
        public ulong Revision => unchecked(terrain.WeatherSurfaceRevision * 1000003UL + forest.Revision);
        public void GetBounds(out Vector3 min, out Vector3 max) => terrain.GetWeatherBounds(out min, out max);
        public void GetVisibleBounds(out Vector2 min, out Vector2 max) => terrain.GetVisibleWeatherBounds(out min, out max);
        public void FillHeights(float[] heights) => terrain.FillWeatherHeights(heights, forest);
    }
}
