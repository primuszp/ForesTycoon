using System;
using System.Collections.Generic;

namespace ForesTycoon.Map
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

    /// <summary>
    /// Running surface of a single-lane road. Asphalt costs more to build but lasts; water-bound macadam is cheap and
    /// wears quickly, above all when wet.
    /// </summary>
    enum RoadPaving : byte { Asphalt, Macadam }

    sealed class RoadNetwork
    {
        private readonly Dictionary<int, RoadEdge> tiles = new Dictionary<int, RoadEdge>();
        private readonly Dictionary<int, RoadPaving> paving = new Dictionary<int, RoadPaving>();
        // Surface condition per road tile: 1 = new, 0 = ruined. Missing means new.
        private readonly Dictionary<int, float> condition = new Dictionary<int, float>();

        public bool Add(int tileId, RoadEdge edges) => Add(tileId, edges, RoadPaving.Asphalt);

        public bool Add(int tileId, RoadEdge edges, RoadPaving surface)
        {
            if (edges == RoadEdge.None) return false;
            bool isNew = !tiles.TryGetValue(tileId, out RoadEdge existing);
            RoadEdge merged = existing | edges;
            tiles[tileId] = merged;
            if (isNew) { paving[tileId] = surface; condition.Remove(tileId); }
            return merged != existing;
        }

        /// <summary>Lays a new surface on an existing road tile; it comes out as new.</summary>
        public bool Resurface(int tileId, RoadPaving surface)
        {
            if (!tiles.ContainsKey(tileId) || GetPaving(tileId) == surface) return false;
            paving[tileId] = surface; condition.Remove(tileId);
            return true;
        }

        public bool Remove(int tileId, RoadEdge edges)
        {
            if (!tiles.TryGetValue(tileId, out RoadEdge existing)) return false;
            RoadEdge updated = existing & ~edges;
            if (updated == RoadEdge.None) { tiles.Remove(tileId); paving.Remove(tileId); condition.Remove(tileId); }
            else tiles[tileId] = updated;
            return updated != existing;
        }

        public RoadPaving GetPaving(int tileId) => paving.TryGetValue(tileId, out var p) ? p : RoadPaving.Asphalt;
        public float GetCondition(int tileId) => condition.TryGetValue(tileId, out float c) ? c : 1f;

        public void SetCondition(int tileId, float value)
        {
            if (!tiles.ContainsKey(tileId)) return;
            value = Math.Clamp(value, 0f, 1f);
            if (value >= 1f) condition.Remove(tileId); else condition[tileId] = value;
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
