using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    static class TerrainElevationPlanner
    {
        public static bool TryCreate(TerrainData data, int startNodeId, int delta, int maxHeight,
            out Dictionary<int, int> changes)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Dictionary<int, int> pending = new Dictionary<int, int>();
            changes = pending;
            if (startNodeId < 0 || startNodeId >= data.Nodes.Length || delta == 0) return false;

            Queue<(Node node, int height)> plannedHeights = new Queue<(Node node, int height)>();
            plannedHeights.Enqueue((data.Nodes[startNodeId], data.Nodes[startNodeId].W + delta));

            while (plannedHeights.Count > 0)
            {
                (Node node, int height) = plannedHeights.Dequeue();
                if (height < 0 || height > maxHeight)
                {
                    pending.Clear();
                    return false;
                }

                int currentHeight = pending.TryGetValue(node.Id, out int value) ? value : node.W;
                if (currentHeight == height) continue;

                pending[node.Id] = height;
                EnqueueRelaxedNeighbour(node.U, node.V - 1, height);
                EnqueueRelaxedNeighbour(node.U + 1, node.V, height);
                EnqueueRelaxedNeighbour(node.U, node.V + 1, height);
                EnqueueRelaxedNeighbour(node.U - 1, node.V, height);
            }

            if (pending.Count > 0) return true;

            pending.Clear();
            return false;

            void EnqueueRelaxedNeighbour(int u, int v, int height)
            {
                if (!data.CheckNode(u, v)) return;

                Node neighbour = data.GetNode(u, v);
                int neighbourHeight = pending.TryGetValue(neighbour.Id, out int value) ? value : neighbour.W;
                int difference = height - neighbourHeight;
                if (Math.Abs(difference) > 1)
                    plannedHeights.Enqueue((neighbour, height - Math.Sign(difference)));
            }
        }
    }
}
