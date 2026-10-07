using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    sealed class WorldEffectSystem : IWorldSystem
    {
        private readonly List<WorldEffect> active = new List<WorldEffect>();

        public IReadOnlyList<WorldEffect> Active => active;
        public int Count => active.Count;

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
