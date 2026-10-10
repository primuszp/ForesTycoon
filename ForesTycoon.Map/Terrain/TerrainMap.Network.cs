using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    /// <summary>
    /// The transport network every vehicle drives on: built roads (asphalt, macadam) and skid trails, the cheapest
    /// kind, which is only ruts on the natural ground. Paths prefer good roads; a trail costs several road tiles of
    /// travel, so a truck uses one only where nothing better leads.
    /// </summary>
    internal sealed partial class TerrainMap
    {
        public bool IsNetworkTile(int tileId) => IsRoadTile(tileId) || IsSkidTrail(tileId);

        /// <summary>Relative travel cost of entering a tile: asphalt 1, macadam a little more, a skid trail much more.</summary>
        public float NetworkCost(int tileId) =>
            IsRoadTile(tileId) ? (roads.GetPaving(tileId) == RoadPaving.Asphalt ? 1f : 1.15f) : 3.5f + 2f * GetSkidTrailWear(tileId);

        /// <summary>Visible arms, including the reciprocal road exits at trail junctions.</summary>
        public RoadEdge GetNetworkEdges(int tileId)
        {
            if (!IsNetworkTile(tileId)) return RoadEdge.None;
            RoadEdge edges = roads.Has(tileId) ? roads.GetEdges(tileId) : GetSkidTrailEdges(tileId);
            Span<int> neighbours = stackalloc int[4];
            int count = GetNetworkNeighbours(tileId, neighbours), size = nodeRows - 1;
            for (int i = 0; i < count; i++)
                edges |= EdgeToNeighbor(tileId / size, tileId % size, neighbours[i] / size, neighbours[i] % size);
            return edges;
        }

        /// <summary>The network tiles a vehicle can drive to from <paramref name="tileId"/>.</summary>
        public int GetNetworkNeighbours(int tileId, Span<int> result)
        {
            if (!IsValidTileId(tileId) || !IsNetworkTile(tileId)) return 0;
            int tpc = nodeRows - 1, u = tileId / tpc, v = tileId % tpc, count = 0;
            for (int k = 0; k < 4; k++)
            {
                var (du, dv, mine, theirs) = k switch
                {
                    0 => (0, -1, RoadEdge.WS, RoadEdge.EN),
                    1 => (1, 0, RoadEdge.SE, RoadEdge.NW),
                    2 => (0, 1, RoadEdge.EN, RoadEdge.WS),
                    _ => (-1, 0, RoadEdge.NW, RoadEdge.SE)
                };
                if (!checkTile(u + du, v + dv)) continue;
                int other = (u + du) * tpc + v + dv;
                if (Connects(tileId, mine, other, theirs)) result[count++] = other;
            }
            return count;
        }

        // Explicit arms join normally; a dead end also joins the network directly ahead of its open end.
        // This lets separately drawn surface types meet without requiring a repainted overlap tile.
        private bool Connects(int a, RoadEdge aEdge, int b, RoadEdge bEdge)
        {
            bool aRoad = roads.Has(a), bRoad = roads.Has(b), aTrail = IsSkidTrail(a), bTrail = IsSkidTrail(b);
            if (!(aRoad || aTrail) || !(bRoad || bTrail)) return false;
            RoadEdge aEdges = aRoad ? roads.GetEdges(a) : GetSkidTrailEdges(a);
            RoadEdge bEdges = bRoad ? roads.GetEdges(b) : GetSkidTrailEdges(b);
            if (aEdges == Opposite(aEdge) || bEdges == Opposite(bEdge)) return true;
            if (aRoad && bRoad) return roads.HasEdge(a, aEdge) && roads.HasEdge(b, bEdge);
            bool aHas = aRoad ? roads.HasEdge(a, aEdge) : aTrail && (GetSkidTrailEdges(a) & aEdge) != 0;
            bool bHas = bRoad ? roads.HasEdge(b, bEdge) : bTrail && (GetSkidTrailEdges(b) & bEdge) != 0;
            // A trail's loose end beside a road joins it too: the machines drive off the road onto it.
            bool aLoose = aTrail && CountEdges(GetSkidTrailEdges(a)) <= 1 && bRoad;
            bool bLoose = bTrail && CountEdges(GetSkidTrailEdges(b)) <= 1 && aRoad;
            return aRoad ? bHas || bLoose : bRoad ? aHas || aLoose : aHas && bHas;
        }

        public bool AreNetworkNeighbours(int a, int b)
        {
            Span<int> next = stackalloc int[4];
            int count = IsNetworkTile(a) ? GetNetworkNeighbours(a, next) : 0;
            for (int i = 0; i < count; i++) if (next[i] == b) return true;
            return false;
        }

        /// <summary>Cheapest path over the network (Dijkstra); empty when not connected.</summary>
        public int[] FindNetworkPath(int start, int end) => FindNetworkPath(start, end, out _);

        public int[] FindNetworkPath(int start, int end, out float totalCost)
        {
            totalCost = float.PositiveInfinity;
            if (!IsNetworkTile(start) || !IsNetworkTile(end)) return Array.Empty<int>();
            if (start == end) { totalCost = 0; return new[] { start }; }
            var cost = new Dictionary<int, float> { [start] = 0 };
            var previous = new Dictionary<int, int> { [start] = -1 };
            // Tile id breaks equal-cost ties independently of heap implementation details.
            var open = new PriorityQueue<int, (float Cost, int Tile)>();
            open.Enqueue(start, (0, start));
            Span<int> next = stackalloc int[4];
            while (open.TryDequeue(out int tile, out var priority))
            {
                float c = priority.Cost;
                if (c > cost[tile]) continue;
                if (tile == end)
                {
                    totalCost = c;
                    var path = new List<int>();
                    for (int t = end; t >= 0; t = previous[t]) path.Add(t);
                    path.Reverse();
                    return path.ToArray();
                }
                int count = GetNetworkNeighbours(tile, next);
                for (int i = 0; i < count; i++)
                {
                    float nc = c + NetworkCost(next[i]);
                    if (cost.TryGetValue(next[i], out float known) && known <= nc) continue;
                    cost[next[i]] = nc; previous[next[i]] = tile; open.Enqueue(next[i], (nc, next[i]));
                }
            }
            return Array.Empty<int>();
        }

        /// <summary>Centre and slope of the surface a vehicle drives on: the frozen road surface, or the natural ground of a trail.</summary>
        public bool TryGetNetworkSurface(int tileId, out Vector3 center, out Vector2 gradient)
        {
            if (TryGetRoadSurface(tileId, out center, out gradient)) return true;
            if (!IsSkidTrail(tileId)) return false;
            GetTrailSurfaceCorners(tileId, out Vector3 w, out Vector3 s, out Vector3 e, out Vector3 n);
            center = (w + s + e + n) * 0.25f;
            Vector3 normal = Vector3.Cross(s - w, n - w) + Vector3.Cross(n - e, s - e);
            gradient = new Vector2(-normal.X / normal.Z, -normal.Y / normal.Z);
            return true;
        }

        /// <summary>Trail ramps meet the locked road edge and taper back to natural ground.</summary>
        public void GetTrailSurfaceCorners(int tileId, out Vector3 w, out Vector3 s, out Vector3 e, out Vector3 n)
        {
            Tile tile = tiles[tileId];
            // Shared locked nodes also keep neighbouring trail ramps continuous at their seam.
            w = RoadCorner(tile.W); s = RoadCorner(tile.S);
            e = RoadCorner(tile.E); n = RoadCorner(tile.N);
        }

        /// <summary>Height on the driving surface, with ordinary ground as the fallback inside work yards.</summary>
        public bool TryGetDrivingSurfaceZ(double x, double y, out float z)
        {
            if (!TryGetTileCoordinates(x, y, out int u, out int v, out double localX, out double localY))
            { z = 0; return false; }
            Tile tile = GetTile(u, v);
            if (!IsNetworkTile(tile.Id)) return TryGetSurfaceZ(x, y, out z);
            GetTrailSurfaceCorners(tile.Id, out Vector3 w, out Vector3 s, out Vector3 e, out Vector3 n);
            float fx = (float)localX, fy = (float)localY;
            z = (w.Z + (s.Z - w.Z) * fx) * (1 - fy) + (n.Z + (e.Z - n.Z) * fx) * fy;
            return true;
        }

        /// <summary>Network tiles beside the given tiles (where a vehicle stops to work at them), sorted.</summary>
        public List<int> FindNetworkDocks(IEnumerable<int> ids)
        {
            var result = new List<int>();
            Span<int> next = stackalloc int[4];
            foreach (int id in ids)
            {
                if (IsNetworkTile(id) && !result.Contains(id)) result.Add(id);
                int count = GetTileNeighbours(id, next);
                for (int i = 0; i < count; i++) if (IsNetworkTile(next[i]) && !result.Contains(next[i])) result.Add(next[i]);
            }
            result.Sort();
            return result;
        }
    }
}
