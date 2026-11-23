using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon;

sealed partial class Viewport
{
    private readonly string frameProfilePath;
    private ViewportFrameProfile frameProfile;
}

/// <summary>Opt-in completed-frame measurements from the real game window.</summary>
internal sealed class ViewportFrameProfile(string path, int width, int height, string scene) : IDisposable
{
    private readonly RenderPassProfiler passes = new();
    private readonly string renderer = GL.GetString(StringName.Renderer);
    private readonly string driver = GL.GetString(StringName.Version);
    private readonly List<Frame> frames = new();
    private long started;
    private double swapMs, completeMs;

    private sealed record Frame(ulong Number, double CompletedMs, double SwapMs, double TotalMs, int SimulationTicks,
        int Draws, int Vertices, int VisibleChunks, int ForestLods, long ForestGpuBytes,
        int Near, int Medium, int Far, RenderPassProfiler.Timing[] Passes);

    internal void BeginFrame() { started = Stopwatch.GetTimestamp(); passes.BeginFrame(); }
    internal void Mark(string name, bool begin) => passes.Mark(name, begin);
    internal void CompleteGpuFrame() { GL.Finish(); completeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    internal void EndPresent(long start) => swapMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    internal void Record(ulong number, GameWorld world, int simulationTicks)
    {
        double total = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        frames.Add(new(number, completeMs, swapMs, total, simulationTicks, RenderMetrics.DrawCalls, RenderMetrics.SubmittedVertices,
            world.VisibleChunkCount, world.ForestResidentLods, world.ForestGpuPayloadBytes,
            world.ReadyVisibleForestChunks(ForestLod.Near), world.ReadyVisibleForestChunks(ForestLod.Medium),
            world.ReadyVisibleForestChunks(ForestLod.Far), passes.ReadCompletedFrame()));
    }

    public void Dispose()
    {
        passes.Dispose();
        if (frames.Count == 0) return;
        var steady = frames.Skip(Math.Min(30, frames.Count / 2)).ToArray();
        static double Percentile(IEnumerable<double> values, double fraction) {
            var ordered = values.Order().ToArray();
            return ordered[(int)Math.Ceiling(ordered.Length * fraction) - 1];
        }
        var report = new {
            renderer, driver, width, height, scene,
            steadyMedianMs = Percentile(steady.Select(f => f.TotalMs), .5),
            steadyP95Ms = Percentile(steady.Select(f => f.TotalMs), .95),
            passMedian = steady.SelectMany(f => f.Passes).GroupBy(p => p.Name).Select(p => new {
                name = p.Key, cpuMs = Percentile(p.Select(x => x.CpuMs), .5), gpuMs = Percentile(p.Select(x => x.GpuMs), .5)
            }).OrderByDescending(p => p.gpuMs), frames
        };
        string destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{renderer}: median {report.steadyMedianMs:F2} ms, p95 {report.steadyP95Ms:F2} ms. {destination}");
    }
}
