using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Effects
{
    internal readonly record struct WeatherParticlePlan(Vector2 Origin, float CellSize, int Columns, int PerCell, int Count)
    {
        internal const int MaxParticles = 65536;
        internal const int MaxHeightSamples = 4 * 1024 * 1024;
        internal static WeatherParticlePlan Create(Vector2 min, Vector2 max, int budget)
        {
            if (budget < 0 || budget > MaxParticles) throw new ArgumentOutOfRangeException(nameof(budget));
            if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(max.X) || !float.IsFinite(max.Y))
                throw new ArgumentOutOfRangeException(nameof(min));
            if (budget == 0 || max.X <= min.X || max.Y <= min.Y) return default;
            double width = (double)max.X - min.X, height = (double)max.Y - min.Y, cell = 8;
            // Padding has an irreducible minimum. Small budgets cap submissions rather
            // than spinning forever trying to shrink a grid below that minimum.
            double target = Math.Max(36, budget / 2);
            while ((Math.Ceiling(width / cell) + 5) * (Math.Ceiling(height / cell) + 5) > target) cell *= 2;
            if (cell > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(max));
            double x = Math.Floor(min.X / cell) - 2, y = Math.Floor(min.Y / cell) - 2;
            int columns = checked((int)(Math.Ceiling(max.X / cell) - x + 2));
            int rows = checked((int)(Math.Ceiling(max.Y / cell) - y + 2));
            int cells = checked(columns * rows);
            int perCell = Math.Clamp(budget / Math.Max(1, cells), 1, 24);
            return new(new Vector2((float)x, (float)y), (float)cell, columns, perCell, Math.Min(budget, cells * perCell));
        }
    }

    internal readonly record struct EffectMetrics(int Particles, int CloudSteps, long CpuPayloadBytes, long GpuPayloadBytes, double CpuMilliseconds, double CpuBudgetMilliseconds = 2)
    {
        internal bool CpuBudgetExceeded => CpuMilliseconds > CpuBudgetMilliseconds;
    }
}
