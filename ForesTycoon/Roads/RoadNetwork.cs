using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    [Flags]
    enum RoadEdge
    {
        None = 0,
        WS = 1 << 0,
        SE = 1 << 1,
        EN = 1 << 2,
        NW = 1 << 3
    }

    sealed class RoadNetwork
    {
        private readonly Dictionary<int, RoadEdge> tiles = new Dictionary<int, RoadEdge>();

        public bool Add(int tileId, RoadEdge edges)
        {
            if (edges == RoadEdge.None) return false;
            tiles.TryGetValue(tileId, out RoadEdge existing);
            RoadEdge merged = existing | edges;
            tiles[tileId] = merged;
            return merged != existing;
        }

        public bool Remove(int tileId, RoadEdge edges)
        {
            if (!tiles.TryGetValue(tileId, out RoadEdge existing)) return false;
            RoadEdge updated = existing & ~edges;
            if (updated == RoadEdge.None) tiles.Remove(tileId);
            else tiles[tileId] = updated;
            return updated != existing;
        }

        public bool Has(int tileId) => tiles.ContainsKey(tileId);

        public bool HasEdge(int tileId, RoadEdge edge) =>
            tiles.TryGetValue(tileId, out RoadEdge edges) && (edges & edge) != 0;

        public RoadEdge GetEdges(int tileId) =>
            tiles.TryGetValue(tileId, out RoadEdge edges) ? edges : RoadEdge.None;

        public int Count => tiles.Count;
        public IEnumerable<int> Tiles => tiles.Keys;
    }
}
