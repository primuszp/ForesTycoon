using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    /// <summary>Building footprints and the road docking/pathing the logistics layer asks of the ground.</summary>
    internal sealed partial class TerrainMap
    {
        internal void SetBuildingFootprint(int[] ids) { foreach (int id in ids) buildingTiles.Add(id); }
        internal bool IsBuildingTile(int id) => buildingTiles.Contains(id);

        internal bool TryGetSawmillFootprint(int id, out int[] footprint, out Vector3 position)
        {
            footprint = Array.Empty<int>(); position = default;
            if (!IsValidTileId(id)) return false;
            int u = id / (nodeRows - 1), v = id % (nodeRows - 1);
            if (!CheckTile(u + 1, v + 1)) return false;
            footprint = new[] { id, GetTile(u + 1, v).Id, GetTile(u, v + 1).Id, GetTile(u + 1, v + 1).Id };
            float low = float.MaxValue, high = float.MinValue;
            foreach (int tileId in footprint)
            {
                Tile tile = tiles[tileId];
                if (roads.Has(tileId) || ShouldDrawStandingWater(tile) || CountRiverCorners(tile) > 0 || buildingTiles.Contains(tileId)) return false;
                foreach (Node node in new[] { tile.W, tile.S, tile.E, tile.N }) { low = Math.Min(low, node.zPos); high = Math.Max(high, node.zPos); }
            }
            if (high - low > 0.05f) return false;
            TryGetTileCenter(id, out var first); TryGetTileCenter(footprint[3], out var last); position = (first + last) * 0.5f; return true;
        }

        internal List<int> FindRoadDocks(int[] ids)
        {
            var result = new List<int>();
            foreach (int id in ids)
                foreach (Tile tile in data.GetAdjacentTiles(tiles[id]))
                    if (roads.Has(tile.Id) && !result.Contains(tile.Id)) result.Add(tile.Id);
            result.Sort();
            return result;
        }

        internal int[] FindLogisticsRoadPath(int start, int end) => RoadPathfinder.FindPath(roads, nodeRows - 1, start, end);
    }
}
