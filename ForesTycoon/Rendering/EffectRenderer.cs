using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    static class EffectRenderer
    {
        public static void Draw(WorldEffectSystem effects, float interpolationAlpha)
        {
            if (effects.Count == 0) return;

            using (new RenderStateScope().AlphaBlend().DepthWrite(false))
            {
                DynamicPrimitiveBatch.Draw(PrimitiveType.Lines, () =>
                {
                    foreach (WorldEffect effect in effects.Active)
                        DrawPulse(effect, interpolationAlpha);
                });
            }
        }

        private static void DrawPulse(WorldEffect effect, float interpolationAlpha)
        {
            Color color = effect.Kind switch
            {
                WorldEffectKind.TerrainChanged => Color.FromArgb(210, 214, 176, 78),
                WorldEffectKind.RoadChanged => Color.FromArgb(210, 235, 225, 170),
                WorldEffectKind.VehicleSpawned => Color.FromArgb(210, 130, 205, 255),
                _ => Color.White
            };
            float progress = effect.Timeline.SampleProgress(interpolationAlpha);
            float radius = 0.5f + progress * 2.2f;
            float z = effect.Position.Z + 0.08f + progress * 0.25f;

            DynamicPrimitiveBatch.Color4(Color.FromArgb((int)(color.A * (1f - progress)), color));
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float angleA = i * MathF.Tau / segments;
                float angleB = (i + 1) * MathF.Tau / segments;
                DynamicPrimitiveBatch.Vertex3(effect.Position.X + MathF.Cos(angleA) * radius,
                    effect.Position.Y + MathF.Sin(angleA) * radius, z);
                DynamicPrimitiveBatch.Vertex3(effect.Position.X + MathF.Cos(angleB) * radius,
                    effect.Position.Y + MathF.Sin(angleB) * radius, z);
            }
        }
    }
}
