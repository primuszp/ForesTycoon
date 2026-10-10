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

        /// <summary>A trail unused this long, with its ruts faded, grows over and disappears.</summary>
        internal const float DefaultSkidTrailOvergrowYears = 3f;
        /// <summary>Idle forest years after which an unused trail without ruts is overgrown (set from the world's tuning).</summary>
        internal float SkidTrailOvergrowYears = DefaultSkidTrailOvergrowYears;
        private readonly Dictionary<int, SkidTrail> skidTrails = new();

        public int SkidTrailCount => skidTrails.Count;
        public bool IsSkidTrail(int tileId) => skidTrails.ContainsKey(tileId);
        public IEnumerable<int> SkidTrailTiles => skidTrails.Keys;
        public RoadEdge GetSkidTrailEdges(int tileId) => skidTrails.TryGetValue(tileId, out var t) ? t.Edges : RoadEdge.None;
        public float GetSkidTrailWear(int tileId) => skidTrails.TryGetValue(tileId, out var t) ? t.Wear : 0;
        public float GetSkidTrailIdle(int tileId) => skidTrails.TryGetValue(tileId, out var t) ? t.Idle : 0;

        /// <summary>Forest floor a machine can drive on: no road, building or open water.</summary>
        public bool CanCarrySkidTrail(int tileId)
        {
            if (!IsValidTileId(tileId) || roads.Has(tileId) || IsBuildingTile(tileId)) return false;
            Tile tile = tiles[tileId];
            return !ShouldDrawStandingWater(tile) && CountRiverCorners(tile) == 0;
        }

        /// <summary>Marks the a → b trail (the same L-shaped plan as roads); road tiles on the way are crossed. Returns the tiles marked.</summary>
        public int[] MarkSkidTrailPath(int startTileId, int endTileId)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return Array.Empty<int>();
            var changed = new List<int>();
            foreach (RoadPlanStep step in BuildRoadPlan(tiles[startTileId], tiles[endTileId]))
            {
                if (!CanCarrySkidTrail(step.TileId)) continue;
                skidTrails.TryGetValue(step.TileId, out var trail);
                bool isNew = trail.Edges == RoadEdge.None;
                RoadEdge merged = GetNetworkEdges(step.TileId) | step.Edges;
                if (!isNew && merged == trail.Edges) continue;
                trail.Edges = merged;
                trail.Idle = 0;
                skidTrails[step.TileId] = trail;
                changed.Add(step.TileId);
            }
            return changed.ToArray();
        }

        public int[] RemoveSkidTrailPath(int startTileId, int endTileId)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return Array.Empty<int>();
            var removed = new List<int>();
            foreach (RoadPlanStep step in BuildRoadPlan(tiles[startTileId], tiles[endTileId]))
                if (skidTrails.Remove(step.TileId)) removed.Add(step.TileId);
            return removed.ToArray();
        }

        /// <summary>One machine pass: the ruts deepen (less so once they are already deep) and the trail counts as used.</summary>
        public void DriveSkidTrail(int tileId, float amount)
        {
            if (!skidTrails.TryGetValue(tileId, out var trail)) return;
            trail.Wear = Math.Clamp(trail.Wear + amount * (1 - trail.Wear), 0, 1);
            trail.Idle = 0;
            skidTrails[tileId] = trail;
        }

        /// <summary>
        /// Over <paramref name="years"/> the ruts settle and grass covers them; a trail left unused for
        /// <see cref="SkidTrailOvergrowYears"/> with no visible ruts is gone. Returns the tiles that disappeared.
        /// </summary>
        public int[] AgeSkidTrails(float years)
        {
            if (years <= 0 || skidTrails.Count == 0) return Array.Empty<int>();
            List<int> gone = null;
            foreach (int id in skidTrails.Keys.ToArray())
            {
                var trail = skidTrails[id];
                trail.Wear = Math.Max(0, trail.Wear - 0.4f * years);
                trail.Idle += years;
                if (trail.Idle >= SkidTrailOvergrowYears && trail.Wear <= 0.001f) { skidTrails.Remove(id); (gone ??= new()).Add(id); }
                else skidTrails[id] = trail;
            }
            return gone?.ToArray() ?? Array.Empty<int>();
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
