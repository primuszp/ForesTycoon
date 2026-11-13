using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon;

/// <summary>Headless long-run verification of the complete world, including deterministic sliced execution and reloads.</summary>
internal static class SimulationSoak
{
    internal static void Run(int months, int side, int seed, string directory)
    {
        if (months < 1 || months > 120 || side < 16 || side > 64) throw new ArgumentOutOfRangeException(nameof(months));
        Directory.CreateDirectory(directory);
        using var whole = Create(side, seed);
        using var sliced = Create(side, seed);
        Require(whole.SceneProblems.Count == 0, string.Join("; ", whole.SceneProblems));
        Require(whole.Forest.IndividualTrees.TreeCount > 0, "fixture must start with a living forest");
        const double step = 1d / 30;
        int ticksPerMonth = (int)Math.Round(EcologyTime.DefaultGameSecondsPerYear * 30 / 12);
        var times = new double[ticksPerMonth];
        int[] budgets = { 1, 7, 64, 257 };
        var retired = new System.Collections.Generic.List<WeakReference>();
        var watch = Stopwatch.StartNew();
        long allocationStart = GC.GetTotalAllocatedBytes();
        using var report = new StreamWriter(Path.Combine(directory, "months.jsonl")) { AutoFlush = true };
        Console.WriteLine($"soak: {side}x{side}, seed={seed}, months={months}, ticks/month={ticksPerMonth}, runtime={Environment.Version}");
        for (int month = 0; month < months; month++) {
            Edit(whole, month, side); Edit(sliced, month, side);
            whole.ExecutePendingCommands();
            var commandTimeout = Stopwatch.StartNew();
            while (sliced.ForegroundPending) {
                sliced.ContinueCommands(17);
                Require(commandTimeout.Elapsed.TotalSeconds < 60, $"command stalled at month {month}");
            }
            for (int tick = 0; tick < ticksPerMonth; tick++) {
                long start = Stopwatch.GetTimestamp();
                whole.Update(step);
                long slicedStart = Stopwatch.GetTimestamp();
                int budget = budgets[(tick / 113) % budgets.Length];
                while (!sliced.ContinueUpdate(step, budget))
                    Require(Stopwatch.GetElapsedTime(slicedStart).TotalSeconds < 30, $"tick stalled at {sliced.SimulationTick}");
                times[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            var expected = JsonSerializer.Serialize(whole.CaptureCheckpoint());
            var actual = JsonSerializer.Serialize(sliced.CaptureCheckpoint());
            Compare(expected, actual, directory, month, "slicing");
            Require(double.IsFinite(whole.Balance) && double.IsFinite(whole.Environment.StoredWater), "nonfinite world totals");
            foreach (var stack in whole.Logistics.Stacks)
                Require(float.IsFinite(stack.Volume) && stack.Volume >= 0 && double.IsFinite(stack.Value) && stack.Value >= 0, "invalid timber stock");
            foreach (var vehicle in whole.VehicleState.Vehicles)
                Require(double.IsFinite(vehicle.Distance) && vehicle.Distance >= 0 && double.IsFinite(vehicle.FuelUsed) && vehicle.FuelUsed >= 0, "invalid vehicle state");
            // Reload only one branch: any lost state becomes visible both immediately and during subsequent months.
            long saveBytes;
            using (var save = new MemoryStream()) {
                sliced.Save(save); saveBytes = save.Length; save.Position = 0;
                try { MemoryPerformanceProbe.Reload(sliced, save, retired); }
                catch {
                    File.WriteAllBytes(Path.Combine(directory, $"failure-{month}-save.json"), save.ToArray());
                    throw;
                }
            }
            Compare(expected, JsonSerializer.Serialize(sliced.CaptureCheckpoint()), directory, month, "reload");
            Array.Sort(times);
            MemoryPerformanceProbe.Collect();
            int retiredAlive = retired.Count(r => r.IsAlive);
            Require(retiredAlive == 0, "Retired world objects remain rooted after reload");
            long heap = GC.GetTotalMemory(forceFullCollection: true);
            var row = new { month = month + 1, tick = whole.SimulationTick, forestYear = whole.Forest.ForestYear,
                elapsedSeconds = watch.Elapsed.TotalSeconds, managedHeapBytes = heap,
                allocatedBytes = GC.GetTotalAllocatedBytes() - allocationStart,
                pairedTickP99Ms = times[(int)(times.Length * .99)], pairedTickMaxMs = times[^1],
                trees = whole.Forest.IndividualTrees.TreeCount, balance = whole.Balance,
                delivered = whole.Logistics.Mills.Sum(m => (double)m.Received),
                checkpointCharacters = expected.Length, saveBytes, retiredAlive, retiredTracked = retired.Count, exact = true };
            report.WriteLine(JsonSerializer.Serialize(row));
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        Require(whole.SimulationTick == (ulong)(months * ticksPerMonth), "lost ticks");
        Require(whole.Logistics.Mills.Sum(m => m.Received) > 0, "fixture did not deliver timber to a mill");
        Require(whole.Logistics.Machines.All(m => m.FuelUsed > 0), "fixture did not exercise both machines");
        Console.WriteLine("Simulation soak passed: exact whole/sliced/reloaded state at every month boundary.");
    }

    internal static SimulationWorld Create(int side, int seed)
    {
        var project = SimulationWorld.DefaultRuleProject(); project.Algorithms = null;
        project.World = SimulationWorld.DefaultScenario(seed, side);
        for (int node = 0; node < (side + 1) * (side + 1); node++) project.World.HeightPaint[node] = 4;
        for (int x = 0; x < side; x++) for (int y = 0; y < side; y++)
            project.World.ForestPaint[x * side + y] = y < 8 ? "" : (x % 2 == 0 ? "Oak" : "Pine");
        project.World.Roads.Add(new() { From = 2 * side + 2, To = (side - 3) * side + 2, Surface = "Macadam" });
        var world = WorldLoader.CreateProject(project);
        try {
            foreach (bool depot in new[] { true, false }) {
                int site = Enumerable.Range(0, side * side).First(t => world.Map.TryGetSawmillFootprint(t, out var footprint, out _)
                    && footprint.All(id => !world.Forest.HasPhysicalObjects(id)) && world.Map.FindRoadDocks(footprint).Count > 0);
                if (depot) world.QueuePlaceDepot(site); else world.QueuePlaceSawmill(site);
                world.ExecutePendingCommands();
            }
            Require(world.Logistics.Depots.Count == 1 && world.Logistics.Mills.Count == 1, world.Logistics.Status);
            return world;
        } catch { world.Dispose(); throw; }
    }

    internal static void Edit(SimulationWorld w, int month, int side)
    {
        w.QueueWeather(month % 2 == 0 ? WeatherPreset.Storm : WeatherPreset.Sunny, month % 2 == 0 ? 20 : 0, 60);
        if (month == 0) {
            int source = Enumerable.Range(0, side * side).Last(t => w.Logistics.CanPlaceStack(t)
                && w.Map.FindNetworkDocks(new[] { t }).Count > 0);
            w.QueueStackSite(source, false); w.ExecutePendingCommands();
            var stack = w.Logistics.StackAt(source);
            Require(stack != null, "missing source stack");
            stack.Add(40, 4000);
            var truck = w.Logistics.Trucks.Single();
            w.QueueSendVehicle(truck.Id, source, w.Logistics.Mills.Single().TileId, true);
            w.QueueSkidTrailPath(8 * side + 2, 8 * side + 7, false);
            int forestStack = 9 * side + 7;
            w.QueueStackSite(forestStack, false); w.ExecutePendingCommands();
            Require(w.Logistics.StackAt(forestStack) != null, "missing forest stack");
            w.Logistics.StackAt(forestStack).Add(10, 1000);
            w.QueueHarvestForestArea(8 * side + 8, 9 * side + 9);
            foreach (var machine in w.Logistics.Machines)
                w.QueueSendVehicle(machine.Id, forestStack, machine.Kind == ForestMachineKind.Forwarder ? source : -1, false);
        }
        if (month > 0) {
            // Controlled external supply keeps transfers under load after the initial harvest is exhausted.
            // Both branches receive exactly the same input before their next tick; this is not a production economy rule.
            int forestStack = 9 * side + 7;
            var roadStack = w.Logistics.Stacks.Single(s => s.Tile != forestStack);
            w.Logistics.StackAt(forestStack).Add(10, 1000);
            var truck = w.Logistics.Trucks.Single();
            w.QueueSendVehicle(truck.Id, roadStack.Tile, w.Logistics.Mills.Single().TileId, true);
            var forwarder = w.Logistics.Machines.Single(m => m.Kind == ForestMachineKind.Forwarder);
            w.QueueSendVehicle(forwarder.Id, forestStack, roadStack.Tile, false);
        }
        if (month % 3 == 1) w.QueueRoadRepair(2 * side + 2, (side - 3) * side + 2);
        if (month % 3 == 2) w.QueuePlantForestArea(side + 8, side + 10, ForestSpecies.Birch);
        if (month % 6 == 3) {
            var p = w.RuleProjectDocument;
            p.Name = "Soak month " + month;
            w.QueueRuleProject(p);
        }
    }

    private static void Compare(string expected, string actual, string directory, int month, string phase)
    {
        if (expected == actual) return;
        File.WriteAllText(Path.Combine(directory, $"failure-{month}-{phase}-expected.json"), expected);
        File.WriteAllText(Path.Combine(directory, $"failure-{month}-{phase}-actual.json"), actual);
        throw new InvalidOperationException($"World states diverged at month {month + 1}: {phase}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
