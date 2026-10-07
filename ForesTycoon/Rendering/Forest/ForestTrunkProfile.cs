using System;

namespace ForesTycoon
{
    /// <summary>Shared bole taper: standing trees and cut stems use the same cross-section.</summary>
    internal readonly record struct ForestTrunkProfile(float Height, float Radius)
    {
        internal const float RootDepth = 1f;
        internal float BottomRadius => Radius * 1.40f;
        internal float TopRadius => Radius * 0.72f;
        internal float RadiusAt(float heightAboveGround)
        {
            float t = Math.Clamp((heightAboveGround + RootDepth) / (Height + RootDepth), 0, 1);
            return BottomRadius + (TopRadius - BottomRadius) * t;
        }

        // No minimum radius: even the smallest sapling leaves a proportionate stump.
        internal float CutHeight => MathF.Min(Height * 0.45f, RadiusAt(0) * 1.15f);
    }
}
