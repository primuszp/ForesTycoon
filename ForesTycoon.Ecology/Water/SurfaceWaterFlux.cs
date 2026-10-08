using System;

namespace ForesTycoon.Ecology
{
    /// <summary>Local runoff response: retained surface water in mm and release rate per weather hour.</summary>
    internal readonly record struct SurfaceRunoffLaw
    {
        internal static SurfaceRunoffLaw Legacy { get; } = new(2, 6);
        internal double RetainedWater { get; }
        internal double ReleasePerHour { get; }

        internal SurfaceRunoffLaw(double retainedWater, double releasePerHour)
        {
            if (!double.IsFinite(retainedWater) || retainedWater < 0) throw new ArgumentOutOfRangeException(nameof(retainedWater));
            if (!double.IsFinite(releasePerHour) || releasePerHour < 0) throw new ArgumentOutOfRangeException(nameof(releasePerHour));
            RetainedWater = retainedWater; ReleasePerHour = releasePerHour;
        }

        // The exponential depends only on the interval, so prepare it once outside the raster loop.
        internal SurfaceRunoffStep Prepare(double hours)
        {
            if (!double.IsFinite(hours) || hours < 0) throw new ArgumentOutOfRangeException(nameof(hours));
            return new(RetainedWater, ReleasePerHour == 0 ? 0 : 1 - Math.Exp(-hours * ReleasePerHour));
        }
    }

    internal readonly struct SurfaceRunoffStep
    {
        private readonly double retainedWater, releasedFraction;
        internal SurfaceRunoffStep(double retainedWater, double releasedFraction)
        {
            if (!double.IsFinite(retainedWater) || retainedWater < 0) throw new ArgumentOutOfRangeException(nameof(retainedWater));
            if (!double.IsFinite(releasedFraction) || releasedFraction < 0 || releasedFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(releasedFraction));
            this.retainedWater = retainedWater; this.releasedFraction = releasedFraction;
        }
        internal double Amount(double available)
        {
            if (!double.IsFinite(available) || available < 0) throw new ArgumentOutOfRangeException(nameof(available));
            return Math.Min(available, Math.Max(0, available - retainedWater) * releasedFraction);
        }
    }

    /// <summary>
    /// Stages one downhill flux per source cell. Incoming water is committed after every source
    /// has been evaluated, so it cannot traverse several cells in a single step. Equal-area cell
    /// depths conserve volume; only explicit outlets export water. No per-step allocations.
    /// </summary>
    internal sealed class SurfaceWaterFlux
    {
        private readonly double[] delta;
        private readonly bool[] scheduled;
        private bool active;

        internal SurfaceWaterFlux(int cells)
        {
            if (cells <= 0) throw new ArgumentOutOfRangeException(nameof(cells));
            delta = new double[cells]; scheduled = new bool[cells];
        }

        internal void BeginStep()
        {
            if (active) throw new InvalidOperationException("Commit or cancel the previous surface flux step.");
            Array.Clear(delta); Array.Clear(scheduled); active = true;
        }

        // Returns exported depth, allowing the caller to accumulate its existing ordered water ledger.
        internal double Schedule(int source, int destination, bool outlet, double available, SurfaceRunoffStep step)
        {
            if (!active) throw new InvalidOperationException("Begin a surface flux step first.");
            if ((uint)source >= (uint)delta.Length) throw new ArgumentOutOfRangeException(nameof(source));
            if (destination < -1 || destination >= delta.Length || destination == source)
                throw new ArgumentOutOfRangeException(nameof(destination));
            if (scheduled[source]) throw new InvalidOperationException("A surface cell can send water only once per step.");
            double amount = step.Amount(available);
            scheduled[source] = true;
            if (destination >= 0) { delta[source] -= amount; delta[destination] += amount; return 0; }
            if (!outlet) return 0;
            delta[source] -= amount; return amount;
        }

        internal void Commit(Span<double> surface)
        {
            if (!active) throw new InvalidOperationException("There is no active surface flux step.");
            if (surface.Length != delta.Length) throw new ArgumentException("Surface raster length mismatch.", nameof(surface));
            // Validate the whole commit before mutating any cell; never clamp away a balance error.
            for (int id = 0; id < surface.Length; id++)
                if (!double.IsFinite(surface[id]) || surface[id] < 0 ||
                    !double.IsFinite(surface[id] + delta[id]) || surface[id] + delta[id] < 0)
                    throw new InvalidOperationException("Surface water changed below its scheduled outgoing flux.");
            for (int id = 0; id < surface.Length; id++) surface[id] += delta[id];
            active = false;
        }

        internal void Cancel() { Array.Clear(delta); Array.Clear(scheduled); active = false; }
    }
}
