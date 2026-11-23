using System;

namespace ForesTycoon
{
    internal static class ForestLodPolicy
    {
        private const float NearExit = 13f, NearEnter = 16f;
        internal static (ForestLod Low,ForestLod High,float Blend) Transition(float pixels)
        {
            if (!float.IsFinite(pixels)||pixels<=0)throw new ArgumentOutOfRangeException(nameof(pixels));
            if(pixels<=2.5f)return(ForestLod.Far,ForestLod.Far,0);
            if(pixels<4.5f)return(ForestLod.Far,ForestLod.Medium,Smooth((pixels-2.5f)/2));
            if(pixels<=NearExit)return(ForestLod.Medium,ForestLod.Medium,0);
            if(pixels<NearEnter)return(ForestLod.Medium,ForestLod.Near,Smooth((pixels-NearExit)/(NearEnter-NearExit)));
            return(ForestLod.Near,ForestLod.Near,0);
        }
        private static float Smooth(float value)=>value*value*(3-2*value);

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

}
