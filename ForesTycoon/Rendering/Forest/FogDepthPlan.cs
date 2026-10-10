using System;

namespace ForesTycoon
{
    internal readonly record struct FogDepthPlan(int Width, int Height, long PayloadBytes)
    {
        internal static FogDepthPlan Create(int width, int height, long budgetBytes, int deviceLimit)
        {
            if (budgetBytes <= 0 || deviceLimit <= 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
            long pixels = (long)width * height;
            if (width == 0 || height == 0 || width > deviceLimit || height > deviceLimit || pixels > budgetBytes / 4) return default;
            return new(width, height, pixels * 4);
        }
    }
}
