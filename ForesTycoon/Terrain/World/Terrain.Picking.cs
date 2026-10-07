using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        public bool TryRaycast(Vector3 rayNear, Vector3 rayFar, out Vector3 hit)
        {
            Vector3 direction = rayFar - rayNear;
            float bestT = float.MaxValue;
            Tile bestTile = null;
            hit = Vector3.Zero;

            foreach (TerrainChunk chunk in chunkIndex.Chunks)
            {
                if (!SegmentIntersectsBox(rayNear, direction, chunk.Min, chunk.Max)) continue;
                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    Tile tile = tiles[chunk.TileIds[i]];
                    GetPickingCorners(tile, out Vector3 w, out Vector3 s, out Vector3 e, out Vector3 n);
                    bool useWE = Math.Abs(w.Z - e.Z) <= Math.Abs(n.Z - s.Z);
                    if (flippedDiagonalTiles.Contains(tile.Id)) useWE = !useWE;

                    if (useWE)
                    {
                        TestTriangle(tile, w, s, e);
                        TestTriangle(tile, w, e, n);
                    }
                    else
                    {
                        TestTriangle(tile, n, w, s);
                        TestTriangle(tile, n, s, e);
                    }
                }
            }

            hoveredTile = bestTile;
            if (bestTile == null) return false;
            hit = rayNear + direction * bestT;
            return true;

            void TestTriangle(Tile tile, Vector3 a, Vector3 b, Vector3 c)
            {
                if (RayTriangle(rayNear, direction, a, b, c, out float t) && t < bestT)
                {
                    bestT = t;
                    bestTile = tile;
                }
            }
        }

        private static bool SegmentIntersectsBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
        {
            float enter = 0f, exit = 1f;
            return Axis(origin.X, direction.X, min.X, max.X, ref enter, ref exit)
                && Axis(origin.Y, direction.Y, min.Y, max.Y, ref enter, ref exit)
                && Axis(origin.Z, direction.Z, min.Z, max.Z, ref enter, ref exit);

            static bool Axis(float start, float delta, float lower, float upper, ref float enter, ref float exit)
            {
                if (Math.Abs(delta) < 1e-7f) return start >= lower && start <= upper;
                float a = (lower - start) / delta, b = (upper - start) / delta;
                if (a > b) (a, b) = (b, a);
                enter = Math.Max(enter, a);
                exit = Math.Min(exit, b);
                return enter <= exit;
            }
        }

        private void GetPickingCorners(Tile tile, out Vector3 w, out Vector3 s, out Vector3 e, out Vector3 n)
        {
            if (roads.Has(tile.Id))
            {
                w = RoadCorner(tile.W); s = RoadCorner(tile.S);
                e = RoadCorner(tile.E); n = RoadCorner(tile.N);
                return;
            }
            w = new Vector3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
            s = new Vector3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
            e = new Vector3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            n = new Vector3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            const float epsilon = 1e-7f;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Math.Abs(determinant) < epsilon) { t = 0f; return false; }

            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, p) * inverse;
            if (u < -epsilon || u > 1f + epsilon) { t = 0f; return false; }
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -epsilon || u + v > 1f + epsilon) { t = 0f; return false; }
            t = Vector3.Dot(edge2, q) * inverse;
            return t >= 0f && t <= 1f;
        }

        public bool SearchScreenPoint(double screenX, double screenY, double radiusPixels, double[] model, double[] proj, int[] view)
        {
            if (view == null || view.Length < 4 || view[2] <= 0 || view[3] <= 0)
            {
                actualNode = null;
                onpos = false;
                return false;
            }

            Matrix4d modelM = new Matrix4d(
                model[0], model[1], model[2], model[3],
                model[4], model[5], model[6], model[7],
                model[8], model[9], model[10], model[11],
                model[12], model[13], model[14], model[15]);

            Matrix4d projM = new Matrix4d(
                proj[0], proj[1], proj[2], proj[3],
                proj[4], proj[5], proj[6], proj[7],
                proj[8], proj[9], proj[10], proj[11],
                proj[12], proj[13], proj[14], proj[15]);

            Matrix4d viewProj = modelM * projM;
            double bestDistanceSq = radiusPixels * radiusPixels;
            Node bestNode = null;

            IEnumerable<Node> candidates = hoveredTile != null && !data.IsBorderTile(hoveredTile)
                ? new[] { hoveredTile.W, hoveredTile.S, hoveredTile.E, hoveredTile.N }
                : EnumerateBoundaryNodes();
            foreach (Node node in candidates)
            {
                Vector4d world = new Vector4d(node.xPos, node.yPos, node.zPos, 1.0);
                double clipX = world.X * viewProj.Row0.X + world.Y * viewProj.Row1.X + world.Z * viewProj.Row2.X + world.W * viewProj.Row3.X;
                double clipY = world.X * viewProj.Row0.Y + world.Y * viewProj.Row1.Y + world.Z * viewProj.Row2.Y + world.W * viewProj.Row3.Y;
                double clipW = world.X * viewProj.Row0.W + world.Y * viewProj.Row1.W + world.Z * viewProj.Row2.W + world.W * viewProj.Row3.W;
                if (Math.Abs(clipW) < 1e-12) continue;

                double projectedX = view[0] + (clipX / clipW + 1.0) * view[2] * 0.5;
                double projectedY = view[1] + (clipY / clipW + 1.0) * view[3] * 0.5;
                double dx = screenX - projectedX;
                double dy = screenY - projectedY;
                double distanceSq = dx * dx + dy * dy;
                if (distanceSq <= bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestNode = node;
                }
            }

            actualNode = bestNode;
            onpos = bestNode != null;
            return onpos;
        }

        private IEnumerable<Node> EnumerateBoundaryNodes()
        {
            for (int u = 0; u < nodeCols; u++)
            {
                yield return getNodeByCoords(u, 0);
                yield return getNodeByCoords(u, nodeRows - 1);
            }
            for (int v = 1; v < nodeRows - 1; v++)
            {
                yield return getNodeByCoords(0, v);
                yield return getNodeByCoords(nodeCols - 1, v);
            }
        }

        public bool SearchTile(double x, double y)
        {
            if (TryGetTileCoordinates(x, y, out int u, out int v, out _, out _))
            {
                hoveredTile = getTileByCoords(u, v);
                return true;
            }
            hoveredTile = null;
            return false;
        }

        public bool TryGetSurfaceZ(double x, double y, out float z)
        {
            if (!TryGetTileCoordinates(x, y, out int u, out int v, out double localX, out double localY))
            {
                z = 0.0f;
                return false;
            }

            Tile tile = getTileByCoords(u, v);
            float fx = (float)Math.Max(0.0, Math.Min(1.0, localX));
            float fy = (float)Math.Max(0.0, Math.Min(1.0, localY));

            float south = tile.W.zPos + (tile.S.zPos - tile.W.zPos) * fx;
            float north = tile.N.zPos + (tile.E.zPos - tile.N.zPos) * fx;
            z = south + (north - south) * fy;
            return true;
        }

        private bool TryGetTileCoordinates(double x, double y, out int u, out int v, out double localX, out double localY)
        {
            // Unprojection on sloped edge tiles can land a small fraction of a tile
            // beyond the mathematical boundary. Treat that as the edge tile itself.
            const double epsilon = 0.02;
            double gridX = (x + offsetX) / tileSizeH;
            double gridY = (y + offsetY) / tileSizeV;
            double maxX = nodeCols - 1;
            double maxY = nodeRows - 1;

            if (gridX < -epsilon || gridY < -epsilon || gridX > maxX + epsilon || gridY > maxY + epsilon)
            {
                u = v = 0;
                localX = localY = 0.0;
                return false;
            }

            gridX = Math.Max(0.0, Math.Min(maxX, gridX));
            gridY = Math.Max(0.0, Math.Min(maxY, gridY));

            u = (int)Math.Floor(gridX);
            v = (int)Math.Floor(gridY);
            if (u >= nodeCols - 1) u = nodeCols - 2;
            if (v >= nodeRows - 1) v = nodeRows - 2;

            localX = gridX - u;
            localY = gridY - v;
            return checkTile(u, v);
        }

        public void GetWorldBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (Node node in nodes)
            {
                min.X = Math.Min(min.X, node.xPos);
                min.Y = Math.Min(min.Y, node.yPos);
                min.Z = Math.Min(min.Z, node.zPos);
                max.X = Math.Max(max.X, node.xPos);
                max.Y = Math.Max(max.Y, node.yPos);
                max.Z = Math.Max(max.Z, node.zPos);
            }
        }

        public void ClearHover()
        {
            hoveredTile = null;
            onpos = false;
        }

        public void ClearTileHover()
        {
            hoveredTile = null;
        }
    }
}
