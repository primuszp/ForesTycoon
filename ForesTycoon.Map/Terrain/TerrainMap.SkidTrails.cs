using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Map
{
    internal sealed record SkidTrailCheckpoint(int TileId, RoadEdge Edges, float Wear, float Idle);

    /// <summary>
    /// Skid trails (közelítő nyomok): temporary tracks the player marks from a road into the forest so the harvester
    /// and the forwarder can reach the felling. They are no built road, only wheel ruts in the soil: the ruts deepen
    /// with every pass of a machine and fade while the trail rests, and an unused trail grows over and disappears.
    /// </summary>
    internal sealed partial class TerrainMap
    {
        private struct SkidTrail
        {
            internal RoadEdge Edges;
            internal float Wear;   // rut depth: 0 freshly marked … 1 deep, muddy ruts
            internal float Idle;   // years since a machine last drove on it
        }

        private readonly Dictionary<int, SkidTrail> skidTrails = new();

        public int SkidTrailCount => skidTrails.Count;
        public bool IsSkidTrail(int tileId) => skidTrails.ContainsKey(tileId);
        public IEnumerable<int> SkidTrailTiles => skidTrails.Keys;
        public RoadEdge GetSkidTrailEdges(int tileId) => skidTrails.TryGetValue(tileId, out var t) ? t.Edges : RoadEdge.None;
        public float GetSkidTrailWear(int tileId) => skidTrails.TryGetValue(tileId, out var t) ? t.Wear : 0;
        public float GetSkidTrailIdle(int tileId) => skidTrails.TryGetValue(tileId, out var t) ? t.Idle : 0;

        /// <summary>Rule-engine access: sets a trail's ruts (0–1) and idle years; returns false for a tile without a trail.</summary>
        public bool SetSkidTrailState(int tileId, float wear, float idle)
        {
            if (!skidTrails.TryGetValue(tileId, out var trail)) return false;
            trail.Wear = Math.Clamp(wear, 0, 1); trail.Idle = Math.Max(0, idle);
            skidTrails[tileId] = trail;
            return true;
        }

        /// <summary>Rule-engine access: the trail is gone (overgrown), and routes can be refreshed.</summary>
        public bool RemoveSkidTrail(int tileId) { if (!skidTrails.Remove(tileId)) return false; NavigationVersion++; return true; }

        /// <summary>Forest floor a machine can drive on: no road, building or open water.</summary>
        public bool CanCarrySkidTrail(int tileId)
        {
            if (!IsValidTileId(tileId) || roads.Has(tileId) || IsBuildingTile(tileId) || HasGroundReservation(tileId) || HasMobileObject(tileId)) return false;
            Tile tile = tiles[tileId];
            return !ShouldDrawStandingWater(tile) && CountRiverCorners(tile) == 0;
        }

        /// <summary>Marks the a → b trail (the same L-shaped plan as roads); road tiles on the way are crossed. Returns the tiles marked.</summary>
        public int[] MarkSkidTrailPath(int startTileId, int endTileId) { var result = new WorkResult<int[]>(); SimulationWork.Complete(MarkSkidTrailPathWork(startTileId, endTileId, result)); return result.Value; }

        internal IEnumerable<int> MarkSkidTrailPathWork(int startTileId, int endTileId, WorkResult<int[]> result)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) { result.Value = Array.Empty<int>(); yield break; }
            var changed = new List<int>();
            foreach (RoadPlanStep step in RoadPlanSteps(tiles[startTileId], tiles[endTileId]))
            {
                yield return 1;
                if (!CanCarrySkidTrail(step.TileId)) continue;
                skidTrails.TryGetValue(step.TileId, out var trail);
                bool isNew = trail.Edges == RoadEdge.None;
                RoadEdge merged = GetNetworkEdges(step.TileId) | step.Edges;
                if (!isNew && merged == trail.Edges) continue;
                trail.Edges = merged;
                trail.Idle = 0;
                skidTrails[step.TileId] = trail;
                NavigationVersion++;
                changed.Add(step.TileId);
            }
            result.Value = changed.ToArray();
        }

        /// <summary>A player stroke must be continuous and reach a road, directly or through existing trails.</summary>
        internal IEnumerable<int> MarkConnectedSkidTrailPathWork(int start, int end, WorkResult<int[]> result)
        {
            result.Value = Array.Empty<int>();
            if (!IsValidTileId(start) || !IsValidTileId(end)) yield break;
            var frontier = new Queue<int>(); var seen = new HashSet<int>(); var planned = new Dictionary<int, RoadEdge>();
            foreach (var step in RoadPlanSteps(tiles[start], tiles[end])) {
                yield return 1;
                if (!IsRoadTile(step.TileId) && !CanCarrySkidTrail(step.TileId)) yield break;
                planned[step.TileId] = step.Edges | GetNetworkEdges(step.TileId);
                if (seen.Add(step.TileId)) frontier.Enqueue(step.TileId);
            }
            bool connected = false; var neighbours = new int[4];
            while (frontier.Count > 0 && !connected) {
                int tile = frontier.Dequeue(); yield return 1;
                if (IsRoadTile(tile)) { connected = true; break; }
                int count = GetTileNeighbours(tile, neighbours);
                for (int i = 0; i < count; i++) {
                    int next = neighbours[i];
                    if (!IsNetworkTile(next) && !planned.ContainsKey(next)) continue;
                    RoadEdge aEdge = EdgeToNeighbor(tile / (nodeRows - 1), tile % (nodeRows - 1), next / (nodeRows - 1), next % (nodeRows - 1));
                    RoadEdge Edges(int id) => IsRoadTile(id) ? roads.GetEdges(id) : planned.TryGetValue(id, out var edge) ? edge : GetSkidTrailEdges(id);
                    if (!Connects(IsRoadTile(tile), !IsRoadTile(tile), Edges(tile), aEdge,
                        IsRoadTile(next), !IsRoadTile(next), Edges(next), Opposite(aEdge))) continue;
                    if (IsRoadTile(next)) { connected = true; break; }
                    if (seen.Add(next)) frontier.Enqueue(next);
                    yield return 1;
                }
            }
            if (!connected) yield break;
            foreach (int unit in MarkSkidTrailPathWork(start, end, result)) yield return unit;
        }

        public int[] RemoveSkidTrailPath(int startTileId, int endTileId) { var result = new WorkResult<int[]>(); SimulationWork.Complete(RemoveSkidTrailPathWork(startTileId, endTileId, result)); return result.Value; }

        internal IEnumerable<int> RemoveSkidTrailPathWork(int startTileId, int endTileId, WorkResult<int[]> result)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) { result.Value = Array.Empty<int>(); yield break; }
            var removed = new List<int>();
            foreach (RoadPlanStep step in RoadPlanSteps(tiles[startTileId], tiles[endTileId]))
            { yield return 1; if (RemoveSkidTrail(step.TileId)) removed.Add(step.TileId); }
            result.Value = removed.ToArray();
        }

        /// <summary>The 4-neighbours of a tile (the directions a skid trail or road connects in).</summary>
        public int GetTileNeighbours(int tileId, Span<int> result)
        {
            int tpc = nodeRows - 1, u = tileId / tpc, v = tileId % tpc, count = 0;
            if (checkTile(u + 1, v)) result[count++] = (u + 1) * tpc + v;
            if (checkTile(u - 1, v)) result[count++] = (u - 1) * tpc + v;
            if (checkTile(u, v + 1)) result[count++] = u * tpc + v + 1;
            if (checkTile(u, v - 1)) result[count++] = u * tpc + v - 1;
            return count;
        }

        internal SkidTrailCheckpoint[] CaptureSkidTrails() =>
            skidTrails.OrderBy(p => p.Key).Select(p => new SkidTrailCheckpoint(p.Key, p.Value.Edges, p.Value.Wear, p.Value.Idle)).ToArray();

        /// <summary>Checks a checkpoint's trails before anything is restored, so a bad save changes nothing.</summary>
        internal void ValidateSkidTrails(SkidTrailCheckpoint[] trails)
        {
            if (trails == null) return;
            var seen = new HashSet<int>();
            foreach (var t in trails)
            {
                CheckpointGuard.Require(t != null && IsValidTileId(t.TileId) && (t.Edges & ~(RoadEdge)15) == 0 && seen.Add(t.TileId), "skid trail");
                CheckpointGuard.Unit(t.Wear, "skid trail wear"); CheckpointGuard.NonNegative(t.Idle, "skid trail idle time");
            }
        }

        internal void RestoreSkidTrails(SkidTrailCheckpoint[] trails)
        {
            skidTrails.Clear();
            if (trails == null) return;
            foreach (var t in trails) skidTrails[t.TileId] = new SkidTrail { Edges = t.Edges, Wear = t.Wear, Idle = t.Idle };
        }
    }
}
