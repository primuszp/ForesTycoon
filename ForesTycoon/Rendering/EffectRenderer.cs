using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    static class EffectRenderer
    {
        public static void Draw(WorldEffectSystem effects)
        {
            if (effects.Count == 0) return;

            using (new RenderStateScope().AlphaBlend().DepthWrite(false))
            {
                foreach (WorldEffect effect in effects.Active)
                    DrawPulse(effect);
            }
        }

        private static void DrawPulse(WorldEffect effect)
        {
            Color color = effect.Kind switch
            {
                WorldEffectKind.TerrainChanged => Color.FromArgb(210, 214, 176, 78),
                WorldEffectKind.RoadChanged => Color.FromArgb(210, 235, 225, 170),
                WorldEffectKind.VehicleSpawned => Color.FromArgb(210, 130, 205, 255),
                _ => Color.White
            };
            float radius = 0.5f + effect.Progress * 2.2f;
            float z = effect.Position.Z + 0.08f + effect.Progress * 0.25f;

            GL.Color4(Color.FromArgb((int)(color.A * (1f - effect.Progress)), color));
            ImmediateRenderer.Draw(PrimitiveType.LineLoop, () =>
            {
                const int segments = 20;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * MathF.Tau / segments;
                    GL.Vertex3(effect.Position.X + MathF.Cos(angle) * radius,
                        effect.Position.Y + MathF.Sin(angle) * radius, z);
                }
            });
        }
    }
}
