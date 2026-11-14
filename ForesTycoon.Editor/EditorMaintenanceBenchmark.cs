using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor;

/// <summary>Reproducible CPU/allocation profile of small and large authoring documents; no window or renderer is created.</summary>
internal static class EditorMaintenanceBenchmark
{
    internal sealed record Measurement(string Operation, int Iterations, double Microseconds, double Bytes);
    internal sealed record Case(int Rules, int PaintedTiles, int CheckpointCharacters, int JsonBytes, IReadOnlyList<Measurement> Measurements);
    private static object sink;
    internal static void Run(string output)
    {
        var cases = new List<Case>();
        foreach (int count in new[] { 32, 256 })
        {
            var project = CreateProject(count); var document = new EditorDocument(); document.Load(project);
            // Validate the stress fixture before measuring it; invalid drafts must not hide compilation regressions.
            _ = new RuleEngine(document.Rules, document.Fields, DynamicsRuntime.Keys(project.Dynamics, SimulationWorld.RuleWorldFields), _ => 0);
            var book = new RuleBookSession(document); var scene = new SceneSession(document.Scene);
            using var catalog = new RuleCatalogSession(document.Tuning, false); catalog.Load(CurrentGameRules.Build());
            var selected = book.Entries[0]; var sentence = RuleFieldCatalog.Sentences(null, selected.Scope);
            object DetailFrame()
            {
                var effect = selected.Effects[0];
                var lines = book.Describe(selected, sentence);
                sink = book.DescribeTerm(effect, effect.Influence.Terms[0], sentence); return lines;
            }
            // Reference implementations retain the former detached-copy path for a same-process comparison.
            object DetachedDetailFrame()
            {
                var effect = selected.Effects[0];
                var lines = sentence.Rule(selected.Capture());
                sink = sentence.Term(effect.Capture(), effect.Influence.Terms[0].Capture()); return lines;
            }
            object ReadFrame()
            {
                double sum = 0;
                for (int i = 0; i < Math.Min(32, book.Entries.Count); i++) sum += book.Entries[i].Effects[0].Influence.Terms[0].Amount;
                sum += scene.World.SoilPaint.Count; return sum;
            }
            var rows = new List<Measurement> {
                Measure("Document capture", 3, () => document.Capture()),
                Measure("Revision via detached capture (reference)", 3, () => EditorDocument.RevisionOf(document.Capture())),
                Measure("Revision after edit", 3, () => { document.Invalidate(); return document.Revision(); }),
                Measure("Cached revision", 1000, () => document.Revision()),
                Measure("Readonly frame queries (32 rows)", 1000, ReadFrame),
                Measure("Rule detail via detached captures (reference)", 200, DetachedDetailFrame),
                Measure("Rule detail text frame", 200, DetailFrame),
                Measure("Cached field analysis", 1000, () => book.FieldUses()),
                Measure("Catalog search (unchanged)", 1000, () => catalog.VisibleRules("", "water")),
                Measure("No-change history checkpoint", 1000, () => document.History.Checkpoint()),
                Measure("History transaction", 3, () => { document.Scene.SetTuning("DieselPrice", document.Tuning.Get("DieselPrice") == 1.2 ? 1.3 : 1.2); return document.History.Count; }),
                Measure("Undo/redo pair", 3, () => { document.History.Undo(); document.History.Redo(); return document.History.Count; })
            };
            var item = new Case(count, project.World.SoilPaint.Count, project.World.InitialState?.Length ?? 0,
                System.Text.Encoding.UTF8.GetByteCount(project.ToJson()), rows); cases.Add(item);
            foreach (var row in rows) Console.WriteLine($"{count} rules | {row.Operation}: {row.Microseconds:F2} us/op; {row.Bytes:F0} B/op");
        }
        string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Runtime = Environment.Version.ToString(),
            OS = Environment.OSVersion.ToString(),
            Samples = 5,
            Statistic = "Median of five samples after warmup; allocated bytes on calling thread",
            Cases = cases
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("CPU only: UI drawing, GPU work, background preview builds and fixture construction excluded. " + path);
        GC.KeepAlive(sink);
    }
    internal static RuleProject CreateProject(int rules)
    {
        var project = SimulationWorld.DefaultRuleProject(); project.Algorithms = null;
        using (var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(rules >= 256 ? 129 : 17, 42)))
        {
            world.Forest.Clear(); project.World = SimulationLoader.CaptureScenario(world);
        }
        var starter = RuleEngine.StarterRules().Single(rule => rule.Id == "water.precipitation");
        project.Rules = Enumerable.Range(0, rules).Select(i =>
        {
            var rule = RuleEngine.Clone(new() { starter })[0]; rule.Id = "benchmark." + i; rule.Name = "Benchmark " + i;
            var effect = rule.Effects.First(e => e.Influence != null);
            effect.Influence.Terms = Enumerable.Range(0, 4).Select(_ => new InfluenceTerm
            {
                Amount = .1,
                Factors = new() { new() { Source = "cover" }, new() { Source = "cover" } }
            }).ToList(); return rule;
        }).ToList();
        for (int i = 0; i < project.World.Size * project.World.Size; i++) project.World.SoilPaint[i] = project.World.SoilTypes[i % project.World.SoilTypes.Count].Id;
        return project;
    }
    private static Measurement Measure(string name, int iterations, Func<object> operation)
    {
        sink = operation(); sink = operation(); var times = new double[5]; var bytes = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long allocated = GC.GetAllocatedBytesForCurrentThread(); long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = operation();
            times[sample] = Stopwatch.GetElapsedTime(started).TotalMicroseconds / iterations;
            bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
        }
        Array.Sort(times); Array.Sort(bytes); return new(name, iterations, times[2], bytes[2]);
    }
}
