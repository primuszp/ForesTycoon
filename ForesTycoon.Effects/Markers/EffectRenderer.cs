using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon.Effects
{
    static class EffectRenderer
    {
        public static void Draw(WorldEffectSystem effects, float interpolationAlpha)
        {
            if (effects.Count == 0) return;

            using (RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false))
            {
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Lines, () =>
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
                WorldEffectKind.TreePlanted => Color.FromArgb(210, 105, 210, 95),
                WorldEffectKind.ForestHarvested => Color.FromArgb(210, 210, 145, 72),
                WorldEffectKind.ForestryRejected => Color.FromArgb(230, 235, 72, 72),
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
