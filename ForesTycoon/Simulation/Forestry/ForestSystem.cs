using System;

namespace ForesTycoon
{
    /// <summary>
    /// Deterministic, allocation-free forest growth model. Simulation work is performed
    /// monthly rather than every render frame, so cost scales predictably with map size.
    /// </summary>
    sealed class ForestSystem : IWorldSystem
    {
        internal const double DefaultSecondsPerYear = 30.0;
        private const int MonthsPerYear = 12;
        private const float YearsPerStep = 1f / MonthsPerYear;

        private IForestHabitat habitat;
        private ForestStand[] stands = Array.Empty<ForestStand>();
        private ForestStand[] nextStands = Array.Empty<ForestStand>();
        private readonly double secondsPerYear;
        private double accumulatedSeconds;
        private ulong month;
        private ForestStatistics statistics;

        public ForestSystem(IForestHabitat habitat, double secondsPerYear = DefaultSecondsPerYear)
        {
            if (secondsPerYear <= 0.0 || !double.IsFinite(secondsPerYear))
                throw new ArgumentOutOfRangeException(nameof(secondsPerYear));
            this.secondsPerYear = secondsPerYear;
            Reset(habitat);
        }

        public int Count => statistics.StandCount;
        public ForestStatistics Statistics => statistics;

        public bool TryGetStand(int tileId, out ForestStand stand)
        {
            if ((uint)tileId >= (uint)stands.Length || stands[tileId].IsEmpty)
            {
                stand = default;
                return false;
            }

            stand = stands[tileId];
            return true;
        }

        /// <summary>Applies infrequent terrain/road changes without adding a full-map scan to every tick.</summary>
        public void RefreshHabitat()
        {
            bool changed = false;
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                if (stands[tileId].IsEmpty || habitat.CanSupportForest(tileId)) continue;
                stands[tileId] = default;
                changed = true;
            }
            if (changed) RecalculateStatistics();
        }

        public void Reset(IForestHabitat newHabitat)
        {
            habitat = newHabitat ?? throw new ArgumentNullException(nameof(newHabitat));
            if (stands.Length != habitat.TileCount)
            {
                stands = new ForestStand[habitat.TileCount];
                nextStands = new ForestStand[habitat.TileCount];
            }
            else
            {
                Array.Clear(stands);
                Array.Clear(nextStands);
            }

            accumulatedSeconds = 0.0;
            month = 0;
            GenerateInitialForest();
            RecalculateStatistics();
        }

        public void Update(double fixedDeltaSeconds)
        {
            if (fixedDeltaSeconds < 0.0 || !double.IsFinite(fixedDeltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));

            accumulatedSeconds += fixedDeltaSeconds;
            double secondsPerMonth = secondsPerYear / MonthsPerYear;
            while (accumulatedSeconds >= secondsPerMonth)
            {
                accumulatedSeconds -= secondsPerMonth;
                StepMonth();
            }
        }

        public void Clear()
        {
            Array.Clear(stands);
            Array.Clear(nextStands);
            accumulatedSeconds = 0.0;
            month = 0;
            statistics = default;
        }

        private void GenerateInitialForest()
        {
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                if (!habitat.CanSupportForest(tileId)) continue;

                float moisture = habitat.GetMoisture(tileId);
                uint random = Hash(habitat.Seed, tileId, 0);
                int density = moisture >= 0.70f ? 3 : 5;
                if (random % (uint)density != 0) continue;

                ForestSpecies species = SelectSpecies(moisture, habitat.GetNormalizedElevation(tileId), random);
                ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
                float age = 4f + UnitFloat(Hash(habitat.Seed, tileId, 1)) * profile.MatureAgeYears * 2.2f;
                float health = Math.Clamp(0.72f + Suitability(species, moisture) * 0.28f, 0f, 1f);
                float biomass = InitialBiomass(profile, age, health);
                stands[tileId] = new ForestStand(species, age, biomass, health);
            }
        }

        private void StepMonth()
        {
            month++;
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                ForestStand stand = stands[tileId];
                nextStands[tileId] = stand.IsEmpty
                    ? TryRegenerate(tileId)
                    : GrowOrDie(tileId, stand);
            }

            (stands, nextStands) = (nextStands, stands);
            RecalculateStatistics();
        }

        private ForestStand GrowOrDie(int tileId, ForestStand stand)
        {
            if (!habitat.CanSupportForest(tileId)) return default;

            ForestSpeciesProfile profile = ForestSpeciesProfile.For(stand.Species);
            float suitability = Suitability(stand.Species, habitat.GetMoisture(tileId));
            float targetHealth = 0.25f + suitability * 0.75f;
            float health = MoveTowards(stand.Health, targetHealth, 0.035f);
            float age = stand.AgeYears + YearsPerStep;
            float remainingCapacity = Math.Max(0f, 1f - stand.Biomass / profile.MaximumBiomass);
            float growth = profile.MaximumBiomass * profile.AnnualGrowthRate
                * remainingCapacity * health * YearsPerStep;
            float biomass = Math.Clamp(stand.Biomass + growth, 0f, profile.MaximumBiomass);

            float agePressure = Math.Max(0f, (age - profile.MaximumAgeYears) / (profile.MaximumAgeYears * 0.25f));
            float mortalityChance = (1f - health) * 0.004f + agePressure * 0.012f;
            if (UnitFloat(Hash(habitat.Seed, tileId, month)) < mortalityChance)
                return default;

            return new ForestStand(stand.Species, age, biomass, health);
        }

        private ForestStand TryRegenerate(int tileId)
        {
            if (!habitat.CanSupportForest(tileId)) return default;

            Span<int> neighbours = stackalloc int[4];
            int neighbourCount = habitat.GetAdjacentTileIds(tileId, neighbours);
            ForestSpecies seedSpecies = ForestSpecies.None;
            int matureNeighbours = 0;
            for (int i = 0; i < neighbourCount; i++)
            {
                ForestStand neighbour = stands[neighbours[i]];
                if (neighbour.IsEmpty || neighbour.Maturity < 1f || neighbour.Health < 0.45f) continue;
                matureNeighbours++;
                if (seedSpecies == ForestSpecies.None) seedSpecies = neighbour.Species;
            }

            if (matureNeighbours == 0) return default;
            float annualChance = Math.Min(0.30f, matureNeighbours * 0.055f);
            float monthlyChance = annualChance / MonthsPerYear;
            if (UnitFloat(Hash(habitat.Seed, tileId, month)) >= monthlyChance) return default;

            float suitability = Suitability(seedSpecies, habitat.GetMoisture(tileId));
            if (suitability < 0.18f) return default;
            ForestSpeciesProfile profile = ForestSpeciesProfile.For(seedSpecies);
            return new ForestStand(seedSpecies, YearsPerStep, profile.MaximumBiomass * 0.015f, 0.55f + suitability * 0.35f);
        }

        private void RecalculateStatistics()
        {
            int count = 0;
            int mature = 0;
            float biomass = 0f;
            float health = 0f;
            for (int i = 0; i < stands.Length; i++)
            {
                ForestStand stand = stands[i];
                if (stand.IsEmpty) continue;
                count++;
                if (stand.Maturity >= 1f) mature++;
                biomass += stand.Biomass;
                health += stand.Health;
            }
            statistics = new ForestStatistics(count, mature, biomass, count == 0 ? 0f : health / count);
        }

        private static ForestSpecies SelectSpecies(float moisture, float elevation, uint random)
        {
            if (elevation > 0.68f || moisture > 0.78f)
                return (random & 3u) == 0u ? ForestSpecies.Birch : ForestSpecies.Spruce;
            if (moisture < 0.52f)
                return (random & 3u) == 0u ? ForestSpecies.Birch : ForestSpecies.Pine;
            return (random & 1u) == 0u ? ForestSpecies.Pine : ForestSpecies.Birch;
        }

        private static float Suitability(ForestSpecies species, float moisture)
        {
            ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
            return Math.Clamp(1f - Math.Abs(moisture - profile.PreferredMoisture) / profile.MoistureTolerance, 0f, 1f);
        }

        private static float InitialBiomass(ForestSpeciesProfile profile, float age, float health)
        {
            float normalizedAge = Math.Clamp(age / profile.MatureAgeYears, 0f, 1f);
            return profile.MaximumBiomass * normalizedAge * normalizedAge * (0.45f + health * 0.55f);
        }

        private static float MoveTowards(float current, float target, float maximumDelta)
        {
            if (Math.Abs(target - current) <= maximumDelta) return target;
            return current + Math.Sign(target - current) * maximumDelta;
        }

        private static float UnitFloat(uint value) => (value >> 8) * (1f / 16777216f);

        private static uint Hash(int seed, int tileId, ulong step)
        {
            uint value = unchecked((uint)seed) ^ unchecked((uint)tileId * 0x9E3779B9u);
            value ^= unchecked((uint)step * 0x85EBCA6Bu);
            value ^= unchecked((uint)(step >> 32) * 0xC2B2AE35u);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }
    }
}
