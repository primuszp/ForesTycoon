using System;

namespace ForesTycoon
{
    internal enum ForestLod { Far, Medium, Near }

    internal static class ForestLodPolicy
    {
        internal static (ForestLod Low,ForestLod High,float Blend) Transition(float pixels)
        {
            if (!float.IsFinite(pixels)||pixels<=0)throw new ArgumentOutOfRangeException(nameof(pixels));
            if(pixels<=2.5f)return(ForestLod.Far,ForestLod.Far,0);
            if(pixels<4.5f)return(ForestLod.Far,ForestLod.Medium,Smooth((pixels-2.5f)/2));
            if(pixels<=7)return(ForestLod.Medium,ForestLod.Medium,0);
            if(pixels<11)return(ForestLod.Medium,ForestLod.Near,Smooth((pixels-7)/4));
            return(ForestLod.Near,ForestLod.Near,0);
        }
        private static float Smooth(float value)=>value*value*(3-2*value);

        internal static ForestLod Select(float pixelsPerWorldUnit, ForestLod? previous)
        {
            if (!float.IsFinite(pixelsPerWorldUnit) || pixelsPerWorldUnit <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerWorldUnit));
            // Different enter/leave thresholds prevent repeated rebuilds near a boundary.
            if (pixelsPerWorldUnit >= (previous == ForestLod.Near ? 7f : 9f)) return ForestLod.Near;
            if (pixelsPerWorldUnit >= (previous == ForestLod.Far || previous == null ? 3.5f : 2.5f))
                return ForestLod.Medium;
            return ForestLod.Far;
        }
    }

}
