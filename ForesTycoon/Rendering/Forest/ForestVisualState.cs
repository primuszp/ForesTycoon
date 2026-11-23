using System;

namespace ForesTycoon
{
    internal static class ForestLodPolicy
    {
        private const float NearExit = 13f, NearEnter = 16f;

        internal static ForestLod Select(float pixelsPerWorldUnit, ForestLod? previous)
        {
            if (!float.IsFinite(pixelsPerWorldUnit) || pixelsPerWorldUnit <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerWorldUnit));
            // Different enter/leave thresholds prevent repeated rebuilds near a boundary.
            if (pixelsPerWorldUnit >= (previous == ForestLod.Near ? NearExit : NearEnter)) return ForestLod.Near;
            if (pixelsPerWorldUnit >= (previous == ForestLod.Far || previous == null ? 3.5f : 2.5f))
                return ForestLod.Medium;
            return ForestLod.Far;
        }

        // The depth texture needs branch silhouettes only where it has enough texels to resolve them.
        // A shadow does not need the camera's fine crown or bark mesh.
        internal static ForestLod SelectShadow(float texelsPerWorldUnit) => texelsPerWorldUnit >= 8 ? ForestLod.Medium : ForestLod.Far;
    }

    internal readonly record struct ForestLodFade(ForestLod Low, ForestLod High, float Progress)
    {
        private const float DurationSeconds = 0.22f;
        // Keep several visible steps even on a slow frame or after a backgrounded window resumes.
        private const float MaxStepSeconds = 1f / 30;

        internal static ForestLodFade Start(ForestLod current, ForestLod next) =>
            current < next ? new(current, next, 0) : new(next, current, 1);

        internal float HighCoverage => Progress * Progress * (3 - 2 * Progress);
        internal ForestLod? Endpoint => Progress <= 0 ? Low : Progress >= 1 ? High : null;

        internal ForestLodFade Advance(ForestLod target, float deltaSeconds)
        {
            // Both directions use the same masks. If a jump skips a level and the camera
            // comes back between the two endpoints, complete towards the dominant model.
            float direction = target <= Low || (target < High && Progress < 0.5f) ? -1 : 1;
            float step = Math.Clamp(deltaSeconds, 0, MaxStepSeconds) / DurationSeconds;
            return this with { Progress = Math.Clamp(Progress + direction * step, 0, 1) };
        }
    }
}
