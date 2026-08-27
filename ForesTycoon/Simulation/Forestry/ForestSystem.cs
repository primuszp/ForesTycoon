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
        private const int MaximumNeighbours = 4;

        private IForestHabitat habitat;
        private ForestStand[] stands = Array.Empty<ForestStand>();
        private ForestStand[] nextStands = Array.Empty<ForestStand>();
        // Empty tiles that border a seeding stand. Rebuilt every month so that the
        // regeneration pass never has to probe the whole map through the habitat interface.
        private bool[] seedCandidate = Array.Empty<bool>();
        private int[] seedCandidateTiles = Array.Empty<int>();
        private int seedCandidateCount;
        private readonly double secondsPerYear;
        private double accumulatedSeconds;
        private ulong month;
        private int standCount;
        private int matureCount;
        private float totalBiomass;
        private float totalHealth;

        public ForestSystem(IForestHabitat habitat, double secondsPerYear = DefaultSecondsPerYear)
        {
            if (secondsPerYear <= 0.0 || !double.IsFinite(secondsPerYear))
                throw new ArgumentOutOfRangeException(nameof(secondsPerYear));
            this.secondsPerYear = secondsPerYear;
            Reset(habitat);
        }

        public int Count => standCount;

        public ForestStatistics Statistics => new ForestStatistics(
            standCount, matureCount, totalBiomass, standCount == 0 ? 0f : totalHealth / standCount);

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

        /// <summary>
        /// Canopy pressure from the four neighbouring tiles, 0 (open field) to 1 (closed canopy).
        /// Drives both suppressed growth and self-thinning mortality.
        /// </summary>
        public float GetCrowding(int tileId)
        {
            if ((uint)tileId >= (uint)stands.Length) return 0f;

            Span<int> neighbours = stackalloc int[MaximumNeighbours];
            int neighbourCount = habitat.GetAdjacentTileIds(tileId, neighbours);
            return Crowding(neighbours, neighbourCount);
        }

        public ForestryActionResult Plant(int tileId, ForestSpecies species)
        {
            if (!Enum.IsDefined(species) || species == ForestSpecies.None)
                throw new ArgumentOutOfRangeException(nameof(species));
            if ((uint)tileId >= (uint)stands.Length) return ForestryActionResult.InvalidTile;
            if (!stands[tileId].IsEmpty) return ForestryActionResult.TileOccupied;
            if (!habitat.CanSupportForest(tileId)) return ForestryActionResult.UnsuitableTerrain;

            ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
            float suitability = Suitability(species, tileId);
            ForestStand planted = new ForestStand(
                species,
                YearsPerStep,
                profile.MaximumBiomass * 0.015f,
                0.50f + suitability * 0.40f);
            stands[tileId] = planted;
            AddToStatistics(planted);
            return ForestryActionResult.Planted;
        }

        public ForestryActionResult Harvest(int tileId, out ForestHarvest harvest)
        {
            if ((uint)tileId >= (uint)stands.Length)
            {
                harvest = default;
                return ForestryActionResult.InvalidTile;
            }
            if (stands[tileId].IsEmpty)
            {
                harvest = default;
                return ForestryActionResult.NoForest;
            }

            ForestStand stand = stands[tileId];
            harvest = new ForestHarvest(stand.Species, stand.AgeYears, TimberYield(stand));
            stands[tileId] = default;
            RemoveFromStatistics(stand);
            return ForestryActionResult.Harvested;
        }

        /// <summary>
        /// Recoverable roundwood in tonnes. One biomass unit is one hundred tonnes of stem wood,
        /// scaled by how valuable and dense the species' timber is.
        /// </summary>
        public static float TimberYield(ForestStand stand)
        {
            if (stand.IsEmpty) return 0f;
            return stand.Biomass * 100f * ForestSpeciesProfile.For(stand.Species).WoodDensity;
        }

        /// <summary>Applies infrequent terrain/road changes without adding a full-map scan to every tick.</summary>
        public void RefreshHabitat()
        {
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                ForestStand stand = stands[tileId];
                if (stand.IsEmpty || habitat.CanSupportForest(tileId)) continue;
                stands[tileId] = default;
                RemoveFromStatistics(stand);
            }
        }

        public void Reset(IForestHabitat newHabitat)
        {
            habitat = newHabitat ?? throw new ArgumentNullException(nameof(newHabitat));
            if (stands.Length != habitat.TileCount)
            {
                stands = new ForestStand[habitat.TileCount];
                nextStands = new ForestStand[habitat.TileCount];
                seedCandidate = new bool[habitat.TileCount];
                seedCandidateTiles = new int[habitat.TileCount];
            }
            else
            {
                Array.Clear(stands);
                Array.Clear(nextStands);
                Array.Clear(seedCandidate);
            }

            seedCandidateCount = 0;
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
            Array.Clear(seedCandidate);
            seedCandidateCount = 0;
            accumulatedSeconds = 0.0;
            month = 0;
            ClearStatistics();
        }

        private void GenerateInitialForest()
        {
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                if (!habitat.CanSupportForest(tileId)) continue;

                float moisture = habitat.GetMoisture(tileId);
                float elevation = habitat.GetNormalizedElevation(tileId);
                // Natural distribution stays selective; manual planting may use harsher land.
                if (moisture < 0.35f || moisture > 0.95f || elevation <= 0.17f || elevation >= 0.83f)
                    continue;
                uint random = Hash(habitat.Seed, tileId, 0);
                int density = moisture >= 0.70f ? 3 : 5;
                if (random % (uint)density != 0) continue;

                // Species must be drawn from an independent hash. Reusing the value the
                // density filter just tested keeps only tiles where random % density == 0,
                // which collapsed every mixture below to its first branch — the whole map
                // came out as a single species.
                ForestSpecies species = SelectSpecies(moisture, elevation, Hash(habitat.Seed, tileId, 2));
                ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
                float age = 4f + UnitFloat(Hash(habitat.Seed, tileId, 1)) * profile.MatureAgeYears * 2.2f;
                float health = Math.Clamp(0.72f + Fitness(species, moisture, elevation) * 0.28f, 0f, 1f);
                float biomass = InitialBiomass(profile, age, health);
                stands[tileId] = new ForestStand(species, age, biomass, health);
            }
        }

        private void StepMonth()
        {
            month++;
            Array.Clear(nextStands);
            ClearSeedCandidates();
            ClearStatistics();

            // Pass one: grow the existing stands and record which empty tiles they can seed.
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                ForestStand stand = stands[tileId];
                if (stand.IsEmpty) continue;

                ForestStand grown = GrowOrDie(tileId, stand);
                nextStands[tileId] = grown;
                if (!grown.IsEmpty) AddToStatistics(grown);
                if (IsSeedSource(stand)) MarkSeedCandidates(tileId);
            }

            // Pass two: only tiles that actually border a seeding stand can regenerate,
            // so an empty map costs no habitat queries at all.
            for (int i = 0; i < seedCandidateCount; i++)
            {
                int tileId = seedCandidateTiles[i];
                if (!stands[tileId].IsEmpty) continue;

                ForestStand seedling = TryRegenerate(tileId);
                if (seedling.IsEmpty) continue;
                nextStands[tileId] = seedling;
                AddToStatistics(seedling);
            }

            (stands, nextStands) = (nextStands, stands);
        }

        private void ClearSeedCandidates()
        {
            for (int i = 0; i < seedCandidateCount; i++) seedCandidate[seedCandidateTiles[i]] = false;
            seedCandidateCount = 0;
        }

        private void MarkSeedCandidates(int tileId)
        {
            Span<int> neighbours = stackalloc int[MaximumNeighbours];
            int neighbourCount = habitat.GetAdjacentTileIds(tileId, neighbours);
            for (int i = 0; i < neighbourCount; i++)
            {
                int neighbourId = neighbours[i];
                if (seedCandidate[neighbourId] || !stands[neighbourId].IsEmpty) continue;
                seedCandidate[neighbourId] = true;
                seedCandidateTiles[seedCandidateCount++] = neighbourId;
            }
        }

        private static bool IsSeedSource(ForestStand stand) =>
            !stand.IsEmpty && stand.Maturity >= 1f && stand.Health >= 0.45f;

        private ForestStand GrowOrDie(int tileId, ForestStand stand)
        {
            if (!habitat.CanSupportForest(tileId)) return default;

            ForestSpeciesProfile profile = ForestSpeciesProfile.For(stand.Species);
            float suitability = Suitability(stand.Species, tileId);
            float crowding = GetCrowding(tileId);
            // Light-demanding species stall under a closed canopy; shade bearers barely notice.
            float shadePressure = crowding * (1f - profile.ShadeTolerance);
            float lightFactor = Math.Clamp(1f - shadePressure, 0.05f, 1f);

            float targetHealth = (0.25f + suitability * 0.75f) * (0.55f + lightFactor * 0.45f);
            float health = MoveTowards(stand.Health, Math.Clamp(targetHealth, 0f, 1f), 0.035f);
            float age = stand.AgeYears + YearsPerStep;
            float remainingCapacity = Math.Max(0f, 1f - stand.Biomass / profile.MaximumBiomass);
            float growth = profile.MaximumBiomass * profile.AnnualGrowthRate
                * remainingCapacity * health * lightFactor * YearsPerStep;
            float biomass = Math.Clamp(stand.Biomass + growth, 0f, profile.MaximumBiomass);

            float agePressure = Math.Max(0f, (age - profile.MaximumAgeYears) / (profile.MaximumAgeYears * 0.25f));
            // Suppressed trees die out of the stand: this is what makes thinning worth doing.
            float mortalityChance = (1f - health) * 0.004f + agePressure * 0.012f + shadePressure * 0.006f;
            if (UnitFloat(Hash(habitat.Seed, tileId, month)) < mortalityChance)
                return default;

            return new ForestStand(stand.Species, age, biomass, health);
        }

        private ForestStand TryRegenerate(int tileId)
        {
            if (!habitat.CanSupportForest(tileId)) return default;

            Span<int> neighbours = stackalloc int[MaximumNeighbours];
            int neighbourCount = habitat.GetAdjacentTileIds(tileId, neighbours);
            float crowding = Crowding(neighbours, neighbourCount);

            // The best-adapted seed source wins the gap rather than whichever neighbour
            // happens to sit at the lowest tile index.
            ForestSpecies seedSpecies = ForestSpecies.None;
            float bestScore = 0f;
            int matureNeighbours = 0;
            for (int i = 0; i < neighbourCount; i++)
            {
                ForestStand neighbour = stands[neighbours[i]];
                if (!IsSeedSource(neighbour)) continue;
                matureNeighbours++;

                ForestSpeciesProfile candidate = ForestSpeciesProfile.For(neighbour.Species);
                float shadeFit = 1f - crowding * (1f - candidate.ShadeTolerance);
                float score = Suitability(neighbour.Species, tileId) * neighbour.Health * Math.Max(0f, shadeFit);
                if (score <= bestScore) continue;
                bestScore = score;
                seedSpecies = neighbour.Species;
            }

            if (matureNeighbours == 0 || seedSpecies == ForestSpecies.None) return default;
            float annualChance = Math.Min(0.30f, matureNeighbours * 0.055f);
            float monthlyChance = annualChance / MonthsPerYear;
            if (UnitFloat(Hash(habitat.Seed, tileId, month)) >= monthlyChance) return default;

            float suitability = Suitability(seedSpecies, tileId);
            if (suitability < 0.18f) return default;
            ForestSpeciesProfile profile = ForestSpeciesProfile.For(seedSpecies);
            return new ForestStand(seedSpecies, YearsPerStep, profile.MaximumBiomass * 0.015f, 0.55f + suitability * 0.35f);
        }

        private float Crowding(ReadOnlySpan<int> neighbours, int neighbourCount)
        {
            if (neighbourCount == 0) return 0f;

            float neighbourBiomass = 0f;
            for (int i = 0; i < neighbourCount; i++) neighbourBiomass += stands[neighbours[i]].Biomass;
            return Math.Clamp(
                neighbourBiomass / (neighbourCount * ForestSpeciesProfile.ReferenceCanopyBiomass), 0f, 1f);
        }

        private void ClearStatistics()
        {
            standCount = 0;
            matureCount = 0;
            totalBiomass = 0f;
            totalHealth = 0f;
        }

        private void AddToStatistics(ForestStand stand)
        {
            standCount++;
            if (stand.Maturity >= 1f) matureCount++;
            totalBiomass += stand.Biomass;
            totalHealth += stand.Health;
        }

        private void RemoveFromStatistics(ForestStand stand)
        {
            standCount--;
            if (stand.Maturity >= 1f) matureCount--;
            totalBiomass = Math.Max(0f, totalBiomass - stand.Biomass);
            totalHealth = Math.Max(0f, totalHealth - stand.Health);
        }

        private void RecalculateStatistics()
        {
            ClearStatistics();
            for (int i = 0; i < stands.Length; i++)
            {
                ForestStand stand = stands[i];
                if (stand.IsEmpty) continue;
                AddToStatistics(stand);
            }
        }

        private static ForestSpecies SelectSpecies(float moisture, float elevation, uint random)
        {
            // Montane and wet ground: spruce belt, with beech mixed in and birch on the edges.
            if (elevation > 0.68f || moisture > 0.78f)
                return (random % 5u) switch
                {
                    0u => ForestSpecies.Birch,
                    1u => ForestSpecies.Beech,
                    _ => ForestSpecies.Spruce
                };

            // Dry lowland: oak country, with birch on the poorest ground and a minority of
            // spruce, which survives here but never thrives — Fitness keeps it small and sickly.
            if (moisture < 0.52f)
                return (random % 5u) switch
                {
                    0u => ForestSpecies.Birch,
                    1u => ForestSpecies.Spruce,
                    _ => ForestSpecies.Oak
                };

            // Fresh mid-slope soils: mixed forest. Beech leads, but a share of spruce keeps
            // conifers present outside the montane belt, the way managed mixed stands are.
            return (random % 5u) switch
            {
                0u => ForestSpecies.Birch,
                1u => ForestSpecies.Oak,
                2u => ForestSpecies.Spruce,
                _ => ForestSpecies.Beech
            };
        }

        private float Suitability(ForestSpecies species, int tileId) =>
            Fitness(species, habitat.GetMoisture(tileId), habitat.GetNormalizedElevation(tileId));

        /// <summary>
        /// How well a species matches the site. Moisture and elevation are combined
        /// multiplicatively so that a stand on the right slope but the wrong soil still suffers.
        /// </summary>
        internal static float Fitness(ForestSpecies species, float moisture, float elevation)
        {
            ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
            if (profile.MoistureTolerance <= 0f || profile.ElevationTolerance <= 0f) return 0f;

            float moistureFit = Math.Clamp(
                1f - Math.Abs(moisture - profile.PreferredMoisture) / profile.MoistureTolerance, 0f, 1f);
            float elevationFit = Math.Clamp(
                1f - Math.Abs(elevation - profile.PreferredElevation) / profile.ElevationTolerance, 0f, 1f);
            // Elevation is the weaker of the two signals; soil moisture decides most sites.
            return moistureFit * (0.45f + elevationFit * 0.55f);
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
