using System;
using System.Collections.Generic;

namespace ForesTycoon.Map
{
    static class TerrainElevationPlanner
    {
        internal sealed class Scratch
        {
            internal readonly Dictionary<int, int> Pending = new();
            internal readonly Queue<(Node node, int height)> Queue = new();
        }

        public static bool TryCreate(TerrainData data, int startNodeId, int delta, int maxHeight, out Dictionary<int, int> changes)
        {
            var result = new WorkResult<Dictionary<int, int>>();
            SimulationWork.Complete(CreateWork(data, startNodeId, delta, maxHeight, result));
            changes = result.Value ?? new(); return result.Value != null;
        }

        internal static IEnumerable<int> CreateWork(TerrainData data, int startNodeId, int delta, int maxHeight,
            WorkResult<Dictionary<int, int>> result, Scratch scratch = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            scratch ??= new Scratch();
            var pending = scratch.Pending; pending.Clear();
            var plannedHeights = scratch.Queue; plannedHeights.Clear();
            result.Value = pending;
            if (startNodeId < 0 || startNodeId >= data.Nodes.Length || delta == 0) { result.Value = null; yield break; }

            plannedHeights.Enqueue((data.Nodes[startNodeId], data.Nodes[startNodeId].W + delta));

            while (plannedHeights.Count > 0)
            {
                yield return 1;
                (Node node, int height) = plannedHeights.Dequeue();
                if (height < 0 || height > maxHeight)
                {
                    pending.Clear();
                    result.Value = null; yield break;
                }

                int currentHeight = pending.TryGetValue(node.Id, out int value) ? value : node.W;
                if (currentHeight == height) continue;

                pending[node.Id] = height;
                EnqueueRelaxedNeighbour(node.U, node.V - 1, height);
                EnqueueRelaxedNeighbour(node.U + 1, node.V, height);
                EnqueueRelaxedNeighbour(node.U, node.V + 1, height);
                EnqueueRelaxedNeighbour(node.U - 1, node.V, height);
            }

            if (pending.Count > 0) yield break;

            pending.Clear();
            result.Value = null; yield break;

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
