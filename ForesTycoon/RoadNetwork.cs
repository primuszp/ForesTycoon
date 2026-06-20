using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>
    /// Úthálózat: út-csempék halmaza (Transport Tycoon-modell). Egy csempe vagy
    /// út, vagy nem; a kapcsolatok a szomszédos út-csempékből adódnak. A terep-
    /// modelltől független; a megjelenítéshez a Terrain adja a csempe-geometriát.
    /// </summary>
    sealed class RoadNetwork
    {
        private readonly HashSet<int> tiles = new HashSet<int>();

        public bool Add(int tileId) => tiles.Add(tileId);
        public bool Remove(int tileId) => tiles.Remove(tileId);
        public bool Has(int tileId) => tiles.Contains(tileId);
        public int Count => tiles.Count;
        public IEnumerable<int> Tiles => tiles;
    }
}
