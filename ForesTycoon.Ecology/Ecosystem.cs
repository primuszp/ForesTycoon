using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    /// <summary>
    /// Composition root of the ecosystem simulation: one forest, one weather-and-water model and the
    /// multi-rate order they advance in. A game talks to this object (plus the species catalog and the
    /// <see cref="TreeShapeSpec"/> contract) and never wires the parts itself.
    /// <para>
    /// Extension points: a new habitat is an <see cref="IForestHabitat"/>; a different weather or water
    /// model is an <see cref="IForestEnvironment"/> (plus <see cref="IForestCanopy"/> for the vegetation
    /// feedback); a new species is a <see cref="ForestSpecies"/> value with a profile and a traits row.
    /// </para>
    /// </summary>
    internal sealed class Ecosystem
    {
        private ForestEnvironmentCoordinator coordinator;

        internal Dictionary<string, double> WorkProfile { get => coordinator.Runtime.WorkProfile; set => coordinator.Runtime.WorkProfile = value; }
        internal ForestSystem Forest { get; private set; }
        internal EnvironmentSystem Environment { get; private set; }
        internal SoilLandscape Soils { get; private set; }
        /// <summary>Simulated seconds per forest year (game tempo).</summary>
        internal double ForestYearSeconds { get; private set; }

        internal Ecosystem(IForestHabitat habitat, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
            SoilLandscapeDefinition soils = null, ClimateDefinition climate = null, ForesTycoon.Rules.EcologyResponseProgram responseRules = null)
        {
            if (habitat == null) throw new ArgumentNullException(nameof(habitat));
            // Validate before allocating the forest, including direct diagnostic callers.
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds)) throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            habitat = BindSoils(habitat, soils);
            Forest = new ForestSystem(habitat, responseRules: responseRules);
            Attach(habitat, forestYearSeconds, climate);
        }

        private Ecosystem() { }
        internal static IEnumerable<int> CreateWork(IForestHabitat habitat, double forestYearSeconds,
            SoilLandscapeDefinition soils, ClimateDefinition climate, WorkResult<Ecosystem> result, ForesTycoon.Rules.EcologyResponseProgram responseRules = null)
        {
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds)) throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            var model = new Ecosystem { ForestYearSeconds = forestYearSeconds };
            if (soils != null)
            {
                model.Soils = new SoilLandscape(habitat, soils, true);
                foreach (int unit in model.Soils.InitializeWork(habitat)) yield return unit;
                habitat = new SoilHabitat(habitat, model.Soils);
            }
            model.Forest = new ForestSystem(habitat, deferInitialization: true, responseRules: responseRules);
            foreach (int unit in model.Forest.ResetWork(habitat)) yield return unit;
            model.Forest.UseEnvironmentTempo(forestYearSeconds);
            model.Environment = new EnvironmentSystem(habitat, model.Forest, forestYearSeconds, climate, true, responseRules);
            model.Environment.ResponseRules = responseRules;
            foreach (int unit in model.Environment.InitializeWork()) yield return unit;
            model.coordinator = new ForestEnvironmentCoordinator(model.Forest, model.Environment, deferInitialization: true);
            foreach (int unit in model.Forest.RefreshEnvironmentRatesWork()) yield return unit;
            result.Value = model;
        }

        /// <summary>Starts over on a new habitat (a regenerated or loaded map) at the given tempo.</summary>
        internal void Reset(IForestHabitat habitat, double forestYearSeconds, SoilLandscapeDefinition soils = null, ClimateDefinition climate = null)
        {
            if (habitat == null) throw new ArgumentNullException(nameof(habitat));
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds)) throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            habitat = BindSoils(habitat, soils);
            Forest.Environment = null;
            Forest.Reset(habitat);
            Attach(habitat, forestYearSeconds, climate);
        }

        private IForestHabitat BindSoils(IForestHabitat habitat, SoilLandscapeDefinition definition)
        {
            var landscape = definition == null ? null : new SoilLandscape(habitat, definition);
            Soils = landscape;
            return landscape == null ? habitat : new SoilHabitat(habitat, landscape);
        }

        private void Attach(IForestHabitat habitat, double forestYearSeconds, ClimateDefinition climate)
        {
            ForestYearSeconds = forestYearSeconds;
            Forest.Environment = null;
            Forest.UseEnvironmentTempo(forestYearSeconds);
            Environment = new EnvironmentSystem(habitat, Forest, forestYearSeconds, climate, responseRules: Forest.ResponseRules);
            Environment.ResponseRules = Forest.ResponseRules;
            coordinator = new ForestEnvironmentCoordinator(Forest, Environment);
        }

        /// <summary>Advances weather, water and forest by fixed-step game time.</summary>
        internal void Update(double fixedDeltaSeconds) => coordinator.Update(fixedDeltaSeconds);
        internal IEnumerable<int> UpdateWork(double seconds) => coordinator.UpdateWork(seconds);

        internal EcologyCheckpoint Capture() => new(1, ForestYearSeconds, Soils?.Definition.Catalog.Hash,
            Soils?.ProfileIndices.ToArray(), coordinator.Runtime.Capture(), Forest.Capture(), Environment.Capture(), Environment.Climate.Definition);

        // Caller restores an isolated ecosystem; failure must never publish partially restored state.
        internal void Restore(EcologyCheckpoint state) => SimulationWork.Complete(RestoreWork(state));

        internal IEnumerable<int> RestoreWork(EcologyCheckpoint state)
        {
            ArgumentNullException.ThrowIfNull(state);
            CheckpointGuard.Require((state.Climate ?? ClimateDefinition.Legacy) == Environment.Climate.Definition, "climate model");
            CheckpointGuard.Require(state.Version == 1 && state.ForestYearSeconds == ForestYearSeconds &&
                state.SoilHash == Soils?.Definition.Catalog.Hash, "ecology model");
            if (Soils != null) CheckpointGuard.Require(state.SoilProfiles != null &&
                Soils.ProfileIndices.SequenceEqual(state.SoilProfiles), "soil raster");
            foreach (int unit in Forest.RestoreWork(state.Forest)) yield return unit;
            foreach (int unit in Environment.RestoreWork(state.Environment)) yield return unit;
            coordinator.Runtime.Restore(state.Clock);
            CheckpointGuard.Require(Math.Abs(Environment.Time - state.Clock.Time) <= Math.Max(1, Environment.Time) * 1e-9,
                "weather/runtime clock agreement");
            CheckpointGuard.Require(Math.Abs(Forest.ForestYear * ForestYearSeconds - Environment.Time) <=
                Math.Max(1, Environment.Time) * 1e-9, "forest/weather calendar agreement");
        }

        internal void ApplyTerrainEdit(ReadOnlySpan<int> changedTiles)
        {
            Forest.ClearTerrainTiles(changedTiles);
        }
    }
}
