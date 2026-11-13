using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ForesTycoon;

internal static class MemoryPerformanceProbe
{
    internal static void Run(string directory, int reloadCycles = 40)
    {
        if (reloadCycles < 0 || reloadCycles > 1000) throw new ArgumentOutOfRangeException(nameof(reloadCycles));
        Directory.CreateDirectory(directory);
        using var report = new StreamWriter(Path.Combine(directory, "report.jsonl")) { AutoFlush = true };
        void Write(object row) { string json = JsonSerializer.Serialize(row); Console.WriteLine(json); report.WriteLine(json); }
        var lifetimes = new List<WeakReference>();
        RunWorld(16, 42, Write, lifetimes, reloadCycles);
        Collect();
        int retained = lifetimes.Count(r => r.IsAlive);
        Write(new { stage = "disposed", tracked = lifetimes.Count, retained, heap = GC.GetTotalMemory(true) });
        if (retained != 0) throw new InvalidOperationException($"{retained} retired world objects remain rooted");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunWorld(int side, int seed, Action<object> write, List<WeakReference> lifetimes, int reloadCycles)
    {
        using var world = SimulationSoak.Create(side, seed);
        SimulationSoak.Edit(world, 0, side); world.ExecutePendingCommands();
        for (int tick = 0; tick < 300; tick++) world.Update(1d / 30);
        world.ProfileUpdates = true;
        for (int pass = 0; pass < 3; pass++) {
            Collect();
            long allocationStart = GC.GetTotalAllocatedBytes(true), start = Stopwatch.GetTimestamp();
            double ecology = 0, logistics = 0, wildlife = 0, other = 0;
            var times = new double[3000];
            for (int tick = 0; tick < times.Length; tick++) {
                long tickStart = Stopwatch.GetTimestamp(); world.Update(1d / 30);
                times[tick] = Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
                var p = world.LastUpdateProfile; ecology += p.EnvironmentForestMs; logistics += p.LogisticsMs;
                wildlife += p.WildlifeMs; other += p.OtherSystemsMs;
            }
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(true) - allocationStart;
            Array.Sort(times);
            write(new { stage = "ticks", pass, ticks = times.Length, elapsed, allocated,
                p50 = times[times.Length / 2], p99 = times[(int)(times.Length * .99)], max = times[^1], ecology, logistics, wildlife, other });
        }
        using var save = new MemoryStream(); world.Save(save);
        string expected = JsonSerializer.Serialize(world.CaptureCheckpoint());
        write(new { stage = "state", hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected))) });
        var retired = new List<WeakReference>();
        for (int cycle = 0; cycle < reloadCycles; cycle++) {
            Reload(world, save, retired);
            if (JsonSerializer.Serialize(world.CaptureCheckpoint()) != expected) throw new InvalidOperationException("Reload changed state");
            if (cycle % 5 == 4) {
                Collect();
                write(new { stage = "reload", cycle = cycle + 1, saveBytes = save.Length,
                    retiredAlive = retired.Count(r => r.IsAlive), tracked = retired.Count, heap = GC.GetTotalMemory(true) });
            }
        }
        lifetimes.AddRange(retired); lifetimes.Add(new(world));
        lifetimes.Add(new(world.Map)); lifetimes.Add(new(world.Forest)); lifetimes.Add(new(world.RuleBook));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Reload(SimulationWorld world, MemoryStream save, List<WeakReference> retired)
    {
        retired.Add(new(world.Map)); retired.Add(new(world.Forest)); retired.Add(new(world.Logistics)); retired.Add(new(world.RuleBook));
        retired.Add(new(world.WithSpecies(ForestSpeciesBook.CaptureContext)));
        save.Position = 0; world.Load(save);
    }

    internal static void Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }
}
