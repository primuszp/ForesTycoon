using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    /// <summary>
    /// Deterministic individual-tree growth model. Simulation work is performed
    /// monthly rather than every render frame, so cost scales predictably with map size.
    /// </summary>
    sealed partial class ForestSystem : IWorldSystem, IForestCanopy
    {
        internal const double DefaultSecondsPerYear = 30.0;
        private const int MonthsPerYear = 12;
        private const float YearsPerStep = 1f / MonthsPerYear;
        private const int MaximumNeighbours = 4;

        private IForestHabitat habitat;
        internal IForestHabitat Habitat => habitat;
        internal ForesTycoon.Rules.EcologyResponseProgram ResponseRules { get; set; }
        private ForesTycoon.Rules.EcologyResponseProgram Responses => ResponseRules ?? ForesTycoon.Rules.EcologyResponseProgram.Default;
        private ForestStand[] stands = Array.Empty<ForestStand>();
        // Empty tiles that border a seeding stand. Rebuilt every month so that the
        // regeneration pass never has to probe the whole map through the habitat interface.
        private bool[] seedCandidate = Array.Empty<bool>();
        private int[] seedCandidateTiles = Array.Empty<int>();
        private int seedCandidateCount;
        private double secondsPerYear;
        internal IForestEnvironment Environment { get; set; }
        internal void UseEnvironmentTempo(double forestYearSeconds = EcologyTime.SecondsPerForestYear) => secondsPerYear = forestYearSeconds;
        private double accumulatedSeconds;
        internal double SecondsUntilMonth => secondsPerYear/MonthsPerYear-accumulatedSeconds;
        private ulong month;
        private int standCount;
        private int matureCount;
        private float totalBiomass;
        private float totalHealth;

        public ForestSystem(IForestHabitat habitat, double secondsPerYear = DefaultSecondsPerYear, bool deferInitialization = false, ForesTycoon.Rules.EcologyResponseProgram responseRules = null)
        {
            if (secondsPerYear <= 0.0 || !double.IsFinite(secondsPerYear))
                throw new ArgumentOutOfRangeException(nameof(secondsPerYear));
            this.secondsPerYear = secondsPerYear;
            this.habitat = habitat;
            ResponseRules = responseRules;
            if (!deferInitialization) Reset(habitat);
        }

        public int Count => standCount;

        // Explicit initial snapshots for isolated visual fixtures; ordinary worlds still use
        // the seed generator and deterministic replay path.
        internal ForestSystem(IForestHabitat habitat, ReadOnlySpan<ForestStand> initialStands) : this(habitat)
        {
            if (initialStands.Length != stands.Length) throw new ArgumentException("Incorrect stand count.", nameof(initialStands));
            for (int i = 0; i < initialStands.Length; i++)
            {
                ForestStand stand = initialStands[i];
                if (!Enum.IsDefined(stand.Species) || !float.IsFinite(stand.AgeYears) || stand.AgeYears < 0
                    || !float.IsFinite(stand.Biomass) || stand.Biomass < 0 || !float.IsFinite(stand.Health)
                    || stand.Health < 0 || stand.Health > 1)
                    throw new ArgumentException("Invalid initial stand.", nameof(initialStands));
                stands[i] = stand.IsEmpty || !habitat.CanSupportForest(i) ? default : stand;
            }
            InitializeIndividuals();
            RecalculateStatistics();
            Revision++; EditRevision++;
        }
        public ulong Revision { get; private set; }
        public ulong EditRevision { get; private set; }

        public ForestStatistics Statistics => new ForestStatistics(
            standCount, matureCount, totalBiomass, standCount == 0 ? 0f : totalHealth / standCount);

        ForestHydrologyInputs IForestCanopy.HydrologyInputs(int tileId) => HydrologyInputs(tileId);

        bool IForestCanopy.TryGetSpecies(int tileId, out ForestSpecies species)
        {
            bool found = TryGetStand(tileId, out var stand);
            species = stand.Species;
            return found;
        }

        public bool TryGetStand(int tileId, out ForestStand stand)
        {
            stand = IndividualStand(tileId, ForestYear);
            return !stand.IsEmpty;
        }

        /// <summary>
        /// Canopy pressure from the four neighbouring tiles, 0 (open field) to 1 (closed canopy).
        /// Used by natural regeneration; individual growth uses metric crown/root competition.
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
            var result = new WorkResult<ForestryActionResult>();
            SimulationWork.Complete(PlantWork(tileId, species, result)); return result.Value;
        }
        internal IEnumerable<int> PlantWork(int tileId, ForestSpecies species, WorkResult<ForestryActionResult> result)
        {
            result.Value = PlantCore(tileId, species, 0, true, deferRevision: true);
            if (result.Value != ForestryActionResult.Planted) yield break;
            foreach (int unit in FinishPlantingAreaWork(plantations[tileId].AreaId)) yield return unit;
            CompletePlantingRevision(tileId);
        }

        internal ForestryActionResult PlantInArea(int tileId, ForestSpecies species, int areaId)
            => PlantCore(tileId, species, areaId, true);

        private ForestryActionResult PlantCore(int tileId, ForestSpecies species, int areaId, bool deferStatistics, bool deferRevision = false)
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
            CreateIndividuals(tileId, planted, ForestYear, true);
            if (areaId == 0) areaId = AllocatePlantationId();
            plantations[tileId] = new(areaId, species, ForestYear, ForestTreeStore.PlantedTreesPerTile);
            PlantationRevision++;
            stands[tileId] = IndividualStand(tileId, ForestYear);
            if (!deferStatistics) FinishPlantingArea(areaId);
            if (!deferRevision) CompletePlantingRevision(tileId);
            return ForestryActionResult.Planted;
        }

        private void CompletePlantingRevision(int tileId)
        {
            ulong before = Revision;
            Revision++; EditRevision++;
            monthlyPreparation.InvalidateLocalTile(habitat, before, Revision, (month + 1) / 12.0, tileId);
        }

        public ForestryActionResult Harvest(int tileId, out ForestHarvest harvest) =>
            HarvestIndividuals(tileId, out harvest);

        internal static float TimberCubicMetres(ForestStand stand) => stand.IsEmpty?0:stand.Biomass*100;
        internal float ExtractTimber(int tileId, float requested)
        {
            if (!float.IsFinite(requested) || requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
            return ExtractIndividualTimber(tileId, requested);
        }

        /// <summary>Removes living trees, stumps and depots when terrain becomes unavailable.</summary>
        public void RefreshHabitat() => SimulationWork.Complete(RefreshHabitatWork());

        internal IEnumerable<int> RefreshHabitatWork()
        {
            bool changed = false;
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                yield return 1;
                if (habitat.CanSupportForest(tileId) || !IndividualTrees.TryGet(tileId, out _)) continue;
                IndividualTrees.RemoveTile(tileId);
                MarkResourceArea(tileId);
                stands[tileId] = default;
                changed = true;
            }
            if (!changed) { foreach (int unit in RefreshEnvironmentRatesWork()) yield return unit; yield break; }
            foreach (int unit in RefreshChangedResourceRatesWork(ForestYear, currentConditions: true)) yield return unit;
            Revision++; EditRevision++;
        }

        public void Reset(IForestHabitat newHabitat) => SimulationWork.Complete(ResetWork(newHabitat));

        internal IEnumerable<int> ResetWork(IForestHabitat newHabitat)
        {
            habitat = newHabitat ?? throw new ArgumentNullException(nameof(newHabitat));
            if (stands.Length != habitat.TileCount)
            {
                stands = new ForestStand[habitat.TileCount];
                seedCandidate = new bool[habitat.TileCount];
                seedCandidateTiles = new int[habitat.TileCount];
            }
            else
            {
                Array.Clear(stands);
                Array.Clear(seedCandidate);
            }

            seedCandidateCount = 0;
            accumulatedSeconds = 0.0;
            month = 0;
            foreach (int unit in GenerateInitialForestWork()) yield return unit;
            foreach (int unit in InitializeIndividualsWork()) yield return unit;
            foreach (int unit in RecalculateStatisticsWork()) yield return unit;
            Revision++; EditRevision++;
        }

        public void Update(double fixedDeltaSeconds) => SimulationWork.Complete(UpdateWork(fixedDeltaSeconds));

        public IEnumerable<int> UpdateWork(double fixedDeltaSeconds)
        {
            if (fixedDeltaSeconds < 0.0 || !double.IsFinite(fixedDeltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));

            accumulatedSeconds += fixedDeltaSeconds;
            double secondsPerMonth = secondsPerYear / MonthsPerYear;
            while (accumulatedSeconds >= secondsPerMonth)
            {
                accumulatedSeconds -= secondsPerMonth;
                month++;
                foreach (int work in StepIndividualMonthWork()) yield return work;
            }
        }

        public void Clear()
        {
            monthlyPreparation.Clear();
            currentYearGrowth = lastYearGrowth = 0;
            IndividualTrees.Clear();
            ClearPlantations(); competition.Clear(); changedResourceTiles.Clear();
            Array.Clear(stands);
            Array.Clear(seedCandidate);
            seedCandidateCount = 0;
            accumulatedSeconds = 0.0;
            month = 0;
            ClearStatistics();
            Revision++; EditRevision++;
        }

        /// <summary>
        /// A world-editor map's forest: painted tiles get a natural mature stand of the chosen species, or none.
        /// Generated maps rebuild individuals from stands; captured scenes preserve every unpainted patch.
        /// </summary>
        internal void PaintInitialForest(IReadOnlyDictionary<int, ForestSpecies> paint, bool preserveUnpainted = false) => SimulationWork.Complete(PaintInitialForestWork(paint, preserveUnpainted));

        internal IEnumerable<int> PaintInitialForestWork(IReadOnlyDictionary<int, ForestSpecies> paint, bool preserveUnpainted = false)
        {
            if (paint == null || paint.Count == 0) yield break;
            if (preserveUnpainted) {
                var tiles = new List<int>(paint.Keys);
                foreach (int unit in ClearTerrainTilesWork(tiles.ToArray())) yield return unit;
            }
            else for (int id = 0; id < stands.Length; id++) { stands[id] = IndividualStand(id, ForestYear); yield return 1; }
            foreach (var (tileId, species) in paint)
            {
                yield return 1;
                if ((uint)tileId >= (uint)stands.Length) continue;
                if (species == ForestSpecies.None || !habitat.CanSupportForest(tileId)) { stands[tileId] = default; continue; }
                float moisture = habitat.GetMoisture(tileId), elevation = habitat.GetNormalizedElevation(tileId);
                ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
                float age = 4f + UnitFloat(Hash(habitat.Seed, tileId, 1)) * profile.MatureAgeYears * 2.2f;
                float health = Math.Clamp(0.72f + Suitability(species, tileId) * 0.28f, 0f, 1f);
                stands[tileId] = new ForestStand(species, age, InitialBiomass(profile, age, health), health);
                if (preserveUnpainted) IndividualTrees.Create(tileId, stands[tileId], ForestYear, habitat.Seed);
            }
            if (preserveUnpainted) {
                monthlyPreparation.Clear();
                foreach (int unit in competition.SnapshotWork(habitat, IndividualTrees, ForestYear)) yield return unit;
                foreach (int id in paint.Keys)
                    if (IndividualTrees.TryGet(id, out var patch)) {
                        foreach (int unit in UpdateIndividualRatesWork(id, patch, ForestYear)) yield return unit;
                        stands[id] = IndividualStand(id, ForestYear);
                    }
            }
            else foreach (int unit in InitializeIndividualsWork()) yield return unit;
            foreach (int unit in RecalculateStatisticsWork()) yield return unit;
            Revision++; EditRevision++;
        }



        private IEnumerable<int> GenerateInitialForestWork()
        {
            if(habitat.ForestPattern != ForestPattern.Natural && habitat.TileGrid.Columns > 0)
            {
                foreach (int unit in LargeForestGenerator.CreateWork(habitat,habitat.TileGrid.Columns,habitat.TileGrid.Rows,habitat.ForestPattern, stands)) yield return unit;
                yield break;
            }
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                yield return 1;
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
                // which collapsed every mixture below to its first branch â€” the whole map
                // came out as a single species.
                ForestSpecies species = SelectSpecies(moisture, elevation, Hash(habitat.Seed, tileId, 2));
                ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
                float age = 4f + UnitFloat(Hash(habitat.Seed, tileId, 1)) * profile.MatureAgeYears * 2.2f;
                float health = Math.Clamp(0.72f + Suitability(species, tileId) * 0.28f, 0f, 1f);
                float biomass = InitialBiomass(profile, age, health);
                stands[tileId] = new ForestStand(species, age, biomass, health);
            }
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
            if (suitability < 0.18f || (Environment?.GrowthFactor(tileId,seedSpecies)??1)<0.3) return default;
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

        private void RecalculateStatistics() => SimulationWork.Complete(RecalculateStatisticsWork());

        private IEnumerable<int> RecalculateStatisticsWork()
        {
            ClearStatistics();
            for (int i = 0; i < stands.Length; i++)
            {
                yield return 1;
                ForestStand stand = stands[i];
                if (stand.IsEmpty) continue;
                AddToStatistics(stand);
            }
        }

        private static ForestSpecies SelectSpecies(float moisture, float elevation, uint random)
        {
            // Montane and wet ground: spruce and fir belt, with beech and maple mixed in, larch on the
            // highest ground and birch on the edges.
            if (elevation > 0.68f || moisture > 0.78f)
                return (random % 8u) switch
                {
                    0u => ForestSpecies.Birch,
                    1u => ForestSpecies.Beech,
                    2u => ForestSpecies.Fir,
                    3u => ForestSpecies.Maple,
                    4u => elevation > 0.8f ? ForestSpecies.Larch : ForestSpecies.Spruce,
                    _ => ForestSpecies.Spruce
                };

            // Dry lowland: oak country (pedunculate, sessile and Turkey oak), with birch and pine on the
            // poorest ground, hawthorn scrub, and a few spruce, which survive here but never thrive —
            // Fitness keeps them small and sickly.
            if (moisture < 0.52f)
                return (random % 10u) switch
                {
                    0u => ForestSpecies.Birch,
                    1u => ForestSpecies.Spruce,
                    2u => ForestSpecies.Pine,
                    3u => ForestSpecies.TurkeyOak,
                    4u => ForestSpecies.SessileOak,
                    5u => ForestSpecies.Hawthorn,
                    _ => ForestSpecies.Oak
                };

            // Fresh mid-slope soils: mixed forest. Beech leads, with maple and ash on the richer soil,
            // hazel in the understorey, and a share of spruce keeps conifers present outside the montane
            // belt, the way managed mixed stands are.
            return (random % 10u) switch
            {
                0u => ForestSpecies.Birch,
                1u => ForestSpecies.Oak,
                2u => ForestSpecies.Spruce,
                3u => ForestSpecies.Maple,
                4u => ForestSpecies.Ash,
                5u => ForestSpecies.Hazel,
                _ => ForestSpecies.Beech
            };
        }

        private float Suitability(ForestSpecies species, int tileId)
        {
            var profile = ForestSpeciesProfile.For(species);
            return (float)Responses.Fitness(habitat.GetMoisture(tileId), habitat.GetNormalizedElevation(tileId),
                habitat.GetSoilProperties(tileId).Fertility, profile.PreferredElevation, profile.ElevationTolerance);
        }

        private static float InitialBiomass(ForestSpeciesProfile profile, float age, float health)
        {
            float normalizedAge = Math.Clamp(age / profile.MatureAgeYears, 0f, 1f);
            return profile.MaximumBiomass * normalizedAge * normalizedAge * (0.45f + health * 0.55f);
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
