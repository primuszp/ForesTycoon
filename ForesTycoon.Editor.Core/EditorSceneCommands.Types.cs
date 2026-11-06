#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

internal sealed partial class EditorSceneCommands
{
    internal SoilTypeDefinition CopySoil(SoilTypeDefinition source)
    {
        var copy = new RuleProject { World = new() { SoilTypes = new() { source } } }.Clone().World!.SoilTypes[0];
        copy.Id = "soil" + Guid.NewGuid().ToString("N")[..6]; copy.Name += " (másolat)";
        Edit(source, _ => World.SoilTypes.Add(copy));
        return copy;
    }
    internal void DeleteSoil(SoilTypeDefinition soil, bool usedInRuntime)
    {
        if (World.SoilTypes.Count <= 1 || World.SoilPaint.ContainsValue(soil.Id) || usedInRuntime)
            throw new InvalidOperationException("A talajtípus használatban van, vagy az utolsó típus.");
        Edit(soil, _ => World.SoilTypes.Remove(soil));
    }
    internal void EditHorizons(SoilTypeDefinition soil, Action<List<SoilHorizon>> edit)
        => Edit(soil, value => {
            if (value.Horizons is not { Count: > 0 }) value.Horizons = SoilHorizon.Defaults(value.Id, value.Color);
            edit(value.Horizons);
        });

    internal void EditShape(SpeciesDefinition species, string? phase, Action<Dictionary<string, double>> edit)
        => Edit(species, value => {
            value.Shape ??= new();
            var values = value.Shape.All;
            if (phase != null && !value.Shape.Phases.TryGetValue(phase, out values)) value.Shape.Phases[phase] = values = new();
            edit(values!);
            foreach (string key in value.Shape.Phases.Where(p => p.Value.Count == 0).Select(p => p.Key).ToArray()) value.Shape.Phases.Remove(key);
            if (value.Shape.IsEmpty) value.Shape = null;
        });
    internal void EditColours(SpeciesDefinition species, Action<TreeColoursDefinition> edit)
        => Edit(species, value => {
            value.Colours ??= new(); edit(value.Colours);
            if (value.Colours.IsEmpty) value.Colours = null;
        });

    internal MissionGoal AddMission()
    {
        if (World.Missions.Count >= 32) throw new InvalidOperationException("Legfeljebb 32 küldetés lehet.");
        int n = World.Missions.Count + 1;
        while (World.Missions.Any(m => m.Id == $"kuldetes-{n}")) n++;
        var mission = new MissionGoal { Id = $"kuldetes-{n}", Title = $"Küldetés {n}" };
        EditWorld(world => world.Missions.Add(mission)); return mission;
    }
    internal void DeleteMission(MissionGoal mission) => Edit(mission, _ => World.Missions.Remove(mission));

    internal void ResetMaterials()
    {
        var keep = World.GroundMaterials.Where(m => !GroundMaterialDefinition.Defaults().Any(d => d.Id == m.Id) && MaterialUsed(m)).ToList();
        EditWorld(world => world.GroundMaterials = GroundMaterialDefinition.Defaults().Concat(keep).ToList());
    }
    internal bool MaterialUsed(GroundMaterialDefinition material) => World.Biomes.Any(b => b.DryGround == material.Id || b.WetGround == material.Id || b.SteepGround == material.Id);
    internal void DeleteMaterial(GroundMaterialDefinition material)
    {
        if (World.GroundMaterials.Count <= 1 || MaterialUsed(material)) throw new InvalidOperationException("A felszínanyagot biom használja, vagy az utolsó anyag.");
        Edit(material, _ => World.GroundMaterials.Remove(material));
    }
}
