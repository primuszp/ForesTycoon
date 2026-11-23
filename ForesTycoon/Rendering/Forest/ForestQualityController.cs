using System;

namespace ForesTycoon
{
    internal enum ForestDetailMode { Adaptive, FixedMedium, FixedHigh, Camera }

    internal readonly record struct ForestGpuSample(double Time, double FrameMs, double ForestMs,
        ForestLod Lod, bool Ready, bool NearFits);

    /// <summary>Chooses one camera-independent detail level from completed GPU work.</summary>
    internal sealed class ForestQualityController
    {
        internal const long DefaultNearBudget = 1280L * 1024 * 1024;
        private ForestDetailMode mode = ForestDetailMode.Camera;
        private double previousTime = double.NaN;
        private double slowSeconds, spareSeconds, nextPromotion;
        private int slowSamples, spareSamples;

        internal ForestDetailMode Mode {
            get => mode;
            set {
                if (mode == value) return;
                mode = value;
                Reset();
            }
        }
        internal ForestLod AdaptiveLod { get; private set; } = ForestLod.Medium;
        internal long NearBudgetBytes { get; set; } = DefaultNearBudget;
        internal ForestGpuSample? LastSample { get; private set; }

        internal void Reset()
        {
            AdaptiveLod = ForestLod.Medium;
            nextPromotion = 0;
            DiscardHistory();
        }

        internal void DiscardHistory()
        {
            previousTime = double.NaN;
            LastSample = null;
            ClearEvidence();
        }

        private void ClearEvidence()
        {
            slowSeconds = spareSeconds = 0;
            slowSamples = spareSamples = 0;
        }

        internal ForestLod Select(float pixelsPerWorldUnit, ForestLod? previous) => Mode switch {
            ForestDetailMode.Adaptive => AdaptiveLod,
            ForestDetailMode.FixedMedium => ForestLod.Medium,
            ForestDetailMode.FixedHigh => ForestLod.Near,
            _ => ForestLodPolicy.Select(pixelsPerWorldUnit, previous)
        };

        internal void LimitNearForMemory(double time)
        {
            if (Mode == ForestDetailMode.Adaptive && AdaptiveLod == ForestLod.Near)
                Change(ForestLod.Medium, time + 10);
        }

        internal void Observe(ForestGpuSample sample)
        {
            if (Mode != ForestDetailMode.Adaptive || sample.Lod != AdaptiveLod ||
                !double.IsFinite(sample.Time) || !double.IsFinite(sample.FrameMs) ||
                !double.IsFinite(sample.ForestMs) || sample.FrameMs <= 0 || sample.ForestMs < 0) return;
            if (double.IsFinite(previousTime) && sample.Time <= previousTime) return;
            double dt = double.IsFinite(previousTime) ? Math.Min(.5, sample.Time - previousTime) : 0;
            previousTime = sample.Time;
            LastSample = sample;

            // A slow frame is actionable only if forest drawing is a meaningful share of GPU work.
            bool slow = sample.FrameMs > 25 && sample.ForestMs >= Math.Max(3.5, sample.FrameMs * .18);
            if (slow && AdaptiveLod > ForestLod.Far) {
                slowSeconds += dt;
                slowSamples++;
                spareSeconds = spareSamples = 0;
                if (slowSeconds >= .9 && slowSamples >= 4)
                    Change(AdaptiveLod - 1, sample.Time + 30);
                return;
            }

            // A healthy lower level is only a starting point. Leave room for the expected
            // increase in forest work and keep the model stable if the prediction is marginal.
            double nextLodWork = AdaptiveLod == ForestLod.Far ? 3 : 5;
            double nextFrame = sample.FrameMs + sample.ForestMs * (nextLodWork - 1);
            bool spare = AdaptiveLod < ForestLod.Near && sample.Time >= nextPromotion && sample.Ready &&
                (AdaptiveLod == ForestLod.Far || sample.NearFits) && nextFrame <= 18;
            if (spare) {
                spareSeconds += dt;
                spareSamples++;
                slowSeconds = slowSamples = 0;
                if (spareSeconds >= 4 && spareSamples >= 12)
                    Change(AdaptiveLod + 1, sample.Time + 4);
                return;
            }
            ClearEvidence();
        }

        private void Change(ForestLod lod, double promotionTime)
        {
            AdaptiveLod = lod;
            nextPromotion = promotionTime;
            DiscardHistory();
        }
    }
}
