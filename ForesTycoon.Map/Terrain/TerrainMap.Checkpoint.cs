using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Map
{
    internal sealed record RoadCheckpoint(int TileId, RoadEdge Edges, RoadPaving Paving = RoadPaving.Asphalt, float Condition = 1);
    internal sealed record LockedNodeCheckpoint(int NodeId, int Height);
    internal sealed record TerrainCheckpoint(int[] Heights, RoadCheckpoint[] Roads, LockedNodeCheckpoint[] RoadHeights,
        int[] Buildings, ulong SurfaceVersion, float[] Moisture, float[] WaterDepth, int[] StandingWater, int[] RiverNodes,
        SkidTrailCheckpoint[] SkidTrails = null);

    internal sealed partial class TerrainMap
    {
        internal TerrainCheckpoint Capture() => new(nodes.Select(n => n.W).ToArray(),
            roads.Tiles.OrderBy(id => id).Select(id => new RoadCheckpoint(id, roads.GetEdges(id), roads.GetPaving(id), roads.GetCondition(id))).ToArray(),
            roadSurfaceW.OrderBy(p => p.Key).Select(p => new LockedNodeCheckpoint(p.Key, p.Value)).ToArray(),
            buildingTiles.OrderBy(id => id).ToArray(), SurfaceVersion, (float[])hydro.TileMoisture.Clone(),
            (float[])hydro.NodeWaterDepth.Clone(), hydro.StandingWaterTileIds.OrderBy(id => id).ToArray(), hydro.RiverNodeIds.OrderBy(id => id).ToArray(),
            CaptureSkidTrails());

        internal void Restore(TerrainCheckpoint s) => SimulationWork.Complete(RestoreWork(s));

        internal IEnumerable<int> RestoreWork(TerrainCheckpoint s)
        {
            ArgumentNullException.ThrowIfNull(s);
            CheckpointGuard.Length(s.Heights, nodes.Length, "terrain nodes");
            CheckpointGuard.Require(s.Roads != null && s.RoadHeights != null && s.Buildings != null, "terrain layers");
            CheckpointGuard.Length(s.Moisture, tiles.Length, "habitat moisture"); CheckpointGuard.Length(s.WaterDepth, nodes.Length, "terrain water depth");
            foreach (float v in s.Moisture) { yield return 1; CheckpointGuard.Unit(v, "habitat moisture"); }
            foreach (float v in s.WaterDepth) { yield return 1; CheckpointGuard.NonNegative(v, "terrain water depth"); }
            CheckpointGuard.Require(s.StandingWater != null && s.RiverNodes != null &&
                s.StandingWater.All(IsValidTileId) && s.RiverNodes.All(id => (uint)id < (uint)nodes.Length), "static water masks");
            foreach (int w in s.Heights) { yield return 1; CheckpointGuard.Require(w >= 0 && w <= settings.MaxHeight, "terrain height"); }
            var roadIds = new HashSet<int>(); var locks = new Dictionary<int, int>(); var buildings = new HashSet<int>();
            foreach (var r in s.Roads)
            {
                yield return 1; CheckpointGuard.Require(r != null && IsValidTileId(r.TileId) && roadIds.Add(r.TileId) &&
                r.Edges != RoadEdge.None && (r.Edges & ~(RoadEdge)15) == 0 && Enum.IsDefined(r.Paving), "road edges");
            }
            foreach (var r in s.Roads) { yield return 1; CheckpointGuard.Unit(r.Condition, "road condition"); }
            foreach (var n in s.RoadHeights)
            {
                yield return 1; CheckpointGuard.Require(n != null && (uint)n.NodeId < (uint)nodes.Length &&
                n.Height >= 0 && n.Height <= settings.MaxHeight && locks.TryAdd(n.NodeId, n.Height), "frozen road height");
            }
            foreach (int id in s.Buildings) { yield return 1; CheckpointGuard.Require(IsValidTileId(id) && buildings.Add(id) && !roadIds.Contains(id), "building footprint"); }
            foreach (int id in roadIds)
            {
                yield return 1;
                var t = tiles[id];
                CheckpointGuard.Require(locks.ContainsKey(t.W.Id) && locks.ContainsKey(t.S.Id) &&
                    locks.ContainsKey(t.E.Id) && locks.ContainsKey(t.N.Id), "missing road foundation");
            }
            ValidateSkidTrails(s.SkidTrails);
            for (int id = 0; id < nodes.Length; id++) { yield return 1; nodes[id].W = s.Heights[id]; }
            foreach (int id in roads.Tiles.ToArray()) { yield return 1; roads.Remove(id, (RoadEdge)15); }
            foreach (var r in s.Roads) { yield return 1; roads.Add(r.TileId, r.Edges, r.Paving); roads.SetCondition(r.TileId, r.Condition); }
            roadSurfaceW.Clear(); foreach (var n in locks) { yield return 1; roadSurfaceW.Add(n.Key, n.Value); }
            buildingTiles.Clear(); buildingTiles.UnionWith(buildings);
            RestoreSkidTrails(s.SkidTrails);
            foreach (int unit in ApplyNodeChangesWork(new List<Node>(nodes))) yield return unit;
            foreach (int unit in RebuildHydrologyWork()) yield return unit;
            foreach (int unit in RebuildFlippedDiagonalTilesWork()) yield return unit;
            // Preserve the historical moisture/depth publication order of the existing hydrology.
            // Rebuilding it extra times would otherwise change wildlife feeding after a reload.
            s.Moisture.CopyTo(hydro.TileMoisture, 0); s.WaterDepth.CopyTo(hydro.NodeWaterDepth, 0);
            hydro.StandingWaterTileIds.Clear(); hydro.StandingWaterTileIds.UnionWith(s.StandingWater);
            hydro.RiverNodeIds.Clear(); hydro.RiverNodeIds.UnionWith(s.RiverNodes);
            surfaceVisualVersion = s.SurfaceVersion;
            NavigationVersion++;
            if (surfaceVisualVersions != null) Array.Fill(surfaceVisualVersions, ulong.MaxValue);
            foreach (var chunk in chunkIndex.Chunks) { yield return 1; chunk.MarkDirty(ChunkDirtyFlags.All); }
            EditsFlushed?.Invoke();
        }
    }
}
