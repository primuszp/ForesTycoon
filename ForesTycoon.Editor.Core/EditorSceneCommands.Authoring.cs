#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

internal enum BiomeGroundKind { Dry, Wet, Steep }
internal enum SpeciesColourChannel { Spring, Summer, Autumn, Bark, Dead, BarkPattern }

/// <summary>Operations whose invariants must hold regardless of which front end invokes them.</summary>
internal sealed partial class EditorSceneCommands
{
    private void RequireTarget(object target)
    {
        if (!Owns(target)) throw new InvalidOperationException("Az elem már nem a megnyitott pálya része.");
    }
    internal bool CanAddElevationZone => World.ElevationZones.Count is > 0 and < 8
        && World.ElevationZones[^1].MaxLevel - (World.ElevationZones.Count > 1 ? World.ElevationZones[^2].MaxLevel : -1) >= 2;
    internal void SetElevationBoundary(ElevationZoneDefinition zone, int level)
    {
        RequireTarget(zone); int index = World.ElevationZones.IndexOf(zone);
        if (index == World.ElevationZones.Count - 1) throw new InvalidOperationException("A legfelső öv felfelé nyitott.");
        int low = index == 0 ? 0 : World.ElevationZones[index - 1].MaxLevel + 1;
        int high = World.ElevationZones[index + 1].MaxLevel - 1;
        Edit(zone, value => value.MaxLevel = Math.Clamp(level, low, high));
    }
    internal void AddElevationZone()
    {
        if (!CanAddElevationZone) throw new InvalidOperationException("Nincs hely új magassági övnek, vagy elértük a 8 öves korlátot.");
        int floor = World.ElevationZones.Count > 1 ? World.ElevationZones[^2].MaxLevel + 1 : 0;
        EditWorld(world => world.ElevationZones.Insert(world.ElevationZones.Count - 1,
            new() { Id = "zone" + Guid.NewGuid().ToString("N")[..6], Name = "Új öv", Color = "#9A9A9A", MaxLevel = floor }));
    }
    internal void DeleteElevationZone(ElevationZoneDefinition zone)
    {
        RequireTarget(zone);
        if (World.ElevationZones.Count <= 1) throw new InvalidOperationException("Az utolsó magassági öv nem törölhető.");
        int index = World.ElevationZones.IndexOf(zone);
        var replacement = World.ElevationZones[index == World.ElevationZones.Count - 1 ? index - 1 : index + 1];
        Edit(zone, _ =>
        {
            if (index == World.ElevationZones.Count - 1) replacement.MaxLevel = zone.MaxLevel;
            foreach (var biome in World.Biomes.Where(b => b.Zone == zone.Id)) biome.Zone = replacement.Id;
            World.ElevationZones.Remove(zone);
        });
    }
    internal void ResetElevationZones()
        => EditWorld(world =>
        {
            var defaults = ElevationZoneDefinition.Defaults(world.MaxHeight);
            foreach (var biome in world.Biomes.Where(b => !defaults.Any(z => z.Id == b.Zone)))
            {
                int level = world.ElevationZones.FirstOrDefault(z => z.Id == biome.Zone)?.MaxLevel ?? 0;
                biome.Zone = (defaults.FirstOrDefault(z => z.MaxLevel >= level) ?? defaults[^1]).Id;
            }
            world.ElevationZones = defaults;
        });
    internal BiomeDefinition AddBiome(string zone)
    {
        if (World.Biomes.Count >= 32) throw new InvalidOperationException("Legfeljebb 32 biom lehet.");
        RequireZone(zone);
        if (World.GroundMaterials.Count == 0) throw new InvalidOperationException("A biomhoz felszínanyag szükséges.");
        var biome = new BiomeDefinition { Id = "biome" + Guid.NewGuid().ToString("N")[..6], Name = "Új biom", Zone = zone, Color = "#8A9A6A" };
        ResolveGrounds(biome);
        EditWorld(world => world.Biomes.Add(biome)); return biome;
    }
    internal void DeleteBiome(BiomeDefinition biome) => Edit(biome, value => World.Biomes.Remove(value));
    internal void SetBiomeZone(BiomeDefinition biome, string zone)
    { RequireTarget(biome); RequireZone(zone); Edit(biome, value => value.Zone = zone); }
    private void RequireZone(string zone)
    {
        if (!World.ElevationZones.Any(z => z.Id == zone)) throw new ArgumentException("Ismeretlen magassági öv.", nameof(zone));
    }
    internal void SetBiomeGround(BiomeDefinition biome, BiomeGroundKind kind, string material)
    {
        RequireTarget(biome);
        if (!Enum.IsDefined(kind) || !World.GroundMaterials.Any(m => m.Id == material)) throw new ArgumentException("Ismeretlen felszín vagy felszínanyag.");
        Edit(biome, value =>
        {
            switch (kind)
            {
                case BiomeGroundKind.Dry: value.DryGround = material; break;
                case BiomeGroundKind.Wet: value.WetGround = material; break;
                case BiomeGroundKind.Steep: value.SteepGround = material; break;
            }
        });
    }
    private void ResolveGrounds(BiomeDefinition biome)
    {
        string Resolve(string id) => World.GroundMaterials.Any(m => m.Id == id) ? id : World.GroundMaterials[0].Id;
        biome.DryGround = Resolve(biome.DryGround); biome.WetGround = Resolve(biome.WetGround); biome.SteepGround = Resolve(biome.SteepGround);
    }
    internal void ResetBiomes()
    {
        if (World.GroundMaterials.Count == 0 || World.ElevationZones.Count == 0) throw new InvalidOperationException("A biomhoz öv és felszínanyag szükséges.");
        var defaults = BiomeDefinition.Defaults();
        foreach (var biome in defaults)
        {
            if (!World.ElevationZones.Any(z => z.Id == biome.Zone)) biome.Zone = World.ElevationZones[0].Id;
            ResolveGrounds(biome);
        }
        EditWorld(world => world.Biomes = defaults);
    }
    internal GroundMaterialDefinition AddMaterial(GroundMaterialDefinition source)
    {
        RequireTarget(source);
        if (World.GroundMaterials.Count >= 32) throw new InvalidOperationException("Legfeljebb 32 felszínanyag lehet.");
        var material = new GroundMaterialDefinition { Id = "ground" + Guid.NewGuid().ToString("N")[..6], Name = "Új felszín", Color = source.Color, Pattern = source.Pattern };
        EditWorld(world => world.GroundMaterials.Add(material)); return material;
    }
    internal void RenameMission(MissionGoal mission, string id)
    {
        RequireTarget(mission);
        if (string.IsNullOrWhiteSpace(id) || World.Missions.Any(m => m != mission && m.Id == id)) throw new ArgumentException("A küldetés azonosítója legyen egyedi és nem üres.", nameof(id));
        Edit(mission, value => value.Id = id);
    }
    internal void SetMissionTitle(MissionGoal mission, string title)
    {
        RequireTarget(mission);
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A küldetés címe nem lehet üres.", nameof(title));
        Edit(mission, value => value.Title = title);
    }
    internal void SetMissionMetric(MissionGoal mission, string metric)
    {
        RequireTarget(mission);
        if (metric == null || !(metric.StartsWith("world.", StringComparison.Ordinal) || metric.StartsWith("stock.", StringComparison.Ordinal))) throw new ArgumentException("A mérőszám world.* vagy stock.* kulcs legyen.", nameof(metric));
        Edit(mission, value => value.Metric = metric);
    }
    internal void SetMissionCondition(MissionGoal mission, string comparison, double target)
    {
        RequireTarget(mission);
        if (!MissionGoal.Comparisons.Contains(comparison) || !double.IsFinite(target)) throw new ArgumentException("Érvénytelen küldetésfeltétel.");
        Edit(mission, value => { value.Comparison = comparison; value.Target = target; });
    }
    internal void SetMissionDeadline(MissionGoal mission, double year)
    {
        RequireTarget(mission);
        if (!double.IsFinite(year)) throw new ArgumentException("A határidő véges szám legyen.", nameof(year));
        Edit(mission, value => value.DeadlineYear = Math.Clamp(year, 0, 1000));
    }
    private void RequireShapePhase(SpeciesDefinition species, string? phase)
    {
        RequireTarget(species);
        if (phase != null && phase != TreeShapeDefinition.DeadState && !TreeShapeDefinition.PhaseNames.Contains(phase))
            throw new ArgumentException("Ismeretlen életfázis.", nameof(phase));
    }
    internal void SetShapeParameter(SpeciesDefinition species, string? phase, string name, double? value)
    {
        RequireShapePhase(species, phase);
        if (string.IsNullOrWhiteSpace(name) || value.HasValue && !double.IsFinite(value.Value)) throw new ArgumentException("Érvénytelen famegjelenési paraméter.");
        EditShape(species, phase, values => { if (value.HasValue) values[name] = value.Value; else values.Remove(name); });
    }
    internal void ResetShapePhase(SpeciesDefinition species, string? phase)
    { RequireShapePhase(species, phase); EditShape(species, phase, values => values.Clear()); }
    internal void SetSpeciesColour(SpeciesDefinition species, SpeciesColourChannel channel, string? value)
    {
        RequireTarget(species);
        if (!Enum.IsDefined(channel) || value != null && (channel == SpeciesColourChannel.BarkPattern
            ? !TreeColoursDefinition.BarkPatterns.Contains(value) : !IsRgb(value))) throw new ArgumentException("Érvénytelen fafajszín vagy kéregminta.", nameof(value));
        EditColours(species, colours =>
        {
            switch (channel)
            {
                case SpeciesColourChannel.Spring: colours.Spring = value; break;
                case SpeciesColourChannel.Summer: colours.Summer = value; break;
                case SpeciesColourChannel.Autumn: colours.Autumn = value; break;
                case SpeciesColourChannel.Bark: colours.Bark = value; break;
                case SpeciesColourChannel.Dead: colours.Dead = value; break;
                case SpeciesColourChannel.BarkPattern: colours.BarkPattern = value; break;
            }
        });
    }
    internal void ResetSpeciesColours(SpeciesDefinition species) => Edit(species, value => value.Colours = null);
    private static bool IsRgb(string value) => value.Length == 7 && value[0] == '#'
        && int.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _);
    internal void RestoreSpecies(SpeciesDefinition species)
    {
        RequireTarget(species); var builtin = SimulationWorld.DefaultScenario().Species.Find(value => value.Id == species.Id);
        if (builtin == null) throw new InvalidOperationException("A fafajnak nincs beépített mintája.");
        Edit(species, _ => World.Species[World.Species.IndexOf(species)] = builtin);
    }
    internal void MoveHorizonUp(SoilTypeDefinition soil, int index)
    {
        RequireHorizon(soil, index);
        if (index == 0) return;
        EditHorizons(soil, values => (values[index - 1], values[index]) = (values[index], values[index - 1]));
    }
    internal void DeleteHorizon(SoilTypeDefinition soil, int index)
    {
        var profile = RequireHorizon(soil, index);
        if (profile.Count <= 1) throw new InvalidOperationException("Az utolsó talajszint nem törölhető.");
        EditHorizons(soil, values => values.RemoveAt(index));
    }
    internal void AddHorizon(SoilTypeDefinition soil)
    {
        RequireTarget(soil); var profile = soil.Profile();
        if (profile.Count >= 8) throw new InvalidOperationException("Legfeljebb 8 talajszint lehet.");
        string colour = profile[^1].Color;
        EditHorizons(soil, values => values.Add(new() { Name = "Új szint", Depth = .3, Color = colour }));
    }
    private IReadOnlyList<SoilHorizon> RequireHorizon(SoilTypeDefinition soil, int index)
    {
        RequireTarget(soil); var profile = soil.Profile();
        if (index < 0 || index >= profile.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return profile;
    }
    internal void SetHorizonName(SoilTypeDefinition soil, int index, string name)
    {
        RequireHorizon(soil, index);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A talajszint neve nem lehet üres.", nameof(name));
        EditHorizons(soil, values => values[index].Name = name.Trim());
    }
    internal void SetHorizonColour(SoilTypeDefinition soil, int index, string colour)
    {
        RequireHorizon(soil, index);
        if (colour == null || !IsRgb(colour)) throw new ArgumentException("Érvénytelen RGB-szín.", nameof(colour));
        EditHorizons(soil, values => values[index].Color = colour);
    }
    internal void SetHorizonDepth(SoilTypeDefinition soil, int index, double depth)
    {
        RequireHorizon(soil, index);
        if (!double.IsFinite(depth)) throw new ArgumentException("A vastagság véges szám legyen.", nameof(depth));
        EditHorizons(soil, values => values[index].Depth = Math.Round(Math.Clamp(depth, .01, 5), 2));
    }
    internal void ResetHorizons(SoilTypeDefinition soil) => Edit(soil, value => value.Horizons = new());
}
