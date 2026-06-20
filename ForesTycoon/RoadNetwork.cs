using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>
    /// Úthálózat-gráf: élek szomszédos terep-node-ok között (node-id párok).
    /// A terep-modelltől független; a megjelenítéshez a Terrain a node-pozíciókat
    /// adja hozzá. Egy szegmens irányítatlan, a két végpont id-jéből képzett kulcs.
    /// </summary>
    sealed class RoadNetwork
    {
        private readonly HashSet<long> segments = new HashSet<long>();

        private static long Key(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        public bool Add(int a, int b) => a != b && segments.Add(Key(a, b));
        public bool Remove(int a, int b) => segments.Remove(Key(a, b));
        public bool Has(int a, int b) => segments.Contains(Key(a, b));
        public int Count => segments.Count;

        public IEnumerable<(int a, int b)> Segments
        {
            get
            {
                foreach (long k in segments)
                    yield return ((int)(k >> 32), (int)(k & 0xffffffff));
            }
        }
    }
}
