using System;

namespace ForesTycoon
{
    /// <summary>Authoritative timber inventory shared by forestry and transport systems.</summary>
    sealed class TimberCargoSystem : IWorldSystem
    {
        public float Available { get; private set; }
        public float Delivered { get; private set; }

        public void AddHarvested(float amount)
        {
            if (!float.IsFinite(amount) || amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            Available += amount;
        }

        public float Load(float capacity)
        {
            if (!float.IsFinite(capacity) || capacity < 0f) throw new ArgumentOutOfRangeException(nameof(capacity));
            float loaded = Math.Min(Available, capacity);
            Available -= loaded;
            return loaded;
        }

        public void Deliver(float amount)
        {
            if (!float.IsFinite(amount) || amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            Delivered += amount;
        }

        public void Update(double fixedDeltaSeconds)
        {
            if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        }

        public void Clear()
        {
            Available = 0f;
            Delivered = 0f;
        }
    }
}
