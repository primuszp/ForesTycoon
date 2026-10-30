using System.Collections.Generic;

namespace ForesTycoon;

partial class SimulationWorld
{
    // Optional host lifecycle hooks. A standalone world never creates presentation resources.
    protected virtual SimulationWorld CreateCandidate(TerrainMap map, double yearSeconds, SoilLandscapeDefinition soils,
        ClimateDefinition climate, Ecosystem preparedEcosystem = null, bool deferRules = false,
        EcologyResponseProgram initialResponses = null, WorldScenario scene = null)
        => new(map, yearSeconds, soils, climate, preparedEcosystem, deferRules, initialResponses, scene);

    protected virtual void VerifyPresentationAccess() { }
    protected virtual void BindPresentationQueries() { }
    protected virtual void InvalidatePresentationGround() { }
    protected virtual void AttachPresentation() { }
    protected virtual IEnumerable<int> PreparePresentationWork() { yield break; }
    protected virtual void SwapPresentation(SimulationWorld candidate) { }
    protected virtual IEnumerable<int> DisposePresentationWork() { yield break; }

    internal VehicleSystem VehicleState => vehicles;
    internal WorldEffectSystem EffectState => effects;
    protected object SpeciesContext => worldSpecies;
    protected WorldScenario PresentationScene => ruleProjectWorld;
    protected static WorldScenario DefaultPresentationScene => BuiltinZones;
}
