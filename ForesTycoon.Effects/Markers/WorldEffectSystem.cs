using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Effects
{
    sealed class WorldEffectSystem : IWorldSystem
    {
        internal const int MaxActiveEffects = 4096;
        private readonly WorldEffect[] active = new WorldEffect[MaxActiveEffects];
        private readonly IReadOnlyList<WorldEffect> view;
        private int start, count;
        internal long DroppedEffects { get; private set; }
        internal long CpuPayloadBytes => (long)active.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<WorldEffect>();
        internal WorldEffectSystem() => view = new ActiveView(this);
        private WorldEffect At(int index) => active[(start + index) % MaxActiveEffects];

        public IReadOnlyList<WorldEffect> Active => view;
        public int Count => count;
        internal EffectCheckpoint[] Capture() {
            var result = new EffectCheckpoint[count];
            for (int i = 0; i < result.Length; i++) { var e = At(i); result[i] = new(e.Kind, new(e.Position), e.LifetimeSeconds, e.AgeSeconds, e.Timeline.PreviousElapsedSeconds); }
            return result;
        }
        internal void Restore(EffectCheckpoint[] state) {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var replacement = new WorldEffect[Math.Min(state.Length, MaxActiveEffects)];
            int first = Math.Max(0, state.Length - MaxActiveEffects);
            for (int i = 0; i < state.Length; i++) { var e = state[i];
                CheckpointGuard.Require(e != null && Enum.IsDefined(e.Kind) && double.IsFinite(e.Lifetime) && e.Lifetime > 0 &&
                    double.IsFinite(e.Age) && e.Age >= 0 && e.Age < e.Lifetime, "effect timeline");
                e.Position.Validate(); var timeline = AnimationTimeline.Restore(e.Lifetime, e.PreviousAge, e.Age);
                if (i >= first) replacement[i - first] = new WorldEffect(e.Kind, e.Position.Vector, timeline);
            }
            Array.Clear(active); replacement.CopyTo(active, 0); start = 0; count = replacement.Length;
            DroppedEffects = first; // Historical overflows retain their newest visual feedback.
        }

        public void Spawn(WorldEffectKind kind, Vector3 position, double lifetimeSeconds = 0.65)
        {
            if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new ArgumentOutOfRangeException(nameof(position));
            var effect = new WorldEffect(kind, position, lifetimeSeconds);
            if (count == MaxActiveEffects) { active[start] = effect; start = (start + 1) % MaxActiveEffects; DroppedEffects++; }
            else { active[(start + count) % MaxActiveEffects] = effect; count++; }
        }

        public void Update(double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            int survivors = 0;
            for (int i = 0; i < count; i++)
            {
                WorldEffect updated = At(i).Advance(deltaSeconds);
                if (!updated.IsExpired) active[(start + survivors++) % MaxActiveEffects] = updated;
            }
            for (int i = survivors; i < count; i++) active[(start + i) % MaxActiveEffects] = default;
            count = survivors;
        }

        public void Clear() { Array.Clear(active); start = count = 0; DroppedEffects = 0; }

        private sealed class ActiveView : IReadOnlyList<WorldEffect>
        {
            private readonly WorldEffectSystem owner;
            internal ActiveView(WorldEffectSystem owner) => this.owner = owner;
            public int Count => owner.count;
            public WorldEffect this[int index] => index >= 0 && index < Count ? owner.At(index) : throw new ArgumentOutOfRangeException(nameof(index));
            public IEnumerator<WorldEffect> GetEnumerator() { for (int i = 0; i < Count; i++) yield return owner.At(i); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
