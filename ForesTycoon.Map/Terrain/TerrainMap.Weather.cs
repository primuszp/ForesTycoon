using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    /// <summary>The ground as seen by the weather: the footprint and a coarse height field including tree canopy.</summary>
    internal sealed partial class TerrainMap
    {
        internal void GetWeatherBounds(out Vector3 min, out Vector3 max)
        {
            // Map footprint is stable; no per-frame scan through every node.
            min = new Vector3(nodes[0].xPos, nodes[0].yPos, 0);
            max = new Vector3(nodes[^1].xPos, nodes[^1].yPos, settings.MaxHeight * settings.HeightScale + 22);
        }

        internal void FillWeatherHeights(float[] heights, ForestSystem forest)
        {
            foreach (Tile tile in tiles)
            {
                float z = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos), Math.Max(tile.E.zPos, tile.N.zPos));
                z = Math.Max(z, settings.SeaLevel);
                if (forest.TryGetStand(tile.Id, out ForestStand stand))
                    z += 2 + 6 * Math.Clamp(stand.Maturity, 0, 1.5f);
                // Tile IDs are column-major; OpenGL images are row-major.
                int u = tile.Id / settings.TileRows, v = tile.Id % settings.TileRows;
                heights[v * settings.TileColumns + u] = z;
            }
        }

    }
}
