#nullable enable
using System;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>Scene edits share ownership checks, revision invalidation and undo transactions across front ends.</summary>
internal sealed partial class EditorSceneCommands(EditorDocument document)
{
    internal WorldScenario World => document.World ?? throw new InvalidOperationException("Nincs megnyitott pálya.");

    internal void Edit<T>(T target, Action<T> change) where T : class
    {
        if (!Owns(target)) throw new InvalidOperationException("Az elem már nem a megnyitott pálya része.");
        document.History.Execute("pálya módosítása", () => { change(target); document.Invalidate(); });
    }
    internal void EditWorld(Action<WorldScenario> change) => Edit(World, change);
    internal void Replace(WorldScenario scenario)
        => document.History.Execute("pálya cseréje", () => {
            document.World = new RuleProject { World = scenario }.Clone().World;
            document.Invalidate();
        });
    internal void SetTuning(string key, double value)
        => document.History.Execute("szám módosítása", () => document.Tuning.Set(key, value));

    private bool Owns(object target) => target switch {
        WorldScenario world => ReferenceEquals(world, document.World),
        SoilTypeDefinition soil => World.SoilTypes.Contains(soil),
        SoilHorizon horizon => World.SoilTypes.Any(s => s.Horizons.Contains(horizon)),
        SpeciesDefinition species => World.Species.Contains(species),
        BiomeDefinition biome => World.Biomes.Contains(biome),
        ElevationZoneDefinition zone => World.ElevationZones.Contains(zone),
        GroundMaterialDefinition material => World.GroundMaterials.Contains(material),
        MissionGoal mission => World.Missions.Contains(mission),
        _ => false
    };
}
