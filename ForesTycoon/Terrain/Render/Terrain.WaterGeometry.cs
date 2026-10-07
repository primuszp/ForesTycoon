using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private float NodeWaterSurfaceNoWave(Node node)
        {
            return node.zPos + nodeWaterDepth[node.Id];
        }

        private bool IsBelowWater(Node node)
        {
            return nodeWaterDepth[node.Id] >= MinimumWaterDepth;
        }

        private Vector3 IntersectWaterEdge(Node a, Node b)
        {
            float depthA = nodeWaterDepth[a.Id];
            float depthB = nodeWaterDepth[b.Id];
            float delta = depthB - depthA;
            float t = Math.Abs(delta) < 0.0001f
                ? 0.5f
                : (MinimumWaterDepth - depthA) / delta;
            t = Math.Max(0.0f, Math.Min(1.0f, t));

            float surfaceA = NodeWaterSurfaceNoWave(a);
            float surfaceB = NodeWaterSurfaceNoWave(b);
            float waterZ = surfaceA + (surfaceB - surfaceA) * t;

            return new Vector3(
                a.xPos + (b.xPos - a.xPos) * t,
                a.yPos + (b.yPos - a.yPos) * t,
                waterZ);
        }

        private void AddWaterPolygonPoint(List<Vector3> polygon, Vector3 point)
        {
            if (polygon.Count == 0)
            {
                polygon.Add(point);
                return;
            }

            Vector3 last = polygon[polygon.Count - 1];
            if ((last - point).LengthSquared < 0.0001f) return;

            polygon.Add(point);
        }

        private List<Vector3> BuildClippedWaterPolygon(Tile tile)
        {
            List<Vector3> polygon = new List<Vector3>(8);

            void AddEdge(Node start, Node end)
            {
                bool startWet = IsBelowWater(start);
                bool endWet = IsBelowWater(end);

                if (startWet)
                    AddWaterPolygonPoint(polygon, new Vector3(start.xPos, start.yPos, NodeWaterSurfaceNoWave(start)));

                if (startWet != endWet)
                    AddWaterPolygonPoint(polygon, IntersectWaterEdge(start, end));
            }

            AddEdge(tile.W, tile.S);
            AddEdge(tile.S, tile.E);
            AddEdge(tile.E, tile.N);
            AddEdge(tile.N, tile.W);

            if (polygon.Count > 1)
            {
                Vector3 first = polygon[0];
                Vector3 last = polygon[polygon.Count - 1];
                if ((first - last).LengthSquared < 0.0001f)
                    polygon.RemoveAt(polygon.Count - 1);
            }

            return polygon;
        }
    }
}
