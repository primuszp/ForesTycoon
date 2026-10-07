using System;

namespace ForesTycoon.Ecology
{
    /// <summary>Derives the long-lived site of a tree (gap direction, wind exposure) from its surroundings.</summary>
    internal static class ForestTreeSites
    {
        /// <summary>Prevailing wind: blows towards this angle in the ground plane.</summary>
        internal const float PrevailingWindAngle = 0.6f;

        /// <summary>
        /// Direction of the nearest canopy gap, from the other trees of the same tile. Crowns lean and
        /// grow away from their neighbours; GapStrength is 0 when surrounded evenly, 1 when all
        /// competition comes from one side. Metres; tile U/V span the tile width/height.
        /// </summary>
        internal static TreeSite Gap(ForestTree[] trees, int count, int index, float tileWidth, float tileHeight,
            double year, float elevation)
        {
            var self = trees[index];
            var size = self.At(year);
            float vx = 0, vy = 0, total = 0;
            for (int j = 0; j < count; j++)
            {
                if (j == index) continue;
                var other = trees[j];
                float dx = (other.U - self.U) * tileWidth, dy = (other.V - self.V) * tileHeight;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance < 1e-3f) continue;
                float reach = size.CrownRadius + other.At(year).CrownRadius;
                float weight = Math.Min(2, reach / distance);
                weight *= weight;
                vx -= weight * dx / distance; vy -= weight * dy / distance; total += weight;
            }
            float magnitude = MathF.Sqrt(vx * vx + vy * vy);
            float strength = total > 0 ? Math.Clamp(magnitude / (total + 0.5f), 0, 1) : 0;
            // Only the high ground is exposed enough for persistent wind shaping.
            float wind = Math.Clamp((elevation - 0.6f) / 0.4f, 0, 1);
            return new(1, 1, wind, PrevailingWindAngle, magnitude > 1e-4f ? MathF.Atan2(vy, vx) : 0, strength);
        }
    }
}
