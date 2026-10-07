using System;
using System.Collections.Generic;

namespace ForesTycoon.Map
{
    static class RoadPathfinder
    {
        public static int[] FindPath(RoadNetwork roads, int tilesPerColumn, int startTileId, int endTileId)
        {
            if (roads == null) throw new ArgumentNullException(nameof(roads));
            if (tilesPerColumn <= 0) throw new ArgumentOutOfRangeException(nameof(tilesPerColumn));
            if (!roads.Has(startTileId) || !roads.Has(endTileId)) return Array.Empty<int>();
            if (startTileId == endTileId) return new[] { startTileId };

            Queue<int> open = new Queue<int>();
            Dictionary<int, int> previous = new Dictionary<int, int> { [startTileId] = -1 };
            open.Enqueue(startTileId);

            while (open.Count > 0)
            {
                int current = open.Dequeue();
                foreach (int neighbour in ConnectedNeighbours(roads, tilesPerColumn, current))
                {
                    if (previous.ContainsKey(neighbour)) continue;
                    previous[neighbour] = current;
                    if (neighbour == endTileId) return Reconstruct(previous, endTileId);
                    open.Enqueue(neighbour);
                }
            }
            return Array.Empty<int>();
        }

        public static int[] FindDemoRoute(RoadNetwork roads, int tilesPerColumn)
        {
            int start = -1;
            foreach (int tileId in roads.Tiles) { start = tileId; break; }
            if (start < 0) return Array.Empty<int>();

            int farthest = FindFarthest(roads, tilesPerColumn, start);
            int opposite = FindFarthest(roads, tilesPerColumn, farthest);
            return FindPath(roads, tilesPerColumn, farthest, opposite);
        }

        private static int FindFarthest(RoadNetwork roads, int tilesPerColumn, int start)
        {
            Queue<int> open = new Queue<int>();
            HashSet<int> visited = new HashSet<int> { start };
            int farthest = start;
            open.Enqueue(start);
            while (open.Count > 0)
            {
                farthest = open.Dequeue();
                foreach (int neighbour in ConnectedNeighbours(roads, tilesPerColumn, farthest))
                    if (visited.Add(neighbour)) open.Enqueue(neighbour);
            }
            return farthest;
        }

        private static IEnumerable<int> ConnectedNeighbours(RoadNetwork roads, int size, int tileId)
        {
            int u = tileId / size;
            int v = tileId % size;
            RoadEdge edges = roads.GetEdges(tileId);
            int neighbour;
            if ((edges & RoadEdge.WS) != 0 && v > 0)
            {
                neighbour = tileId - 1;
                if (roads.HasEdge(neighbour, RoadEdge.EN)) yield return neighbour;
            }
            if ((edges & RoadEdge.SE) != 0 && u < size - 1)
            {
                neighbour = tileId + size;
                if (roads.HasEdge(neighbour, RoadEdge.NW)) yield return neighbour;
            }
            if ((edges & RoadEdge.EN) != 0 && v < size - 1)
            {
                neighbour = tileId + 1;
                if (roads.HasEdge(neighbour, RoadEdge.WS)) yield return neighbour;
            }
            if ((edges & RoadEdge.NW) != 0 && u > 0)
            {
                neighbour = tileId - size;
                if (roads.HasEdge(neighbour, RoadEdge.SE)) yield return neighbour;
            }
        }

        private static int[] Reconstruct(Dictionary<int, int> previous, int end)
        {
            List<int> path = new List<int>();
            for (int current = end; current >= 0; current = previous[current]) path.Add(current);
            path.Reverse();
            return path.ToArray();
        }
    }
}
