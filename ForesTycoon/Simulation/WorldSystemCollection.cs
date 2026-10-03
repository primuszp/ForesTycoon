using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>Runs world systems in stable registration order without per-tick allocations.</summary>
    sealed class WorldSystemCollection
    {
        private readonly List<IWorldSystem> systems = new List<IWorldSystem>();

        public int Count => systems.Count;

        public T Add<T>(T system) where T : class, IWorldSystem
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (systems.Contains(system))
                throw new InvalidOperationException("The world system is already registered.");

            systems.Add(system);
            return system;
        }

        public void Update(double fixedDeltaSeconds)
        {
            if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
            for (int i = 0; i < systems.Count; i++)
                systems[i].Update(fixedDeltaSeconds);
        }

        public void Clear()
        {
            for (int i = systems.Count - 1; i >= 0; i--)
                systems[i].Clear();
        }
    }
}
