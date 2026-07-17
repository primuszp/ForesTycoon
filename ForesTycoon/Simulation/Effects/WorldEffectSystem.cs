using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    sealed class WorldEffectSystem
    {
        private readonly List<WorldEffect> active = new List<WorldEffect>();

        public IReadOnlyList<WorldEffect> Active => active;
        public int Count => active.Count;

        public void Spawn(WorldEffectKind kind, Vector3 position, double lifetimeSeconds = 0.65) =>
            active.Add(new WorldEffect(kind, position, lifetimeSeconds));

        public void Update(double deltaSeconds)
        {
            if (deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            for (int i = active.Count - 1; i >= 0; i--)
            {
                WorldEffect updated = active[i].Advance(deltaSeconds);
                if (updated.IsExpired) active.RemoveAt(i);
                else active[i] = updated;
            }
        }

        public void Clear() => active.Clear();
    }
}
