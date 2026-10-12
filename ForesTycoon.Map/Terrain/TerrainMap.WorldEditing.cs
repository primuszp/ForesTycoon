using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Map
{
    /// <summary>World-editor terrain: absolute corner heights painted on a generated map, and the queries the editor draws.</summary>
    internal sealed partial class TerrainMap
    {
        /// <summary>The highest corner height that lies below sea level: corners at or below it hold water.</summary>
        public int WaterHeight => Math.Max(0, (int)Math.Ceiling(settings.SeaLevel / tileSizeM) - 1);
        public int MaximumHeight => settings.MaxHeight;
        public int NodeHeight(int nodeId) => (uint)nodeId < (uint)nodes.Length ? nodes[nodeId].W : 0;
        /// <summary>The four corner node IDs of a tile.</summary>
        public int[] TileCorners(int tileId)
        {
            if (!IsValidTileId(tileId)) return Array.Empty<int>();
            var t = tiles[tileId];
            return new[] { t.N.Id, t.E.Id, t.S.Id, t.W.Id };
        }
        /// <summary>Standing water or a river on the tile.</summary>
        public bool IsWaterTile(int tileId) => IsValidTileId(tileId) && (ShouldDrawStandingWater(tiles[tileId]) || hydro.CountRiverCorners(tiles[tileId]) >= 2);

        /// <summary>
        /// Moves each corner to its target height in ID order, keeping neighbouring corners within one step, then
        /// rebuilds the water once. Targets are clamped to the map's height range. Returns the changed tiles.
        /// </summary>
        public int[] ApplyHeightTargets(IReadOnlyDictionary<int, int> targets)
        {
            if (targets == null || targets.Count == 0) return Array.Empty<int>();
            var changedTiles = new HashSet<int>();
            elevationEditTiles = changedTiles;
            suppressHydrologyRebuild = true;
            try {
                foreach (var (id, target) in targets.OrderBy(p => p.Key)) {
                    if ((uint)id >= (uint)nodes.Length) continue;
                    int delta = Math.Clamp(target, 0, settings.MaxHeight) - nodes[id].W;
                    if (delta != 0) RaiseOrLower(nodes[id], delta);
                }
            } finally {
                suppressHydrologyRebuild = false;
                elevationEditTiles = null;
                if (changedTiles.Count > 0) EditsFlushed?.Invoke();
            }
            if (changedTiles.Count == 0) return Array.Empty<int>();
            RebuildHydrology();
            var result = changedTiles.ToArray(); Array.Sort(result);
            return result;
        }
    }
}
