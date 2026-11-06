#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>A dictionary projection with no mutable dictionary or owned value reachable from its public API.</summary>
internal sealed class EditorReadDictionary<TKey, TValue>(Func<IEnumerable<KeyValuePair<TKey, TValue>>> entries,
    Func<TKey, (bool Found, TValue Value)> lookup, Func<int> count) : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    private static readonly Dictionary<TKey, TValue> empty = new();
    public int Count => count();
    public TValue this[TKey key] => lookup(key) is (true, var value) ? value : throw new KeyNotFoundException();
    public IEnumerable<TKey> Keys => entries().Select(entry => entry.Key);
    public IEnumerable<TValue> Values => entries().Select(entry => entry.Value);
    public bool ContainsKey(TKey key) => lookup(key).Found;
    public bool TryGetValue(TKey key, out TValue value) { var found = lookup(key); value = found.Value; return found.Found; }
    internal bool ContainsValue(TValue value) => Values.Contains(value);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => entries().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    internal static EditorReadDictionary<TKey, TValue> From(Func<Dictionary<TKey, TValue>?> dictionary)
        => new(() => dictionary() ?? empty, key => { bool found = (dictionary() ?? empty).TryGetValue(key, out var value); return (found, value!); }, () => dictionary()?.Count ?? 0);
}

internal sealed class SceneView
{
    private readonly WorldScenario model;
    public string Name => model.Name;
    public int Seed => model.Seed;
    public int Size => model.Size;
    public int MaxHeight => model.MaxHeight;
    public string? InitialState => model.InitialState;
    public EditorReadList<SoilView> SoilTypes { get; }
    public EditorReadList<ElevationZoneView> ElevationZones { get; }
    public EditorReadList<GroundMaterialView> GroundMaterials { get; }
    public EditorReadList<BiomeView> Biomes { get; }
    public double ProfileScale => model.ProfileScale;
    public EditorReadDictionary<int, string> SoilPaint { get; }
    public EditorReadDictionary<int, int> HeightPaint { get; }
    public EditorReadDictionary<int, string> ForestPaint { get; }
    public EditorReadList<int> ForestFelling { get; }
    public EditorReadDictionary<int, double> Springs { get; }
    public EditorReadList<SpeciesView> Species { get; }
    public EditorReadList<RoadView> Roads { get; }
    public EditorReadList<int> Sawmills { get; }
    public EditorReadList<int> Depots { get; }
    public EditorReadList<MissionView> Missions { get; }
    internal SceneView(WorldScenario model, SceneSession session)
    {
        this.model = model;
        SoilTypes = new(() => model.SoilTypes.Count, index => session.View(model.SoilTypes[index])!);
        ElevationZones = new(() => model.ElevationZones.Count, index => session.View(model.ElevationZones[index])!);
        GroundMaterials = new(() => model.GroundMaterials.Count, index => session.View(model.GroundMaterials[index])!);
        Biomes = new(() => model.Biomes.Count, index => session.View(model.Biomes[index])!);
        SoilPaint = EditorReadDictionary<int, string>.From(() => model.SoilPaint);
        HeightPaint = EditorReadDictionary<int, int>.From(() => model.HeightPaint);
        ForestPaint = EditorReadDictionary<int, string>.From(() => model.ForestPaint);
        ForestFelling = new(() => model.ForestFelling.Count, index => model.ForestFelling[index]);
        Springs = EditorReadDictionary<int, double>.From(() => model.Springs);
        Species = new(() => model.Species.Count, index => session.View(model.Species[index])!);
        Roads = new(() => model.Roads.Count, index => session.View(model.Roads[index])!);
        Sawmills = new(() => model.Sawmills.Count, index => model.Sawmills[index]);
        Depots = new(() => model.Depots.Count, index => model.Depots[index]);
        Missions = new(() => model.Missions.Count, index => session.View(model.Missions[index])!);
    }
    internal int ZoneOf(int level) => model.ZoneOf(level);
    internal int BiomeOf(int level, int column, int row) => model.BiomeOf(level, column, row);
    internal WorldScenario.GroundSurface Ground(int biome, double moisture, int slope) => model.Ground(biome, moisture, slope);
}

internal sealed class SoilView
{
    private readonly SoilTypeDefinition model;
    private readonly SceneSession session;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Color => model.Color;
    public double PreferredMoisture => model.PreferredMoisture;
    public double PreferredTexture => model.PreferredTexture;
    public double Saturation => model.Saturation;
    public double FieldCapacity => model.FieldCapacity;
    public double WiltingPoint => model.WiltingPoint;
    public double InfiltrationPerHour => model.InfiltrationPerHour;
    public double DrainagePerHour => model.DrainagePerHour;
    public double Fertility => model.Fertility;
    public double BearingCapacity => model.BearingCapacity;
    public EditorReadList<SoilHorizonView> Horizons { get; }
    internal SoilView(SoilTypeDefinition model, SceneSession session)
    {
        this.model = model; this.session = session;
        Horizons = new(() => model.Horizons?.Count ?? 0, index => session.View(model.Horizons[index])!);
    }
    internal IReadOnlyList<SoilHorizonView> Profile() => model.Profile().Select(horizon => session.View(horizon)!).ToArray();
}

internal sealed class SoilHorizonView
{
    private readonly SoilHorizon model;
    public string Name => model.Name;
    public double Depth => model.Depth;
    public string Color => model.Color;
    internal SoilHorizonView(SoilHorizon model, SceneSession session)
    {
        this.model = model;
    }
}

internal sealed class SpeciesView
{
    private readonly SpeciesDefinition model;
    private readonly SceneSession session;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Description => model.Description;
    public double MaximumAgeYears => model.MaximumAgeYears;
    public double MatureAgeYears => model.MatureAgeYears;
    public double MaximumBiomass => model.MaximumBiomass;
    public double AnnualGrowthRate => model.AnnualGrowthRate;
    public double PreferredMoisture => model.PreferredMoisture;
    public double MoistureTolerance => model.MoistureTolerance;
    public double PreferredElevation => model.PreferredElevation;
    public double ElevationTolerance => model.ElevationTolerance;
    public double ShadeTolerance => model.ShadeTolerance;
    public double WoodDensity => model.WoodDensity;
    public double DrySensitivity => model.DrySensitivity;
    public double MatureHeight => model.MatureHeight;
    public double MaxHeight => model.MaxHeight;
    public int Capacity => model.Capacity;
    public double TimberPrice => model.TimberPrice;
    public TreeShapeView? Shape => session.View(model.Shape);
    public TreeColoursDefinition? Colours => model.Colours == null ? null : new() { Spring = model.Colours.Spring, Summer = model.Colours.Summer,
        Autumn = model.Colours.Autumn, Bark = model.Colours.Bark, Dead = model.Colours.Dead, BarkPattern = model.Colours.BarkPattern };
    internal SpeciesView(SpeciesDefinition model, SceneSession session)
    {
        this.model = model; this.session = session;

    }
}

internal sealed class TreeShapeView
{
    private readonly TreeShapeDefinition model;
    private readonly EditorReadCache<Dictionary<string, double>, EditorReadDictionary<string, double>> phaseValues = new(values => EditorReadDictionary<string, double>.From(() => values));
    public EditorReadDictionary<string, double> All { get; }
    public EditorReadDictionary<string, IReadOnlyDictionary<string, double>> Phases { get; }
    internal TreeShapeView(TreeShapeDefinition model, SceneSession session)
    {
        this.model = model;
        All = EditorReadDictionary<string, double>.From(() => model.All);
        Phases = new(() => model.Phases?.Select(entry => new KeyValuePair<string, IReadOnlyDictionary<string, double>>(entry.Key,
            phaseValues.View(entry.Value)!)) ?? Enumerable.Empty<KeyValuePair<string, IReadOnlyDictionary<string, double>>>(),
            key => model.Phases != null && model.Phases.TryGetValue(key, out var values) ? (true, phaseValues.View(values)!) : (false, null!), () => model.Phases?.Count ?? 0);
    }
    public bool IsEmpty => model.IsEmpty;
}

internal sealed class ElevationZoneView
{
    private readonly ElevationZoneDefinition model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Color => model.Color;
    public int MaxLevel => model.MaxLevel;
    internal ElevationZoneView(ElevationZoneDefinition model, SceneSession session)
    {
        this.model = model;
    }
}

internal sealed class BiomeView
{
    private readonly BiomeDefinition model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Color => model.Color;
    public string Zone => model.Zone;
    public double Share => model.Share;
    public string Description => model.Description;
    public string DryGround => model.DryGround;
    public string WetGround => model.WetGround;
    public string SteepGround => model.SteepGround;
    internal BiomeView(BiomeDefinition model, SceneSession session)
    {
        this.model = model;
    }
}

internal sealed class GroundMaterialView
{
    private readonly GroundMaterialDefinition model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Color => model.Color;
    public string Pattern => model.Pattern;
    internal GroundMaterialView(GroundMaterialDefinition model, SceneSession session)
    {
        this.model = model;
    }
}

internal sealed class MissionView
{
    private readonly MissionGoal model;
    public string Id => model.Id;
    public string Title => model.Title;
    public string Description => model.Description;
    public string Metric => model.Metric;
    public string Comparison => model.Comparison;
    public double Target => model.Target;
    public double DeadlineYear => model.DeadlineYear;
    internal MissionView(MissionGoal model, SceneSession session)
    {
        this.model = model;
    }
    internal void Validate() => model.Validate();
}

internal sealed class RoadView
{
    private readonly RoadSegment model;
    public int From => model.From;
    public int To => model.To;
    public string Surface => model.Surface;
    internal RoadView(RoadSegment model, SceneSession session)
    {
        this.model = model;
    }
}
