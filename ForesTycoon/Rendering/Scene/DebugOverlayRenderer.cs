using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal static class DebugOverlayRenderer
    {
        public static void DrawNodeMarker(Node node, float radius)
        {
            if (node == null) return;

            float markerRadius = Math.Max(0.12f, Math.Min(1.2f, radius));

            RenderDevice.PushModel();
            {
                RenderDevice.Translate(node.xPos, node.yPos, node.zPos);
                using (RenderDevice.CreateStateScope()
                    .Disable(RenderCapability.DepthTest)
                    .DepthWrite(false)
                    )
                {
                    DrawSphere(markerRadius, 24, 32);
                }
            }
            RenderDevice.PopModel();
        }

        public static void DrawHoveredTile(Tile tile)
        {
            if (tile == null) return;

            using (RenderDevice.CreateStateScope().AlphaBlend())
            {
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Triangles, () =>
                {
                    DynamicPrimitiveBatch.Color4(Color.FromArgb(90, 255, 235, 60));
                    if (Math.Abs(tile.W.zPos - tile.E.zPos) <= Math.Abs(tile.N.zPos - tile.S.zPos))
                    {
                        DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                    }
                    else
                    {
                        DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                    }
                });

                using (RenderDevice.CreateStateScope().ThinLines())
                {
                    DynamicPrimitiveBatch.Draw(PrimitiveTopology.LineLoop, () =>
                    {
                        DynamicPrimitiveBatch.Color4(Color.FromArgb(245, 255, 240, 80));
                        DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                        DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                    });
                }
            }
        }

        private static void DrawSphere(float radius, int rings, int sectors)
        {
            Vector3 lightDir = new Vector3(0.5f, -0.5f, 1.0f);
            lightDir.Normalize();

            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
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
                        DynamicPrimitiveBatch.Color3(ShadedWhite(n1, lightDir)); DynamicPrimitiveBatch.Vertex3(n1 * radius);
                        DynamicPrimitiveBatch.Color3(ShadedWhite(n2, lightDir)); DynamicPrimitiveBatch.Vertex3(n2 * radius);
                        DynamicPrimitiveBatch.Color3(ShadedWhite(n3, lightDir)); DynamicPrimitiveBatch.Vertex3(n3 * radius);
                        DynamicPrimitiveBatch.Color3(ShadedWhite(n4, lightDir)); DynamicPrimitiveBatch.Vertex3(n4 * radius);
                    }
                }
            });
        }

        private static Vector3 SphereNormal(float theta, float phi) => new Vector3(
            (float)(Math.Cos(theta) * Math.Cos(phi)),
            (float)(Math.Cos(theta) * Math.Sin(phi)),
            (float)Math.Sin(theta));

        private static Color ShadedWhite(Vector3 normal, Vector3 light)
        {
            float diffuse = Math.Max(0.0f, Math.Min(1.0f, Vector3.Dot(normal, light)));
            float i = 0.76f + diffuse * 0.24f;
            int value = Math.Max(0, Math.Min(255, (int)(255 * i)));
            return Color.FromArgb(value, value, value);
        }
    }
}
