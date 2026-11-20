using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

internal static class ForestFollowup
{
    private sealed record Recruit(ulong Month, ForestSpecies Species, bool Gap);
    private const string Columns = "month,id,tile,species,dead,soil_mm,mean_supply,water,light,light_response,space,resource_limit,diameter_cm,health,stress_years";
    private static void Write(TextWriter writer, TreeMonthSample s, ForesTycoon.Rules.EcologyResponseProgram rules) => writer.WriteLine(FormattableString.Invariant(
        $"{s.Month},{s.Id},{s.Tile},{s.Species},{(s.Dead ? 1 : 0)},{s.RootWaterMm:F4},{s.Supply:F6},{s.Resources.Water:F6},{s.Resources.Light:F6},{s.Resources.LightResponse(s.Species, rules):F6},{s.Resources.Space:F6},{s.Resources.Limitation(s.Species, rules):F6},{s.DiameterMetres * 100:F5},{s.Health:F6},{s.StressYears:F6}"));

    internal static void Run(int years, int seed, string directory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        int months = checked(years * 12);
        Directory.CreateDirectory(directory);
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, seed), forestYearSeconds: 120, enableRendering: false);
        var recruits = new Dictionary<ulong, Recruit>();
        var deaths = new Dictionary<ulong, ulong>();
        using var trace = File.CreateText(Path.Combine(directory, $"trees-{seed}.csv"));
        trace.WriteLine(Columns);
        world.Forest.SeedlingEstablished += (month, tree, gap) => recruits.Add(tree.Id, new(month, tree.Species, gap));
        world.Forest.MonthlyTreeObserved += sample => {
            if (sample.Dead && recruits.ContainsKey(sample.Id)) deaths.Add(sample.Id, sample.Month);
            if (sample.Species is ForestSpecies.Birch or ForestSpecies.Pine) Write(trace, sample, world.Forest.ResponseRules);
        };
        for (int month = 0; month < months; month++) AdvanceMonth(world);
        using var output = File.CreateText(Path.Combine(directory, $"cohorts-{seed}.csv"));
        output.WriteLine("seed,species,origin,born,age_years,eligible,alive,censored");
        foreach (var group in recruits.GroupBy(p => (p.Value.Species, p.Value.Gap)).OrderBy(g => g.Key.Species).ThenBy(g => g.Key.Gap)) {
            foreach (uint age in new uint[] { 1, 3, 5 }) {
                int eligible = 0, alive = 0;
                foreach (var (id, recruit) in group) {
                    ulong anniversary = recruit.Month + age * 12;
                    if (anniversary > (ulong)months) continue;
                    eligible++;
                    if (!deaths.TryGetValue(id, out ulong death) || death > anniversary) alive++;
                }
                output.WriteLine($"{seed},{group.Key.Species},{(group.Key.Gap ? "gap" : "open")},{group.Count()},{age},{eligible},{alive},{group.Count()-eligible}");
            }
        }
    }

    private static void AdvanceMonth(GameWorld world)
    {
        for (int tick = 0; tick < 300; tick++) world.Update(1d / 30);
    }

    // Dudlesz soil-group means and individual profiles for 0–200 cm. Other hydraulic parameters are controlled model assumptions.
    internal static void SoilRecovery(TextWriter output)
    {
        output.WriteLine("reference,dv_mm,deficit_mm," + Columns);
        foreach (var (reference, capacity) in new[] { ("ARBE-mean", 184), ("ABE-mean", 280),
            ("Dudlesz-137", 195), ("Dudlesz-12", 289) }) {
            var standard = SoilProperties.Standard;
            var soil = standard with { FieldCapacity = standard.WiltingPoint + capacity,
                Saturation = standard.WiltingPoint + capacity + standard.Saturation - standard.FieldCapacity };
            var data = new SoilCatalogData { Version = 1, Profiles = new[] { new SoilProfileData {
                Id = "reference", Name = "Dudlesz", Color = "#846744", PreferredMoisture = .5, PreferredTexture = .5, Properties = soil } } };
            var definition = new SoilLandscapeDefinition(1, new SoilCatalog(JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), forestYearSeconds: 120, soils: definition, enableRendering: false);
            var trees = world.Forest.IndividualTrees.Patches.OrderBy(p => p.Key)
                .SelectMany(p => p.Value.Trees.Take(p.Value.Count)).Where(t => t.Species is ForestSpecies.Birch or ForestSpecies.Pine)
                .GroupBy(t => t.Species).Select(group => group.First()).ToArray();
            if (trees.Length != 2) throw new InvalidOperationException("Missing reference species.");
            int deficit = 0;
            var ids = trees.Select(t => t.Id).ToHashSet();
            world.Forest.MonthlyTreeObserved += s => {
                if (ids.Contains(s.Id)) { output.Write($"{reference},{capacity},{deficit},"); Write(output, s, world.Forest.ResponseRules); }
            };
            foreach (int step in new[] { 0, 40, 80, 120, 160, 200, 240, 200, 160, 120, 80, 40, 0 }) {
                deficit = step;
                for (int tick = 0; tick < 300; tick++) {
                    foreach (var t in trees)
                        world.Environment.SetWater(t.TileId, EnvironmentSystem.WaterStore.Soil, Math.Max(soil.WiltingPoint, soil.FieldCapacity - deficit));
                    world.Update(1d / 30);
                }
            }
        }
    }
}
