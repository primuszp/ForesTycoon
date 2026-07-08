using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
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

            foreach (Node node in nodes)
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
            const double epsilon = 1e-6;
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
