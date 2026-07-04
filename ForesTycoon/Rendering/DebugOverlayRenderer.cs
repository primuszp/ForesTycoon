using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal static class DebugOverlayRenderer
    {
        public static void DrawNodeMarker(Node node)
        {
            if (node == null) return;

            GL.PushMatrix();
            {
                GL.Translate(node.xPos, node.yPos, node.zPos);
                DrawSphere(0.55f, 16, 16);
            }
            GL.PopMatrix();
        }

        public static void DrawHoveredTile(Tile tile)
        {
            if (tile == null) return;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.Begin(PrimitiveType.Triangles);
            GL.Color4(Color.FromArgb(90, 255, 235, 60));
            if (Math.Abs(tile.W.zPos - tile.E.zPos) <= Math.Abs(tile.N.zPos - tile.S.zPos))
            {
                GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
            }
            else
            {
                GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            }
            GL.End();

            GL.LineWidth(4.0f);
            GL.Begin(PrimitiveType.LineLoop);
            GL.Color4(Color.FromArgb(245, 255, 240, 80));
            GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
            GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
            GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
            GL.End();
            GL.LineWidth(2.0f);

            GL.Disable(EnableCap.Blend);
        }

        private static void DrawSphere(float radius, int rings, int sectors)
        {
            Vector3 lightDir = new Vector3(0.5f, -0.5f, 1.0f);
            lightDir.Normalize();

            GL.Begin(PrimitiveType.Quads);
            for (int i = 0; i < rings; i++)
            {
                float theta1 = (float)(i * Math.PI / rings) - (float)(Math.PI / 2);
                float theta2 = (float)((i + 1) * Math.PI / rings) - (float)(Math.PI / 2);
                for (int j = 0; j < sectors; j++)
                {
                    float phi1 = (float)(j * 2 * Math.PI / sectors);
                    float phi2 = (float)((j + 1) * 2 * Math.PI / sectors);
                    Vector3 n1 = SphereNormal(theta1, phi1);
                    Vector3 n2 = SphereNormal(theta1, phi2);
                    Vector3 n3 = SphereNormal(theta2, phi2);
                    Vector3 n4 = SphereNormal(theta2, phi1);
                    GL.Color3(ShadedWhite(n1, lightDir)); GL.Vertex3(n1 * radius);
                    GL.Color3(ShadedWhite(n2, lightDir)); GL.Vertex3(n2 * radius);
                    GL.Color3(ShadedWhite(n3, lightDir)); GL.Vertex3(n3 * radius);
                    GL.Color3(ShadedWhite(n4, lightDir)); GL.Vertex3(n4 * radius);
                }
            }
            GL.End();
        }

        private static Vector3 SphereNormal(float theta, float phi) => new Vector3(
            (float)(Math.Cos(theta) * Math.Cos(phi)),
            (float)(Math.Cos(theta) * Math.Sin(phi)),
            (float)Math.Sin(theta));

        private static Color ShadedWhite(Vector3 normal, Vector3 light)
        {
            float i = Math.Max(0.65f, Math.Min(1.0f, Vector3.Dot(normal, light) * 0.85f + 0.25f));
            return Color.FromArgb((int)(255 * i), (int)(255 * i), (int)(255 * i));
        }
    }
}
