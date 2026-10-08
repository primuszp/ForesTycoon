using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Effects
{
    sealed class WorldEffectSystem : IWorldSystem
    {
        private readonly List<WorldEffect> active = new List<WorldEffect>();

        public IReadOnlyList<WorldEffect> Active => active;
        public int Count => active.Count;
        internal EffectCheckpoint[] Capture() {
            var result = new EffectCheckpoint[active.Count];
            for (int i = 0; i < result.Length; i++) { var e = active[i]; result[i] = new(e.Kind, new(e.Position), e.LifetimeSeconds, e.AgeSeconds, e.Timeline.PreviousElapsedSeconds); }
            return result;
        }
        internal void Restore(EffectCheckpoint[] state) {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var replacement = new List<WorldEffect>();
            foreach (var e in state) {
                CheckpointGuard.Require(e != null && Enum.IsDefined(e.Kind) && double.IsFinite(e.Lifetime) && e.Lifetime > 0 &&
                    double.IsFinite(e.Age) && e.Age >= 0 && e.Age < e.Lifetime, "effect timeline");
                e.Position.Validate(); replacement.Add(new WorldEffect(e.Kind, e.Position.Vector,
                    AnimationTimeline.Restore(e.Lifetime, e.PreviousAge, e.Age)));
            }
            active.Clear(); active.AddRange(replacement);
        }

        public void Spawn(WorldEffectKind kind, Vector3 position, double lifetimeSeconds = 0.65) =>
            active.Add(new WorldEffect(kind, position, lifetimeSeconds));

        public void Update(double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            int survivors = 0;
            for (int i = 0; i < active.Count; i++)
            {
                WorldEffect updated = active[i].Advance(deltaSeconds);
                if (!updated.IsExpired) active[survivors++] = updated;
            }
            active.RemoveRange(survivors, active.Count - survivors);
        }

        public void Clear() => active.Clear();
    }
}
