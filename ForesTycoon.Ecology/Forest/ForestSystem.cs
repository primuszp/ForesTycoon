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
        internal ForesTycoon.Rules.GameplayProgram Gameplay => Responses.Gameplay;
        internal const double DefaultSecondsPerYear = 30.0;
        private const int MonthsPerYear = 12;
        private const float YearsPerStep = 1f / MonthsPerYear;
        private const int MaximumNeighbours = 4;

        private IForestHabitat habitat;
        internal IForestHabitat Habitat => habitat;
        internal ForesTycoon.Rules.EcologyResponseProgram ResponseRules { get; set; }
        private ForesTycoon.Rules.EcologyResponseProgram Responses => ResponseRules ?? ForesTycoon.Rules.EcologyResponseProgram.Default;
        private ForestStand[] stands = Array.Empty<ForestStand>();
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
        /// Used by natural regeneration; individual growth uses metric crown competition.
        /// </summary>
        public float GetCrowding(int tileId)
        {
            if ((uint)tileId >= (uint)stands.Length) return 0f;

            Span<int> neighbours = stackalloc int[MaximumNeighbours];
            int neighbourCount = habitat.GetAdjacentTileIds(tileId, neighbours);
            return Crowding(neighbours, neighbourCount, habitat.GetMoisture(tileId));
        }

        public ForestryActionResult Plant(int tileId, ForestSpecies species)
        {
            var result = new WorkResult<ForestryActionResult>();
            SimulationWork.Complete(PlantWork(tileId, species, result)); return result.Value;
        }
        internal IEnumerable<int> PlantWork(int tileId, ForestSpecies species, WorkResult<ForestryActionResult> result)
        {
            foreach (int unit in PlantCoreWork(tileId, species, 0, true, result, deferRevision: true)) yield return unit;
            if (result.Value != ForestryActionResult.Planted) yield break;
            foreach (int unit in FinishPlantingAreaWork(plantations[tileId].AreaId)) yield return unit;
            CompletePlantingRevision(tileId);
        }

        // Explicit synchronous adapter for diagnostics. Interactive commands use PlantInAreaWork.
        internal ForestryActionResult PlantInArea(int tileId, ForestSpecies species, int areaId)
        {
            var result = new WorkResult<ForestryActionResult>();
            SimulationWork.Complete(PlantInAreaWork(tileId, species, areaId, result)); return result.Value;
        }
        internal IEnumerable<int> PlantInAreaWork(int tileId, ForestSpecies species, int areaId, WorkResult<ForestryActionResult> result)
            => PlantCoreWork(tileId, species, areaId, true, result);

        private IEnumerable<int> PlantCoreWork(int tileId, ForestSpecies species, int areaId, bool deferStatistics,
            WorkResult<ForestryActionResult> result, bool deferRevision = false)
        {
            if (!Enum.IsDefined(species) || species == ForestSpecies.None)
                throw new ArgumentOutOfRangeException(nameof(species));
            if ((uint)tileId >= (uint)stands.Length) { result.Value = ForestryActionResult.InvalidTile; yield break; }
            if (!stands[tileId].IsEmpty) { result.Value = ForestryActionResult.TileOccupied; yield break; }
            if (!habitat.CanSupportForest(tileId)) { result.Value = ForestryActionResult.UnsuitableTerrain; yield break; }

            ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
            float suitability = Suitability(species, tileId);
            if (Gameplay.Evaluate("forest.plantAllow", suitability, (int)species) < .5) { result.Value = ForestryActionResult.UnsuitableTerrain; yield break; }
            ForestStand planted = new ForestStand(
                species,
                YearsPerStep,
                profile.MaximumBiomass * 0.015f,
                (float)Gameplay.Evaluate("forest.plantHealth", suitability, (int)species));
            stands[tileId] = planted;
            foreach (int unit in IndividualTrees.CreateWork(tileId, planted, ForestYear, habitat.Seed, planted: true, responses: Responses)) yield return unit;
            if (areaId == 0) areaId = AllocatePlantationId();
            plantations[tileId] = new(areaId, species, ForestYear, ForestTreeStore.PlantedTreesPerTile);
            PlantationRevision++;
            stands[tileId] = IndividualStand(tileId, ForestYear);
            if (!deferStatistics) foreach (int unit in FinishPlantingAreaWork(areaId)) yield return unit;
            if (!deferRevision) CompletePlantingRevision(tileId);
            result.Value = ForestryActionResult.Planted;
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
            }
            else
            {
                Array.Clear(stands);
            }

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
            ClearPlantations(); competition.Clear(); changedResourceTiles.Clear(); changedResourceSources.Clear();
            Array.Clear(stands);
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
                float age = (float)Gameplay.Evaluate("forest.initialAge", 0, profile.MatureAgeYears, UnitFloat(Hash(habitat.Seed, tileId, 1)));
                float health = (float)Gameplay.Evaluate("forest.initialHealth", 0, UnitFloat(Hash(habitat.Seed, tileId, 3)), Suitability(species, tileId));
                stands[tileId] = new ForestStand(species, age, InitialBiomass(profile, age, health), health);
                if (preserveUnpainted) foreach (int unit in IndividualTrees.CreateWork(tileId, stands[tileId], ForestYear, habitat.Seed, responses: Responses)) yield return unit;
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
                foreach (int unit in LargeForestGenerator.CreateWork(habitat,habitat.TileGrid.Columns,habitat.TileGrid.Rows,habitat.ForestPattern, stands, Gameplay)) yield return unit;
                yield break;
            }
            for (int tileId = 0; tileId < stands.Length; tileId++)
            {
                yield return 1;
                if (!habitat.CanSupportForest(tileId)) continue;

                float moisture = habitat.GetMoisture(tileId);
                float elevation = habitat.GetNormalizedElevation(tileId);
                uint random = Hash(habitat.Seed, tileId, 0);
                if (Gameplay.Evaluate("forest.initialAllow", 0, 0, UnitFloat(random), moisture, elevation, random % 3, random % 5) < .5) continue;

                // Species must be drawn from an independent hash. Reusing the value the
                // density filter just tested keeps only tiles where random % density == 0,
                // which collapsed every mixture below to its first branch â€” the whole map
                // came out as a single species.
                uint speciesDraw = Hash(habitat.Seed, tileId, 2);
                ForestSpecies species = (ForestSpecies)(int)Gameplay.Evaluate("forest.initialSpecies", 0, 0, UnitFloat(speciesDraw), moisture, elevation, speciesDraw % 8, speciesDraw % 10);
                ForestSpeciesProfile profile = ForestSpeciesProfile.For(species);
                float age = (float)Gameplay.Evaluate("forest.initialAge", 0, profile.MatureAgeYears, UnitFloat(Hash(habitat.Seed, tileId, 1)));
                float health = (float)Gameplay.Evaluate("forest.initialHealth", 0, UnitFloat(Hash(habitat.Seed, tileId, 3)), Suitability(species, tileId));
                float biomass = InitialBiomass(profile, age, health);
                stands[tileId] = new ForestStand(species, age, biomass, health);
            }
        }

        private float Crowding(ReadOnlySpan<int> neighbours, int neighbourCount, double moisture)
        {
            float neighbourBiomass = 0f;
            for (int i = 0; i < neighbourCount; i++) neighbourBiomass += stands[neighbours[i]].Biomass;
            return (float)Responses.Regeneration("forest.seedCrowding", new() {
                Moisture = moisture, NeighborBiomass = neighbourCount == 0 ? 0 : neighbourBiomass / neighbourCount
            });
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

        private float Suitability(ForestSpecies species, int tileId)
        {
            var profile = ForestSpeciesProfile.For(species);
            return (float)Responses.Fitness(habitat.GetMoisture(tileId), habitat.GetNormalizedElevation(tileId),
                habitat.GetSoilProperties(tileId).Fertility, profile.PreferredElevation, profile.ElevationTolerance);
        }

        private float InitialBiomass(ForestSpeciesProfile profile, float age, float health)
        {
            return (float)Gameplay.Evaluate("forest.initialBiomass", profile.MaximumBiomass, age, profile.MatureAgeYears, health);
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
