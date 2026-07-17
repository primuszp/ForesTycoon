using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    enum WorldEffectKind
    {
        TerrainChanged,
        RoadChanged,
        VehicleSpawned
    }

    readonly struct WorldEffect
    {
        public WorldEffect(WorldEffectKind kind, Vector3 position, double lifetimeSeconds)
        {
            if (lifetimeSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
            Kind = kind;
            Position = position;
            LifetimeSeconds = lifetimeSeconds;
            AgeSeconds = 0;
        }

        public WorldEffectKind Kind { get; }
        public Vector3 Position { get; }
        public double LifetimeSeconds { get; }
        public double AgeSeconds { get; }
        public bool IsExpired => AgeSeconds >= LifetimeSeconds;
        public float Progress => (float)Math.Clamp(AgeSeconds / LifetimeSeconds, 0.0, 1.0);

        public WorldEffect Advance(double deltaSeconds) => new WorldEffect(Kind, Position, LifetimeSeconds, AgeSeconds + deltaSeconds);

        private WorldEffect(WorldEffectKind kind, Vector3 position, double lifetimeSeconds, double ageSeconds)
        {
            Kind = kind;
            Position = position;
            LifetimeSeconds = lifetimeSeconds;
            AgeSeconds = ageSeconds;
        }
    }
}
