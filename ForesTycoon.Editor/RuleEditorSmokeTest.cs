using System;
using System.IO;
using System.Linq;
using ImGuiNET;
using ForesTycoon.Rules;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor
{
    internal static class RuleEditorSmokeTest
    {
        internal static void Run()
        {
            const int width = 1360, height = 960;
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new(width, height),
                API = ContextAPI.OpenGL, APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                var settings = TerrainSettings.Default.WithNodeSize(17, 42);
                using var world = new GameWorld(settings); using var restored = new GameWorld(settings);
                for (int id = 0; id < 255 && world.RoadCount < 2; id++) { world.QueueRoadPath(id, id + 1, false, RoadPaving.Macadam); world.ExecutePendingCommands(); }
                var roads = world.CaptureCheckpoint().Terrain.Roads;
                Require(roads.Length > 0, "No road fixture."); int tile = roads[0].TileId;
                var model = RuleModel.Default(); model.Name = "Útkopás ×4"; model.Nodes.Find(n => n.Id == "scale").Value = 4;
                world.QueueRuleModel(model); world.ExecutePendingCommands();
                float before = world.GetRoadCondition(tile); world.ApplyTrafficWear(tile, 0.0015f);
                Require(Math.Abs(before - world.GetRoadCondition(tile) - 0.006f) < 1e-6f, "Edited graph did not change terrain condition.");
                using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0; restored.Load(saved);
                Require(restored.RuleDocument.ToJson() == model.ToJson(), "Checkpoint lost the rule model.");
                Require(restored.GetRoadCondition(tile) == world.GetRoadCondition(tile), "Checkpoint lost road wear.");
                restored.ApplyTrafficWear(tile, 0.0015f); world.ApplyTrafficWear(tile, 0.0015f);
                Require(restored.GetRoadCondition(tile) == world.GetRoadCondition(tile), "Loaded rule binding points at the wrong world.");
                // Pending model changes retain their execution boundary after loading.
                model.Nodes.Find(n => n.Id == "scale").Value = 2;
                world.QueueRuleModel(model); using var pending = new MemoryStream(); world.Save(pending); pending.Position = 0; restored.Load(pending);
                Require(restored.RuleDocument.Nodes.Find(n => n.Id == "scale").Value == 4, "Pending rule applied early.");
                restored.ExecutePendingCommands(); world.ExecutePendingCommands();
                Require(restored.RuleDocument.ToJson() == world.RuleDocument.ToJson(), "Pending rule was lost.");
                // Replay from the command journal alone reconstructs the same rule.
                pending.Position = 0; var data = WorldSaveSerializer.Read(pending);
                using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
                    Tick = data.Tick, TickRate = data.TickRate, Terrain = data.Terrain, ForestYearSeconds = data.ForestYearSeconds,
                    SoilModel = data.SoilModel, Climate = data.Climate, Commands = data.Commands });
                journal.Position = 0; restored.Load(journal);
                Require(restored.RuleDocument.ToJson() == model.ToJson(), "Journal replay lost rule changes.");
                // Exercise the real moving truck callback against the edited terrain rule.
                Require(roads.Length >= 2, "Road fixture needs two tiles.");
                int other = roads[1].TileId;
                world.TryGetTileCenter(tile, out var start); world.TryGetTileCenter(other, out var end);
                var geometry = new VehicleRoadRoute(new[] { start, end }, new OpenTK.Mathematics.Vector2[2]);
                int passes = 0;
                var truck = new Vehicle(999, new[] { tile, other }, 1.5, roadRoute: geometry, roadPhysics: true)
                {
                    RoadState = id => (RoadSurface.Gravel, world.GetRoadCondition(id)),
                    RoadWear = (id, amount) => { passes++; world.ApplyTrafficWear(id, amount); }
                };
                truck.Load(20);
                float otherBefore = world.GetRoadCondition(other);
                for (int tick = 0; tick < 600; tick++) truck.Update(1.0 / 30);
                Require(passes > 0 && truck.FuelUsed > 0 && world.GetRoadCondition(other) < otherBefore,
                    "Moving truck did not execute the edited rule.");
                using var ui = new ImGuiController(); HudTheme.Apply(); var editor = new RuleEditorView(); bool open = true;
                string snapshot = world.CaptureCheckpoint().Rules.ToJson();
                for (int frame = 0; frame < 3; frame++)
                {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); editor.Draw(world, ref open);
                    GL.Viewport(0, 0, width, height); GL.ClearColor(.08f, .1f, .12f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(world.RuleDocument.ToJson() == snapshot, "Drawing changed active rules.");
                Require(GL.GetError() == ErrorCode.NoError, "Editor OpenGL error.");
                string output = Path.GetFullPath("artifacts/rule-editor"); Directory.CreateDirectory(output);
                FramebufferCapture.SavePng(Path.Combine(output, "editor.png"), width, height);
                File.WriteAllText(Path.Combine(output, "road-wear.json"), model.ToJson());
                var graphEditor = new RuleEditorView(Path.Combine(output, "road-wear.json"));
                for (int frame = 0; frame < 3; frame++)
                {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    graphEditor.Draw(world, ref open);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Cached graph editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "road-graph.png"), width, height);
                var current = world.DescribeCurrentRules();
                var catalogView = new CurrentRulesView(); catalogView.Load(current);
                catalogView.SetRoadTrafficModel(world, model.Clone());
                catalogView.RoadTrafficModel.Nodes.Find(n => n.Id == "scale").Value = 3;
                Require(GameRuleCatalog.FromJson(current.ToJson()).RoadTrafficModel.Nodes.Find(n => n.Id == "scale").Value == 3,
                    "Graph edits were not retained by the catalog authoring document.");
                catalogView.SetRoadTrafficModel(world, model.Clone());
                string captures = Path.Combine(output, "current-rules"); Directory.CreateDirectory(captures);
                int captureIndex = 0;
                foreach (string group in current.Rules.Select(r => r.Module).Distinct().Prepend("").Append("gaps"))
                {
                    catalogView.SelectModule(group);
                    for (int frame = 0; frame < 2; frame++)
                    {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                        ImGui.Begin("Jelenlegi szabályrendszer", ImGuiWindowFlags.NoSavedSettings);
                        catalogView.Draw(world, _ => { }); ImGui.End();
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    Require(GL.GetError() == ErrorCode.NoError, "Current rule catalog OpenGL error: " + group);
                    FramebufferCapture.SavePng(Path.Combine(captures, $"{captureIndex++:D2}.png"), width, height);
                }
                File.WriteAllText(Path.Combine(captures, "catalog.json"), current.ToJson());
                Require(world.RuleDocument.ToJson() == snapshot, "Browsing catalog changed executable rules.");
                // Tuning: edit numbers in the catalog, capture the list, apply them as one journaled change, save and reload.
                var fuelRule = current.Rules.Find(r => r.Id == "economy.fuel");
                int diesel = fuelRule.Parameters.FindIndex(p => p.Key == "DieselPrice");
                fuelRule.Parameters[diesel] = fuelRule.Parameters[diesel] with { Value = 0.95 };
                var forwarderRule = current.Rules.Find(r => r.Id == "machine.forwarder");
                int capacity = forwarderRule.Parameters.FindIndex(p => p.Key == "ForwarderCapacity");
                forwarderRule.Parameters[capacity] = forwarderRule.Parameters[capacity] with { Value = 18 };
                for (int frame = 0; frame < 2; frame++)
                {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Hangolás", ImGuiWindowFlags.NoSavedSettings);
                    catalogView.DrawTuning(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Tuning list OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "tuning.png"), width, height);
                Require(catalogView.UnappliedTuning(world) == 2, "Tuning edits were not tracked.");
                world.QueueTuning(catalogView.TuningOverrides(world));
                Require(world.Tuning[Tune.DieselPrice] == ForestryLogistics.DieselPrice, "Tuning applied before its tick.");
                world.ExecutePendingCommands();
                Require(world.Tuning[Tune.DieselPrice] == 0.95 && world.Tuning.F(Tune.ForwarderCapacity) == 18f, "Tuning was not applied.");
                Require(catalogView.UnappliedTuning(world) == 0, "Applied tuning still reported as pending.");
                using var tunedSave = new MemoryStream(); world.Save(tunedSave); tunedSave.Position = 0; restored.Load(tunedSave);
                Require(restored.Tuning.ToJson() == world.Tuning.ToJson(), "Save lost the tuning.");
                var behaviorView = new BehaviorEditorView();
                var behavior = new ForesTycoon.Rules.BehaviorModel();
                behavior.Graphs.Add(new() { Id = "mill-example", Name = "Malom feldolgozása", Hook = "mill.process", Kind = "mill" });
                behavior.Graphs[0].Nodes.Add(new() { Id = "scale", Name = "Termelési szorzó", Operation = BehaviorOperation.Constant, Value = 2, X = 30, Y = 190 });
                behavior.Graphs[0].Nodes.Add(new() { Id = "result", Name = "Feldolgozott térfogat", Operation = BehaviorOperation.Multiply, A = "native", B = "scale", X = 300, Y = 120 });
                behavior.Graphs[0].Output = "result";
                behaviorView.Load(behavior);
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Viselkedések", ImGuiWindowFlags.NoSavedSettings); behaviorView.Draw(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Behavior editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "behaviors.png"), width, height);
                var controller = BehaviorController.TimedPause("forwarder");
                controller.States[1].Transitions.Add(new() { Target = "wait", Conditions = new() { new() { Trigger = BehaviorTrigger.Elapsed, Value = 20 } } });
                behaviorView.Load(new() { Controllers = new() { controller } });
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Gépvezérlés", ImGuiWindowFlags.NoSavedSettings); behaviorView.Draw(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Controller editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "controllers.png"), width, height);
                var transport = new BehaviorModel { Controllers = new() { BehaviorController.TransportCycle("truck"), BehaviorController.TransportCycle("forwarder") } };
                File.WriteAllText(Path.Combine(output, "transport-behaviors.json"), transport.ToJson());
                behaviorView.Load(transport);
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Szállítási gráf", ImGuiWindowFlags.NoSavedSettings); behaviorView.Draw(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Truck controller editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "truck-controller.png"), width, height);
                var route = new BehaviorGraph { Id = "route-example", Hook = "route.cost", Kind = "truck", Name = "Sérült utak büntetése" };
                route.Nodes.Add(new() { Id = "damage", Name = "Útsérültség", Operation = BehaviorOperation.State, X = 30, Y = 190 });
                route.Nodes.Add(new() { Id = "cost", Name = "Költség + sérültség", Operation = BehaviorOperation.Add, A = "native", B = "damage", X = 300, Y = 120 }); route.Output = "cost";
                transport.Graphs.Add(route); File.WriteAllText(Path.Combine(output, "transport-behaviors.json"), transport.ToJson());
                behaviorView.Load(transport);
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Útvonalszabály", ImGuiWindowFlags.NoSavedSettings); behaviorView.Draw(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Route graph editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "route-policy.png"), width, height);
                Console.WriteLine($"Rule editor smoke passed: terrain binding, snapshot, pending commands, journal replay, UI and behavior editor. Capture: {output}");
            }
            finally { RenderDevice.Dispose(); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
