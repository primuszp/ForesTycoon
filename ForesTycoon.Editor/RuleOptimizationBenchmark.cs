using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor
{
    /// <summary>CPU-only comparison of the previous algorithms and the indexed/cached editor operations.</summary>
    internal static class RuleOptimizationBenchmark
    {
        private sealed record Measurement(double MicrosecondsPerOperation, double BytesPerOperation);
        private sealed record Comparison(string Operation, int Iterations, Measurement Before, Measurement After);
        private static object sink;

        internal static void Run()
        {
            var catalog = CurrentGameRules.Build(); var index = new GameRuleIndex(catalog);
            var draft = RuleModel.Default(); var compiler = new RoadRuleDraftCompiler(); compiler.Compile(draft);
            RuleConnection[] OriginalConnections() => catalog.Rules.SelectMany(producer => producer.Writes.SelectMany(field =>
                catalog.Rules.Where(consumer => consumer.Id != producer.Id && consumer.Reads.Contains(field))
                    .Select(consumer => new RuleConnection(producer.Id, consumer.Id, field)))).Distinct().ToArray();
            object OriginalOverview() => index.Connections.Select(c => new RuleConnection(catalog.Rules.Find(r => r.Id == c.From).Module,
                catalog.Rules.Find(r => r.Id == c.To).Module, c.Field)).Where(c => c.From != c.To).GroupBy(c => (c.From, c.To)).Select(g => g.First()).ToArray();
            object UncachedCompilation() { var compiled = new CompiledRoadRule(draft); compiled.ValidateRange(); return compiled; }
            var rows = new List<Comparison>
            {
                Compare("Catalog connections", 250, () => OriginalConnections(), () => catalog.Connections()),
                Compare("Overview dependencies per frame (warm index)", 1000, OriginalOverview, () => index.ModuleConnections),
                Compare("Independent rule snapshot", 2000, () => RuleModel.FromJson(draft.ToJson()), () => draft.Clone()),
                Compare("Uncached vs cached unchanged draft validation", 1000, UncachedCompilation, () => compiler.Compile(draft))
            };
            string file = Path.GetFullPath("artifacts/rule-editor/optimization-benchmark.json"); Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(), Rules = catalog.Rules.Count,
                Connections = index.Connections.Count, Rounds = 5, Results = rows }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var row in rows)
                Console.WriteLine($"{row.Operation}: {row.Before.MicrosecondsPerOperation:F2} -> {row.After.MicrosecondsPerOperation:F2} us/op; {row.Before.BytesPerOperation:F0} -> {row.After.BytesPerOperation:F0} B/op");
            Console.WriteLine("CPU benchmark only; UI/rendering and index construction excluded. " + file);
            GC.KeepAlive(sink);
        }

        private static Comparison Compare(string name, int iterations, Func<object> before, Func<object> after)
        {
            // Alternate samples so JIT warmup and clock drift do not always favour one implementation.
            for (int i = 0; i < 64; i++) { sink = before(); sink = after(); }
            var oldSamples = new Measurement[5]; var newSamples = new Measurement[5];
            for (int i = 0; i < 5; i++)
            {
                if (i % 2 == 0) { oldSamples[i] = Measure(iterations, before); newSamples[i] = Measure(iterations, after); }
                else { newSamples[i] = Measure(iterations, after); oldSamples[i] = Measure(iterations, before); }
            }
            Measurement Median(Measurement[] samples) => samples.OrderBy(m => m.MicrosecondsPerOperation).ElementAt(2);
            return new(name, iterations, Median(oldSamples), Median(newSamples));
        }

        private static Measurement Measure(int iterations, Func<object> action)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = action();
            double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
            return new(microseconds, (double)(GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations);
        }
    }
}
