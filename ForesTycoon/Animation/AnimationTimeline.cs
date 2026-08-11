using System;

namespace ForesTycoon
{
    public enum AnimationPlayback
    {
        Once,
        Loop,
        PingPong
    }

    /// <summary>
    /// Allocation-free animation time state. Simulation advances it at fixed steps and
    /// rendering samples between the previous and current state for smooth motion.
    /// </summary>
    public readonly struct AnimationTimeline
    {
        public AnimationTimeline(double durationSeconds, AnimationPlayback playback = AnimationPlayback.Once)
            : this(durationSeconds, playback, 0.0, 0.0)
        {
        }

        private AnimationTimeline(double durationSeconds, AnimationPlayback playback,
            double previousElapsedSeconds, double elapsedSeconds)
        {
            if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (!Enum.IsDefined(playback))
                throw new ArgumentOutOfRangeException(nameof(playback));
            DurationSeconds = durationSeconds;
            Playback = playback;
            PreviousElapsedSeconds = previousElapsedSeconds;
            ElapsedSeconds = elapsedSeconds;
        }

        public double DurationSeconds { get; }
        public AnimationPlayback Playback { get; }
        public double PreviousElapsedSeconds { get; }
        public double ElapsedSeconds { get; }
        public bool IsComplete => Playback == AnimationPlayback.Once && ElapsedSeconds >= DurationSeconds;

        public AnimationTimeline Advance(double deltaSeconds)
        {
            EnsureInitialized();
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

            double next = Playback == AnimationPlayback.Once
                ? Math.Min(DurationSeconds, ElapsedSeconds + deltaSeconds)
                : ElapsedSeconds + deltaSeconds;
            return new AnimationTimeline(DurationSeconds, Playback, ElapsedSeconds, next);
        }

        public double SampleTime(float interpolationAlpha)
        {
            EnsureInitialized();
            double alpha = Math.Clamp(interpolationAlpha, 0f, 1f);
            double time = PreviousElapsedSeconds + (ElapsedSeconds - PreviousElapsedSeconds) * alpha;
            return Playback switch
            {
                AnimationPlayback.Once => Math.Min(time, DurationSeconds),
                AnimationPlayback.Loop => PositiveModulo(time, DurationSeconds),
                AnimationPlayback.PingPong => PingPong(time, DurationSeconds),
                _ => throw new InvalidOperationException($"Unsupported playback mode: {Playback}.")
            };
        }

        public float SampleProgress(float interpolationAlpha) =>
            (float)(SampleTime(interpolationAlpha) / DurationSeconds);

        private static double PositiveModulo(double value, double modulus) =>
            ((value % modulus) + modulus) % modulus;

        private void EnsureInitialized()
        {
            if (DurationSeconds <= 0.0)
                throw new InvalidOperationException("The animation timeline was not initialized.");
        }

        private static double PingPong(double value, double duration)
        {
            double time = PositiveModulo(value, duration * 2.0);
            return time <= duration ? time : duration * 2.0 - time;
        }
    }
}
