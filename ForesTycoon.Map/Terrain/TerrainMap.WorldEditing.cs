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
        public int[] ApplyHeightTargets(IReadOnlyDictionary<int, int> targets) { var result = new WorkResult<int[]>(); SimulationWork.Complete(ApplyHeightTargetsWork(targets, result)); return result.Value; }

        internal bool CanApplyHeightTargets(IReadOnlyDictionary<int, int> targets)
        {
            var result = new WorkResult<bool>(); SimulationWork.Complete(CanApplyHeightTargetsWork(targets, result)); return result.Value;
        }

        /// <summary>Authoring preflight includes the full slope cascade beyond the painted corners.</summary>
        internal IEnumerable<int> CanApplyHeightTargetsWork(IReadOnlyDictionary<int, int> targets, WorkResult<bool> result)
        {
            result.Value = false; var scratch = new TerrainElevationPlanner.Scratch();
            foreach (var (node, height) in targets) {
                yield return 1;
                if ((uint)node >= (uint)nodes.Length) yield break;
                int delta = Math.Clamp(height, 0, settings.MaxHeight) - nodes[node].W;
                if (delta == 0) continue;
                var planned = new WorkResult<Dictionary<int, int>>();
                foreach (int unit in TerrainElevationPlanner.CreateWork(data, node, delta,
                    settings.MaxHeight, planned, scratch)) yield return unit;
                if (planned.Value == null) yield break;
                var valid = new WorkResult<bool>();
                foreach (int unit in ValidateRoadsAgainstPendingTerrainWork(planned.Value, valid)) yield return unit;
                if (!valid.Value) yield break;
            }
            result.Value = true;
        }

        internal IEnumerable<int> ApplyHeightTargetsWork(IReadOnlyDictionary<int, int> targets, WorkResult<int[]> result, bool initialDefinition = false)
        {
            result.Value = Array.Empty<int>();
            if (targets == null || targets.Count == 0) yield break;
            var changedTiles = new HashSet<int>();
            bool previousDefinition = initialTerrainDefinition; initialTerrainDefinition = initialDefinition;
            elevationEditTiles = changedTiles;
            suppressHydrologyRebuild = true;
            try
            {
                for (int id = 0; id < nodes.Length; id++)
                {
                    yield return 1;
                    if (!targets.TryGetValue(id, out int target)) continue;
                    int delta = Math.Clamp(target, 0, settings.MaxHeight) - nodes[id].W;
                    if (delta != 0) foreach (int unit in RaiseOrLowerWork(nodes[id], delta)) yield return unit;
                }
            }
            finally
            {
                suppressHydrologyRebuild = false;
                initialTerrainDefinition = previousDefinition;
                elevationEditTiles = null;
                if (changedTiles.Count > 0) EditsFlushed?.Invoke();
            }
            if (changedTiles.Count == 0) yield break;
            foreach (int unit in RebuildHydrologyWork()) yield return unit;
            var ordered = new List<int>(changedTiles.Count);
            for (int id = 0; id < tiles.Length; id++) { if (changedTiles.Contains(id)) ordered.Add(id); yield return 1; }
            result.Value = ordered.ToArray();
        }
    }
}
