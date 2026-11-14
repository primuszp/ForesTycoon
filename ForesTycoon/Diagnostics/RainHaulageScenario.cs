using System;
using System.IO;
using System.Linq;

namespace ForesTycoon;

internal static class RainHaulageScenario
{
    private enum Policy { HaulDuringRain, WaitForRainToEnd, RepairBeforeRain }

    private const int TickRate = 30, RainSeconds = 90, EndSeconds = 240, SampleSeconds = 5;
    private const int RoadStart = 34, RoadEnd = 210, Depot = 67, Landing = 99, Mill = 195;

    internal static void Run(TextWriter output)
    {
        output.WriteLine("policy,simulation_seconds,rain_mm_per_environment_hour,mean_road_condition,roadside_timber_m3,delivered_timber_m3,balance_khuf");
        foreach (var policy in Enum.GetValues<Policy>())
            RunPolicy(policy, output);
    }

    private static void RunPolicy(Policy policy, TextWriter output)
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        using var world = new SimulationWorld(map);
        world.Forest.Clear();
        var logistics = world.Logistics;
        map.BuildRoadTilePath(RoadStart, RoadEnd, RoadPaving.Macadam);
        int[] road = Enumerable.Range(0, map.Tiles.Count).Where(map.IsRoadTile).ToArray();
        if (road.Length == 0) throw new InvalidOperationException("The scenario road could not be built.");
        foreach (int tile in road)
            map.SetRoadCondition(tile, 0.65f);

        Require(logistics.PlaceMill(Mill), logistics.Status);
        Require(logistics.PlaceDepot(Depot), logistics.Status);
        Require(logistics.PlaceStack(Landing), logistics.Status);
        var stack = logistics.StackAt(Landing);
        stack.Add(48, 48 * ForestryLogistics.BuiltinTimberPrice(ForestSpecies.Oak));
        var truck = logistics.Trucks.Single();
        var mill = logistics.Mills.Single();

        world.QueueWeather(WeatherPreset.Rain, 32, RainSeconds);
        if (policy == Policy.RepairBeforeRain)
            world.QueueRoadRepair(RoadStart, RoadEnd);
        world.ExecutePendingCommands();

        for (int second = 0; second <= EndSeconds; second++)
        {
            if (second == RainSeconds)
            {
                world.QueueWeather(WeatherPreset.Sunny, 0, EndSeconds);
                world.ExecutePendingCommands();
            }

            if (second == (policy == Policy.WaitForRainToEnd ? RainSeconds : 0))
                Require(logistics.AssignTruck(truck, stack, Mill), logistics.Status);

            if (second % SampleSeconds == 0)
            {
                double condition = road.Average(tile => (double)map.GetRoadCondition(tile));
                output.WriteLine(FormattableString.Invariant(
                    $"{policy},{second},{world.Environment.LiquidRainRate:F4},{condition:F4},{stack.Volume:F4},{mill.Received:F4},{world.Balance:F4}"));
            }

            if (second == EndSeconds) break;
            for (int tick = 0; tick < TickRate; tick++)
                world.Update(1.0 / TickRate);
        }
    }

    private static void Require(bool success, string status)
    {
        if (!success) throw new InvalidOperationException(status);
    }
}
