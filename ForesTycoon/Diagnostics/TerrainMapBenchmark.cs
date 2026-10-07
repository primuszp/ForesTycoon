using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ForesTycoon
{
    // Headless startup measurement; fingerprint includes heights and hydrology.
    internal static class TerrainMapBenchmark
    {
        internal static void Run()
        {
            foreach (int side in new[] { 32, 64, 128 })
            {
                var settings = TerrainSettings.Default.WithNodeSize(side + 1, 42);
                string reference = Fingerprint(new TerrainMap(settings));
                var times = new double[7];
                var allocations = new long[7];
                for (int run = -2; run < times.Length; run++)
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long started = Stopwatch.GetTimestamp();
                    var map = new TerrainMap(settings);
                    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    if (Fingerprint(map) != reference)
                        throw new InvalidOperationException("Terrain generation is not deterministic.");
                    if (run >= 0) { times[run] = elapsed; allocations[run] = bytes; }
                }
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine($"Map {side}x{side}: median {times[3]:F2} ms, max {times[^1]:F2} ms, allocated {allocations[3] / 1048576.0:F2} MiB; SHA256 {reference}");
            }
        }

        internal static string Fingerprint(TerrainMap map)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> value = stackalloc byte[12];
            foreach (var node in map.Nodes)
            {
                BinaryPrimitives.WriteInt32LittleEndian(value, node.W);
                BinaryPrimitives.WriteSingleLittleEndian(value[4..], node.zPos);
                BinaryPrimitives.WriteSingleLittleEndian(value[8..], map.Hydrology.NodeWaterDepth[node.Id]);
                hash.AppendData(value);
            }
            foreach (var tile in map.Tiles)
            {
                BinaryPrimitives.WriteInt32LittleEndian(value, tile.LowPos);
                BinaryPrimitives.WriteSingleLittleEndian(value[4..], map.Hydrology.TileMoisture[tile.Id]);
                BinaryPrimitives.WriteInt32LittleEndian(value[8..], int.Parse(tile.Code));
                hash.AppendData(value);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
