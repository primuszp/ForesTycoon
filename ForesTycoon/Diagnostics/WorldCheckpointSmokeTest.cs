using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class WorldCheckpointSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new(800, 600),
                API = ContextAPI.OpenGL, APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                var settings = TerrainSettings.Default.WithNodeSize(17, 42);
                using var original = new GameWorld(settings, climate: ClimateDefinition.Legacy); using var restored = new GameWorld(settings);
                original.QueueWeather(WeatherPreset.Storm, 32, 20); original.ExecutePendingCommands();
                int source = 0; while (source < 256 && !original.TryGetForestStand(source, out _)) source++;
                Require(source < 256, "No forest fixture.");
                original.QueueHarvestForest(source); original.ExecutePendingCommands();
                for (int id = 0; id < 256 && original.Logistics.Mills.Count == 0; id++) {
                    original.QueuePlaceSawmill(id); original.ExecutePendingCommands();
                }
                Require(original.Logistics.Mills.Count == 1, "No sawmill fixture.");
                for (int i = 0; i < 901; i++) original.Update(1.0 / 30);
                using var saved = new MemoryStream(); original.Save(saved); saved.Position = 0;
                var data = WorldSaveSerializer.Read(saved);
                Require(data.Checkpoint != null && data.Version == WorldSaveData.CurrentVersion, "Missing checkpoint.");
                saved.Position = 0; long start = Stopwatch.GetTimestamp(); restored.Load(saved);
                double fastMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Require(restored.LastLoadReplayedTicks == 0, "Checkpoint replayed historical ticks.");
                Equal(original, restored, "initial restore");
                // Legacy v7 still replays the same log with the pinned soil model.
                using var journal = new MemoryStream();
                WorldSaveSerializer.Write(journal, new WorldSaveData { Version = 7, Tick = data.Tick, TickRate = data.TickRate,
                    Terrain = data.Terrain, ForestYearSeconds = data.ForestYearSeconds, SoilModel = data.SoilModel, Commands = data.Commands });
                journal.Position = 0; start = Stopwatch.GetTimestamp(); restored.Load(journal);
                double replayMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Require(restored.LastLoadReplayedTicks == data.Tick, "Legacy replay skipped ticks.");
                Equal(original, restored, "legacy versus checkpoint");
                // A real v8 snapshot has neither climate configuration nor a regional rain ledger.
                var v8 = System.Text.Json.Nodes.JsonNode.Parse(saved.ToArray());
                v8["version"] = 8; v8.AsObject().Remove("climate");
                v8["checkpoint"]["ecology"].AsObject().Remove("climate");
                v8["checkpoint"]["ecology"]["environment"].AsObject().Remove("rainReceived");
                using var historical = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(v8.ToJsonString()));
                restored.Load(historical);
                Equal(original, restored, "v8 snapshot migration");
                // A snapshot followed by a command/tick tail must advance only that suffix.
                for (int i = 0; i < 31; i++) { original.Update(1.0 / 30); restored.Update(1.0 / 30); }
                original.QueueWeather(WeatherPreset.Sunny, 0, 20); original.ExecutePendingCommands();
                restored.QueueWeather(WeatherPreset.Sunny, 0, 20); restored.ExecutePendingCommands();
                using var after = new MemoryStream(); original.Save(after); after.Position = 0; var tail = WorldSaveSerializer.Read(after);
                using var tailFile = new MemoryStream(); WorldSaveSerializer.Write(tailFile, new WorldSaveData {
                    Tick = tail.Tick, TickRate = tail.TickRate, Terrain = tail.Terrain, ForestYearSeconds = tail.ForestYearSeconds,
                    SoilModel = tail.SoilModel, Climate = tail.Climate, Commands = tail.Commands, Checkpoint = data.Checkpoint
                });
                tailFile.Position = 0; restored.Load(tailFile);
                Require(restored.LastLoadReplayedTicks == 31, "Checkpoint tail replayed the wrong tick count.");
                Equal(original, restored, "tail replay");
                // Persist pending input as pending, never apply it during terminal restoration.
                original.QueueWeather(WeatherPreset.Rain, 12, 20);
                using var pending = new MemoryStream(); original.Save(pending); pending.Position = 0; restored.Load(pending);
                Equal(original, restored, "pending input");
                Require(original.ExecutePendingCommands() == restored.ExecutePendingCommands(), "Pending command count changed.");
                for (int i = 0; i < 3000; i++) { original.Update(1.0 / 30); restored.Update(1.0 / 30); }
                Equal(original, restored, "future RNG/months");
                // Failed restore must preserve the live world AND its queued input.
                restored.QueueWeather(WeatherPreset.Storm, 24, 30);
                string before = State(restored); var broken = original.CaptureCheckpoint(); broken.Ecology.Environment.Fields[0][0] = -1;
                using var invalid = new MemoryStream(); WorldSaveSerializer.Write(invalid, new WorldSaveData {
                    Tick = broken.Tick, Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate, ForestYearSeconds = data.ForestYearSeconds,
                    Commands = tail.Commands, Checkpoint = broken with { CommandCursor = tail.Commands.Count, PendingCommands = 0 }
                });
                invalid.Position = 0; bool rejected = false;
                try { restored.Load(invalid); } catch (InvalidDataException) { rejected = true; }
                Require(rejected && State(restored) == before && restored.ExecutePendingCommands() == 1, "Failed load changed the running world.");
                CheckRegionalContinuation(settings);
                Require(GL.GetError() == ErrorCode.NoError, "Checkpoint GPU resource error.");
                Console.WriteLine($"World checkpoint smoke passed: exact state, v7 migration, 31-tick tail, queued input, future RNG/months and atomic rejection. Load: checkpoint {fastMs:0.0} ms, 901-tick replay {replayMs:0.0} ms; {saved.Length / 1024:0} KiB.");
            }
            finally { RenderDevice.Dispose(); }
        }
        private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
        private static void CheckRegionalContinuation(TerrainSettings settings)
        {
            var climate = new ClimateDefinition(1, 12, 3, .25, 5, .08);
            using var original = new GameWorld(settings, climate: climate);
            using var clone = new GameWorld(settings);
            original.QueueWeather(WeatherPreset.Storm, 32, 20); original.ExecutePendingCommands();
            for (int i = 0; i < 917; i++) original.Update(1.0 / 30);
            using var saved = new MemoryStream(); original.Save(saved); saved.Position = 0; clone.Load(saved);
            Require(clone.Environment.Climate.Definition == climate, "Regional climate configuration was replaced.");
            Equal(original, clone, "regional snapshot");
            for (int i = 0; i < 3000; i++) { original.Update(1.0 / 30); clone.Update(1.0 / 30); }
            Equal(original, clone, "regional future");
            Require(Math.Abs(original.Environment.BalanceError) < 1e-5, "Regional water balance failed.");
            Console.WriteLine("Regional world checkpoint: pinned custom climate and exact future state passed.");
        }
        private static void Equal(GameWorld a, GameWorld b, string stage)
        {
            string expected = State(a), actual = State(b);
            if (expected == actual) return;
            int index = 0; while (index < Math.Min(expected.Length, actual.Length) && expected[index] == actual[index]) index++;
            int start = Math.Max(0, index - 100);
            throw new InvalidOperationException($"State changed at {stage}, offset {index}. Expected: {expected.Substring(start, Math.Min(240, expected.Length - start))}; actual: {actual.Substring(start, Math.Min(240, actual.Length - start))}");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
