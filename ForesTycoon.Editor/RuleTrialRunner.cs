using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Editor;

internal sealed record TrialMeasure(string Key, double Before, double After);
internal sealed record RuleTrialResult(string DraftRevision, string Source, string OutputPath, int Ticks,
    ulong StartTick, ulong EndTick, double StartYear, double EndYear, double BaselineEndYear, double EditedStartYear, TrialMeasure[] Measures);

internal static class RuleTrialRunner
{
    internal static async Task<RuleTrialResult> Run(RuleProject baseline, RuleProject edited, string save, string folder,
        string revision, string source, int ticks, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        string beforePath = Path.Combine(folder, "baseline.json"), afterPath = Path.Combine(folder, "edited.json");
        RuleProject.WriteAtomic(beforePath, baseline.ToJson()); RuleProject.WriteAtomic(afterPath, edited.ToJson());
        string beforeOutput = await RunOne(baseline, beforePath, "before", save, folder, ticks, cancellation);
        string afterOutput = await RunOne(edited, afterPath, "after", save, folder, ticks, cancellation);
        using var before = JsonDocument.Parse(File.ReadAllText(beforeOutput)); using var after = JsonDocument.Parse(File.ReadAllText(afterOutput));
        var a = before.RootElement.GetProperty("trace"); var b = after.RootElement.GetProperty("trace");
        ulong start = a[0].GetProperty("tick").GetUInt64(), end = a[a.GetArrayLength() - 1].GetProperty("tick").GetUInt64();
        if (b[0].GetProperty("tick").GetUInt64() != start || b[b.GetArrayLength() - 1].GetProperty("tick").GetUInt64() != end)
            throw new InvalidDataException("Az összehasonlított futások eltérő időhatárokon futottak.");
        var original = a[a.GetArrayLength() - 1].GetProperty("fields"); var modified = b[b.GetArrayLength() - 1].GetProperty("fields");
        var measures = original.EnumerateObject().Where(p => modified.TryGetProperty(p.Name, out _))
            .Select(p => new TrialMeasure(p.Name, p.Value.GetDouble(), modified.GetProperty(p.Name).GetDouble())).ToArray();
        if (measures.Any(m => !double.IsFinite(m.Before) || !double.IsFinite(m.After))) throw new InvalidDataException("Nem véges próbaeredmény.");
        var result = new RuleTrialResult(revision, source, Path.Combine(folder, "comparison.json"), ticks, start, end,
            a[0].GetProperty("fields").GetProperty("world.time").GetDouble(),
            b[b.GetArrayLength() - 1].GetProperty("fields").GetProperty("world.time").GetDouble(),
            a[a.GetArrayLength() - 1].GetProperty("fields").GetProperty("world.time").GetDouble(),
            b[0].GetProperty("fields").GetProperty("world.time").GetDouble(), measures);
        RuleProject.WriteAtomic(result.OutputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }
    private static async Task<string> RunOne(RuleProject project, string projectFile, string name, string save, string folder,
        int ticks, CancellationToken cancellation)
    {
        var runtime = project.Algorithms != null ? await AlgorithmBuild.CompileAsync(project.Algorithms, cancellation)
            : new AlgorithmBuildResult(AlgorithmBuild.CurrentRevision, typeof(GameWorld).Assembly.Location, "");
        string output = Path.Combine(folder, name + ".trace.json");
        using var process = AlgorithmBuild.StartRuntime(runtime, new[] { "--run-rule-project", projectFile,
            ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), output, "--world", save, "--migrate" }, projectFile);
        try {
            await process.WaitForExitAsync(cancellation);
            if (process.ExitCode != 0) throw new InvalidDataException($"A {name} próba sikertelen (kód: {process.ExitCode}). Eredménykönyvtár: {folder}");
            return output;
        } finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
