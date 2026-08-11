using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    enum WorldEffectKind
    {
        TerrainChanged,
        RoadChanged,
        VehicleSpawned,
        TreePlanted,
        ForestHarvested
    }

    readonly struct WorldEffect
    {
        public WorldEffect(WorldEffectKind kind, Vector3 position, double lifetimeSeconds)
        {
            Kind = kind;
            Position = position;
            Timeline = new AnimationTimeline(lifetimeSeconds);
        }

        public WorldEffectKind Kind { get; }
        public Vector3 Position { get; }
        public AnimationTimeline Timeline { get; }
        public double LifetimeSeconds => Timeline.DurationSeconds;
        public double AgeSeconds => Timeline.ElapsedSeconds;
        public bool IsExpired => Timeline.IsComplete;
        public float Progress => Timeline.SampleProgress(1f);

        public WorldEffect Advance(double deltaSeconds) => new WorldEffect(Kind, Position, Timeline.Advance(deltaSeconds));

        private WorldEffect(WorldEffectKind kind, Vector3 position, AnimationTimeline timeline)
        {
            Kind = kind;
            Position = position;
            Timeline = timeline;
        }
    }
}
