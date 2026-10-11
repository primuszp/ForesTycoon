using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon;

internal static class GraphRuntimeHost
{
    internal static bool RunHeadless(string[] args)
    {
        if (args.Length < 4 || args[0] != "--run-rule-project") return false;
        if (!int.TryParse(args[2], out int ticks) || ticks < 0 || ticks > 100000) throw new ArgumentException("0–100000 tick szükséges.");
        var project = RuleProject.FromJson(File.ReadAllText(args[1]));
        if (project.Algorithms != null && project.Algorithms.Revision != AlgorithmBuild.CurrentRevision) throw new InvalidDataException("A futtató algoritmusrevíziója eltér a projekttől.");
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        if (args.Length >= 6 && args[4] == "--world") {
            using var input = File.OpenRead(args[5]); world.Load(input, allowAlgorithmMigration: args.Contains("--migrate"));
        }
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var trace = new List<object>();
        for (int i = 0; i <= ticks; i++) {
            if (i % 30 == 0 || i == ticks) trace.Add(new { tick = world.SimulationTick, seconds = i / 30d,
                stocks = world.DynamicsValues.ToDictionary(p => p.Key, p => p.Value), fields = world.CaptureTrialFields() });
            if (i < ticks) world.Update(1d / 30);
            if (world.DynamicsError != null || world.BehaviorError != null) throw new InvalidDataException(world.DynamicsError ?? world.BehaviorError);
        }
        RuleProject.WriteAtomic(args[3], JsonSerializer.Serialize(new { revision = AlgorithmBuild.CurrentRevision, trace, world = world.ExportRuleWorld("Algoritmusgráf szimuláció") },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        int acknowledgement = Array.IndexOf(args, "--ack");
        if (acknowledgement >= 0) RuleProject.WriteAtomic(args[acknowledgement + 1], "ready");
        Console.WriteLine("Gráfszimuláció kész: " + Path.GetFullPath(args[3])); return true;
    }
    internal static bool DispatchProjectRuntime(string[] args)
    {
        if (!File.Exists(RuleProject.DefaultPath)) return false;
        var project = RuleProject.FromJson(File.ReadAllText(RuleProject.DefaultPath));
        if (project.Algorithms == null || project.Algorithms.Revision == AlgorithmBuild.CurrentRevision) return false;
        Console.WriteLine("Az editor algoritmusgráfjainak fordítása…");
        var runtime = AlgorithmBuild.CompileAsync(project.Algorithms).GetAwaiter().GetResult();
        using var child = AlgorithmBuild.StartRuntime(runtime, args, RuleProject.DefaultPath);
        return true;
    }
}

// Host observations are outside the editable simulation algorithms; sampling does not advance the world.
sealed partial class GameWorld
{
    internal Dictionary<string, double> CaptureTrialFields()
    {
        var fields = RuleWorldFields.ToDictionary(k => k, ReadRuleValue);
        double volume = 0, height = 0, diameter = 0, health = 0; int count = 0;
        foreach (var patch in forest.IndividualTrees.Patches.Select(p => p.Value))
            for (int i = 0; i < patch.Count; i++) {
                var tree = patch.Trees[i]; var size = tree.At(forest.ForestYear);
                volume += ForestTree.Volume(size); height += size.Height; diameter += size.Diameter; health += tree.Health; count++;
            }
        fields["forest.totalVolume"] = volume;
        if (count > 0) {
            fields["forest.meanHeight"] = height / count; fields["forest.meanDiameter"] = diameter / count; fields["forest.meanHealth"] = health / count;
        }
        double condition = 0; int roads = 0;
        for (int i = 0; i < map.Tiles.Count; i++) if (map.IsRoadTile(i)) { condition += map.GetRoadCondition(i); roads++; }
        if (roads > 0) fields["road.meanCondition"] = condition / roads;
        foreach (var stock in DynamicsValues) fields["stock." + stock.Key] = stock.Value;
        return fields;
    }
}
