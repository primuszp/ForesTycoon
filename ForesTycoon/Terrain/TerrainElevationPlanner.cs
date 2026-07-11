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

            bool valid = true;
            int HeightOf(Node node) => pending.TryGetValue(node.Id, out int value) ? value : node.W;

            void Set(Node node, int height)
            {
                if (!valid) return;
                if (height < 0 || height > maxHeight) { valid = false; return; }
                if (HeightOf(node) == height) return;
                pending[node.Id] = height;
                Relax(node.U, node.V - 1, height);
                Relax(node.U + 1, node.V, height);
                Relax(node.U, node.V + 1, height);
                Relax(node.U - 1, node.V, height);
            }

            void Relax(int u, int v, int height)
            {
                if (!data.CheckNode(u, v)) return;
                Node neighbour = data.GetNode(u, v);
                int difference = height - HeightOf(neighbour);
                if (Math.Abs(difference) > 1)
                    Set(neighbour, height - Math.Sign(difference));
            }

            Node start = data.Nodes[startNodeId];
            Set(start, start.W + delta);
            if (valid && pending.Count > 0) return true;

            pending.Clear();
            return false;
        }
    }
}
