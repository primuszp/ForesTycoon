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
                var trafficProject = world.RuleProjectDocument;
                var traffic = trafficProject.Rules.Single(r => r.Id == "traffic.wear");
                foreach (var term in traffic.Effects[0].Influence.Terms) term.Amount *= 4;
                world.QueueRuleProject(trafficProject); world.ExecutePendingCommands();
                float before = world.GetRoadCondition(tile);
                world.QueuePassage(new(tile, TrafficVehicleKind.Truck, 1, 36000, 1)); world.ExecutePendingCommands();
                Require(Math.Abs(before - world.GetRoadCondition(tile) - .006f) < 1e-6f, "Edited passage graph did not change road condition.");
                using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0; restored.Load(saved);
                Require(restored.GetRoadCondition(tile) == world.GetRoadCondition(tile), "Checkpoint lost road wear.");
                foreach (var w in new[] { restored, world }) { w.QueuePassage(new(tile, TrafficVehicleKind.Truck, 1, 36000, 1)); w.ExecutePendingCommands(); }
                Require(restored.GetRoadCondition(tile) == world.GetRoadCondition(tile), "Loaded passage binding points at the wrong world.");
                traffic.Effects[0].Influence.Terms[1].Amount = .003;
                world.QueueRuleProject(trafficProject);
                using var pending = new MemoryStream(); world.Save(pending); pending.Position = 0; restored.Load(pending);
                Require(restored.RuleBook.Rules.Single(r => r.Id == "traffic.wear").Effects[0].Influence.Terms[1].Amount == .006, "Pending rule applied early.");
                restored.ExecutePendingCommands(); world.ExecutePendingCommands();
                Require(restored.RuleProjectDocument.ToJson() == world.RuleProjectDocument.ToJson(), "Pending project was lost.");
                pending.Position = 0; var data = WorldSaveSerializer.Read(pending);
                using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
                    Tick = data.Tick, TickRate = data.TickRate, Terrain = data.Terrain, ForestYearSeconds = data.ForestYearSeconds,
                    SoilModel = data.SoilModel, Climate = data.Climate, Commands = data.Commands });
                journal.Position = 0; restored.Load(journal);
                Require(restored.RuleProjectDocument.ToJson() == world.RuleProjectDocument.ToJson(), "Journal replay lost rule changes.");
                // Exercise the real moving truck callback against the edited terrain rule.
                Require(roads.Length >= 2, "Road fixture needs two tiles.");
                int other = roads[1].TileId;
                world.TryGetTileCenter(tile, out var start); world.TryGetTileCenter(other, out var end);
                var geometry = new VehicleRoadRoute(new[] { start, end }, new OpenTK.Mathematics.Vector2[2]);
                int passes = 0;
                var truck = new Vehicle(999, new[] { tile, other }, 1.5, roadRoute: geometry, roadPhysics: true)
                {
                    RoadState = id => (RoadSurface.Gravel, world.GetRoadCondition(id)),
                    Passage = passage => { passes++; world.RecordPassage(passage); }
                };
                truck.Load(20);
                float otherBefore = world.GetRoadCondition(other);
                for (int tick = 0; tick < 600; tick++) { truck.Update(1.0 / 30); SimulationWork.Complete(world.TrafficWork()); }
                Require(passes > 0 && truck.FuelUsed > 0 && world.GetRoadCondition(other) < otherBefore,
                    "Moving truck did not execute the edited rule.");
                var coverage = EditorCoverage.Check(world, world.BehaviorDocument);
                Require(coverage.Gaps.Count == 0, "The editor does not reach everything the engine runs:\n" + EditorCoverage.Format(coverage));
                using var ui = new ImGuiController(ImGuiController.Authoring); EditorStyle.Apply(); var editor = new RuleEditorView(); bool open = true;
                string snapshot = world.RuleProjectDocument.ToJson();
                for (int frame = 0; frame < 3; frame++)
                {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); editor.Draw(world, ref open);
                    GL.Viewport(0, 0, width, height); GL.ClearColor(.08f, .1f, .12f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(world.RuleProjectDocument.ToJson() == snapshot, "Drawing changed active rules.");
                Require(GL.GetError() == ErrorCode.NoError, "Editor OpenGL error.");
                string output = Path.GetFullPath("artifacts/rule-editor"); Directory.CreateDirectory(output);
                FramebufferCapture.SavePng(Path.Combine(output, "editor.png"), width, height);
                int forestTile = Enumerable.Range(0, world.Map.Tiles.Count).FirstOrDefault(id => world.DescribeTile(id).ContainsKey("forest.trees"));
                var trialProject = editor.CollectProject(world); trialProject.Algorithms = null;
                trialProject.World ??= GameWorld.DefaultScenario(world.Map.Settings.Seed);
                var trialBase = GameWorld.DefaultRuleProject(); trialBase.Algorithms = null; trialBase.World = trialProject.World;
                var rainTrial = RuleEditorView.RunWorldTrial(trialBase, trialProject, 30, 40);
                foreach (var screen in new (Action Show, string Name)[] {
                    (() => editor.ShowWorld(WorldTab.Map, MapLayer.Soil, forestTile), "world-map-soil"),
                    (() => { editor.AddSpring(world, forestTile, 20000, 12 * world.SecondsPerEnvironmentHour); editor.ShowWorld(WorldTab.Map, MapLayer.Water, forestTile); }, "world-map-water"),
                    (() => editor.ShowWorld(WorldTab.Rules, tile: forestTile, rule: "water.infiltration|tile"), "world-rules-water"),
                    (() => editor.ShowWorld(WorldTab.Map, MapLayer.Trafficability, forestTile), "world-map-traffic"),
                    (() => editor.ShowWorld(WorldTab.Map, MapLayer.Elevation, forestTile), "world-map-elevation"),
                    (() => editor.ShowWorld(WorldTab.Map, MapLayer.Forest, forestTile), "world-map-forest"),
                    (() => editor.ShowWorld(WorldTab.Map, MapLayer.Tracks, forestTile), "world-map-structures"),
                    (() => editor.ShowWorld(WorldTab.Types), "world-types"),
                    (() => editor.ShowWorldTypes(TypeTab.Species, 2), "world-types-species"),
                    (() => editor.ShowWorldTypes(TypeTab.Surfaces), "world-types-surfaces"),
                    (() => editor.ShowObject("tree"), "world-objects-tree"),
                    (() => editor.ShowWorld(WorldTab.Rules, tile: forestTile), "world-rules"),
                    (() => editor.ShowMissions(), "world-missions"),
                    (() => editor.ShowRuleBook(), "world-rulebook"),
                    (() => editor.ShowWorldTypes(TypeTab.Vehicles), "world-vehicles"),
                    (() => editor.ShowWorldTypes(TypeTab.Parameters), "world-parameters"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "tree.drought")), "world-rulebook-curve"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "road.weather")), "world-rule-influences"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.stumps")), "world-stump-influences"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "water.spread")), "world-flow"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.health")), "tree-health-graph"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.stress")), "tree-stress-graph"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.mortality")), "tree-mortality-graph"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.deadwood")), "tree-deadwood-graph"),
                    (() => editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.growth")), "tree-growth-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "water.stress").Id), "common-water-response"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.initialSpecies").Id), "gameplay-initial-species"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "economy.sale").Id), "gameplay-sale"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "mission.reached").Id), "gameplay-mission"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "wildlife.destinationScore").Id), "gameplay-wildlife"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.site").Id), "site-fitness-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.initialHeight").Id), "initial-size-height-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.initialDiameter").Id), "initial-size-diameter-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.initialCrown").Id), "initial-size-crown-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "weather.eventType").Id), "environment-weather-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "machine.fetch").Id), "forwarder-fetch-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "machine.felling").Id), "processor-felling-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "mill.process").Id), "mill-processing-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "vehicle.mass").Id), "vehicle-mass-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "vehicle.fuel").Id), "vehicle-fuel-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "upkeep.breakdown").Id), "vehicle-breakdown-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "upkeep.repairCost").Id), "vehicle-repair-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "climate.temperature").Id), "environment-climate-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.cover").Id), "environment-canopy-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "snow.release").Id), "environment-snow-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "trail.trafficability").Id), "environment-trafficability-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.seedSupply").Id), "regeneration-seedSupply-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.seedSelection").Id), "regeneration-seedSelection-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.germination").Id), "regeneration-germination-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.seedCrowding").Id), "regeneration-seedCrowding-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.seedlingAge").Id), "regeneration-seedlingAge-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.seedlingCount").Id), "regeneration-seedlingCount-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.seedlingHealth").Id), "regeneration-seedlingHealth-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.shading").Id), "competition-shading-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.crowding").Id), "competition-crowding-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.light").Id), "competition-light-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.space").Id), "competition-space-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.lightResponse").Id), "competition-response-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "forest.resourceLimit").Id), "competition-resource-limit-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "water.drought").Id), "site-drought-graph"),
                    (() => editor.ShowCommonRule("behavior:" + world.BehaviorDocument.Graphs.First(g => g.Hook == "water.waterlogging").Id), "site-waterlogging-graph"),
                                        (() => editor.ShowRuleCalculation(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "forest.growth"), 0), "common-calculation"),
                    (() => editor.ShowRuleOverview(), "world-rule-overview"),
                    (() => editor.ShowSystemMap(), "world-system-map"),
                    (() => { editor.ShowWorld(WorldTab.Trial); editor.ShowWorldTrial(rainTrial); }, "world-trial") }) {
                    screen.Show();
                    for (int frame = 0; frame < 3; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); editor.Draw(world, ref open);
                        GL.Viewport(0, 0, width, height); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    Require(GL.GetError() == ErrorCode.NoError, "World editor OpenGL error: " + screen.Name);
                    FramebufferCapture.SavePng(Path.Combine(output, screen.Name + ".png"), width, height);
                }
                Require(rainTrial.Summary.Contains("járhatóság"), "World trial produced no result.");
                // Undo/redo: a finished mission edit and a number edit are steps; undo restores both in order, redo replays.
                void Frames(int n) {
                    for (int frame = 0; frame < n; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); editor.Draw(world, ref open);
                        GL.Viewport(0, 0, width, height); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                        ui.Render();
                    }
                }
                editor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "road.weather")); Frames(12);
                int connectionCount = editor.CollectProject(world).Rules.Single(r => r.Id == "road.weather").Effects[0].Influence.Terms.Count;
                void DragLink(string source, string target)
                {
                    var a = editor.InfluenceGraphAnchors[source]; var b = editor.InfluenceGraphAnchors[target];
                    ui.MouseMove((int)a.X, (int)a.Y); Frames(2);
                    ui.MouseButton(0, true); Frames(2);
                    ui.MouseMove((int)b.X, (int)b.Y); Frames(2);
                    ui.MouseButton(0, false); Frames(12); world.ExecutePendingCommands();
                }
                DragLink("source:rainIntensity", "target:condition");
                var graphRule = world.RuleProjectDocument.Rules.Single(r => r.Id == "road.weather");
                Require(graphRule.Effects[0].Influence.Terms.Count == connectionCount + 1, "Dragging source to target did not create and apply a relationship.");
                Require(graphRule.Effects[0].Influence.Terms[^1].Factors.Single().Source == "rainIntensity", "Graph connected the wrong source.");
                DragLink("source:tile.moisture", "effect:0:0");
                graphRule = world.RuleProjectDocument.Rules.Single(r => r.Id == "road.weather");
                Require(graphRule.Effects[0].Influence.Terms[0].Factors.Count == 2, "Dragging source to an effect did not add a multiplier.");
                editor.Undo(world); world.ExecutePendingCommands(); Frames(12);
                editor.Undo(world); world.ExecutePendingCommands(); Frames(12);
                Require(world.RuleProjectDocument.Rules.Single(r => r.Id == "road.weather").Effects[0].Influence.Terms.Count == connectionCount, "Undo did not restore graph relationships.");
                // The rule reads as Hungarian sentences that follow the graph.
                Require(editor.InfluenceSentences.Any(l => l.StartsWith("Minden út esetén az állapot évente", StringComparison.Ordinal)) && editor.InfluenceSentences.Any(l => l.Contains("aszfalt")),
                    "Rule sentences missing or not in designer words: " + string.Join(" | ", editor.InfluenceSentences));
                // Click-to-connect: click a source, then click the target.
                void Click(string anchor) { var p = editor.InfluenceGraphAnchors[anchor]; ui.MouseMove((int)p.X, (int)p.Y); Frames(2); ui.MouseButton(0, true); Frames(2); ui.MouseButton(0, false); Frames(4); }
                Click("source:rainIntensity"); Click("target:condition"); Frames(8); world.ExecutePendingCommands();
                Require(world.RuleProjectDocument.Rules.Single(r => r.Id == "road.weather").Effects[0].Influence.Terms.Count == connectionCount + 1, "Click-to-connect did not create a relationship.");
                Require(editor.InfluenceSentences.Count(l => l.Contains("eső erőssége")) >= 3, "Sentences did not update after connecting.");
                ui.MouseMove(0, 0); editor.Undo(world); world.ExecutePendingCommands(); Frames(12);
                Require(world.RuleProjectDocument.Rules.Single(r => r.Id == "road.weather").Effects[0].Influence.Terms.Count == connectionCount, "Undo did not remove the clicked relationship.");
                Require(RuleEditorView.FieldUses(world.RuleProjectDocument.Rules).Any(u => u.Key == "road.condition" && u.Writers.Count > 0) && RuleEditorView.FieldUses(world.RuleProjectDocument.Rules).Any(u => u.Key == "tile.moisture" && u.Readers.Count > 0),
                    "Overview does not list the road condition rule.");
                // Response cards use a real checkbox and an explicit off-result choice, preserving the authored graph.
                var responseBefore = editor.CollectProject(world).Behaviors.Graphs.Single(g => g.Hook == "forest.shading");
                string responseKey = "behavior:" + responseBefore.Id;
                editor.ShowCommonRule(responseKey); ui.MouseMove(0, 0); Frames(12);
                void CardClick(string key) {
                    var point = editor.RuleCardAnchors[key]; ui.MouseMove((int)point.X, (int)point.Y); Frames(2);
                    ui.MouseButton(0, true); Frames(2); ui.MouseButton(0, false); Frames(12); world.ExecutePendingCommands();
                }
                CardClick("enable:" + responseKey);
                Require(!editor.CollectProject(world).Behaviors.Graphs.Single(g => g.Id == responseBefore.Id).Disabled,
                    "Response was disabled before choosing its replacement result.");
                CardClick("off.cancel");
                Require(!editor.CollectProject(world).Behaviors.Graphs.Single(g => g.Id == responseBefore.Id).Disabled, "Cancelling off changed the response.");
                CardClick("enable:" + responseKey);
                ui.MouseMove(0, 0); Frames(2);
                FramebufferCapture.SavePng(Path.Combine(output, "response-card-off-choice.png"), width, height);
                CardClick("off.confirm");
                ui.MouseMove(0, 0); Frames(2);
                FramebufferCapture.SavePng(Path.Combine(output, "response-card-off.png"), width, height);
                var responseOff = world.BehaviorDocument.Graphs.Single(g => g.Id == responseBefore.Id);
                Require(responseOff.Disabled && responseOff.DisabledValue == 0, "Chosen off result was not applied to the game.");
                Require(System.Text.Json.JsonSerializer.Serialize(responseOff.Nodes) == System.Text.Json.JsonSerializer.Serialize(responseBefore.Nodes), "Off replaced the saved graph.");
                using (var offSave = new MemoryStream()) { world.Save(offSave); offSave.Position = 0; restored.Load(offSave);
                    Require(restored.BehaviorDocument.Graphs.Single(g => g.Id == responseBefore.Id).Disabled, "Response off state was not saved."); }
                CardClick("enable:" + responseKey);
                Require(!world.BehaviorDocument.Graphs.Single(g => g.Id == responseBefore.Id).Disabled, "Re-enabling did not restore the graph.");
                editor.AddResponseCurve("water.stress"); ui.MouseMove(0, 0); Frames(12); world.ExecutePendingCommands();
                var curveResponse = world.BehaviorDocument.Graphs.Single(g => g.Hook == "water.stress");
                Require(curveResponse.Nodes.Single(n => n.Id == curveResponse.Output).Operation == BehaviorOperation.Curve, "Shared curve was not applied to the game.");
                Require(GL.GetError() == ErrorCode.NoError, "Shared graph curve editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "common-response-curve.png"), width, height);
                editor.Undo(world); world.ExecutePendingCommands(); Frames(12);
                Require(!world.BehaviorDocument.Graphs.Single(g => g.Hook == "water.stress").Nodes.Any(n => n.Operation == BehaviorOperation.Curve), "Undo did not restore the original response graph.");
                // Species appearance: the preview is the game's own mesh; an authored form reaches the generator, common and per phase.
                int oakIndex = editor.EditedScenario.Species.FindIndex(x => x.Id == "Oak");
                void PreviewFrames() { for (int i = 0; i < 600 && !editor.TreePreviewReady; i++) { Frames(1); System.Threading.Thread.Sleep(5); } Frames(3); }
                editor.ShowSpeciesAppearance(oakIndex, 4); PreviewFrames();
                Require(editor.TreePreviewReady, "Tree preview was not built.");
                FramebufferCapture.SavePng(Path.Combine(output, "species-appearance.png"), width, height);
                // Scots pine: the high, flat, clumpy crown on a clear bole, and how it grows out of the young cone.
                int pineIndex = editor.EditedScenario.Species.FindIndex(x => x.Id == "Pine");
                editor.ShowSpeciesAppearance(pineIndex, 4); PreviewFrames();
                FramebufferCapture.SavePng(Path.Combine(output, "species-appearance-pine.png"), width, height);
                editor.SetTreePreviewForTest(2, -1, false); PreviewFrames();
                FramebufferCapture.SavePng(Path.Combine(output, "species-life-course-pine.png"), width, height);
                editor.SetTreePreviewForTest(0, -1, false); editor.ShowSpeciesAppearance(oakIndex, 4); PreviewFrames();
                var oak = editor.EditedScenario.Species[oakIndex];
                oak.Shape = new() { All = { ["1Branches"] = 9, ["AttractionUp"] = -1.2 }, Phases = { ["Old"] = new() { ["1DownAngle"] = 95 } } };
                PreviewFrames();
                FramebufferCapture.SavePng(Path.Combine(output, "species-appearance-edited.png"), width, height);
                editor.SetTreePreviewForTest(0, -1, true); PreviewFrames();
                FramebufferCapture.SavePng(Path.Combine(output, "species-appearance-see-through.png"), width, height);
                editor.SetTreePreviewForTest(1, 1, false); PreviewFrames();
                Require(editor.TreePreviewReady, "Branch structure preview was not built.");
                FramebufferCapture.SavePng(Path.Combine(output, "species-appearance-branches.png"), width, height);
                editor.SetTreePreviewForTest(2, -1, false); PreviewFrames();
                Require(editor.TreePreviewReady, "Life course preview was not built.");
                FramebufferCapture.SavePng(Path.Combine(output, "species-life-course.png"), width, height);
                editor.SetTreePreviewForTest(0, -1, false); editor.ShowSpeciesAppearance(oakIndex, 7); PreviewFrames();
                FramebufferCapture.SavePng(Path.Combine(output, "species-appearance-dead.png"), width, height);
                // Colours and bark: an authored autumn colour and bark reach the meshes, the pattern the shader family.
                oak.Colours = new() { Autumn = "#B8321E", Bark = "#D8D6CC", BarkPattern = "birch" };
                editor.ShowSpeciesColoursForTest(oakIndex, 2, 1); PreviewFrames();
                Require(editor.TreePreviewReady, "Colour preview was not built.");
                FramebufferCapture.SavePng(Path.Combine(output, "species-colours-autumn.png"), width, height);
                editor.ShowSpeciesColoursForTest(oakIndex, 1, 9); PreviewFrames();
                FramebufferCapture.SavePng(Path.Combine(output, "species-colours-bark.png"), width, height);
                editor.ApplyEditsForTest(world); world.ExecutePendingCommands(); Frames(4);
                var oakColours = ForesTycoon.TreeModels.TreeForm.Colours(ForestSpecies.Oak);
                Require(oakColours.Autumn.ToArgb() == System.Drawing.Color.FromArgb(0xB8, 0x32, 0x1E).ToArgb() && ForestSpeciesTraits.For(ForestSpecies.Oak).Pattern == 3,
                    "Authored colours did not reach the game.");
                oak.Colours = null; editor.ApplyEditsForTest(world); world.ExecutePendingCommands(); Frames(4);
                Require(ForestSpeciesBook.Look(ForestSpecies.Oak) == null, "Clearing the colours did not restore the built-in look.");
                Require(GL.GetError() == ErrorCode.NoError, "Tree preview OpenGL error.");
                editor.ApplyEditsForTest(world); world.ExecutePendingCommands(); Frames(4);
                Require(ForesTycoon.TreeModels.TreeArchitecture.BaseParameters(ForestSpecies.Oak)["1Branches"] == 9
                    && ForesTycoon.TreeModels.TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Old, 2)["1DownAngle"] == 95
                    && ForesTycoon.TreeModels.TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Mature, 2)["1DownAngle"] != 95,
                    "Authored tree form did not reach the generator.");
                oak.Shape = null; editor.ApplyEditsForTest(world); world.ExecutePendingCommands(); Frames(4);
                Require(ForestSpeciesBook.Shape(ForestSpecies.Oak) == null, "Clearing the form did not restore the built-in tree.");
                editor.ShowMissions(); Frames(12);
                int missions = editor.EditedScenario.Missions.Count;
                editor.EditedScenario.Missions.Add(new MissionGoal { Id = "undo-proba", Title = "Visszavonási próba" }); Frames(12);
                double undoDiesel = world.Tuning[Tune.DieselPrice];
                editor.SetTuningForTest("DieselPrice", undoDiesel + 1); Frames(12);
                Require(editor.CanUndo, "Edits were not recorded for undo.");
                editor.Undo(world); world.ExecutePendingCommands(); Frames(12);
                Require(Math.Abs(world.Tuning[Tune.DieselPrice] - undoDiesel) < 1e-9 && editor.EditedScenario.Missions.Count == missions + 1, "Undo did not restore the number.");
                editor.Undo(world); Frames(12);
                Require(editor.EditedScenario.Missions.Count == missions, "Undo did not remove the mission.");
                editor.Redo(world); editor.Redo(world); world.ExecutePendingCommands(); Frames(12);
                Require(editor.EditedScenario.Missions.Count == missions + 1 && Math.Abs(world.Tuning[Tune.DieselPrice] - undoDiesel - 1) < 1e-9, "Redo did not replay the edits.");
                editor.Undo(world); editor.Undo(world); world.ExecutePendingCommands(); Frames(12);
                string workshopActive = world.RuleProjectDocument.ToJson();
                foreach (string ruleId in new[] { "forest.growth", "water.precipitation", "traffic.wear", "wildlife.initialize", "wildlife.populationTarget" }) {
                    int ruleIndex = world.RuleProjectDocument.Rules.FindIndex(r => r.Id == ruleId);
                    Require(ruleIndex >= 0, "Editor rule fixture is missing: " + ruleId);
                    editor.ShowRuleBook(ruleIndex);
                    for (int frame = 0; frame < 2; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); editor.Draw(world, ref open);
                        GL.Viewport(0, 0, width, height); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    Require(world.RuleProjectDocument.ToJson() == workshopActive, "Browsing rule cards changed the running project.");
                    Require(GL.GetError() == ErrorCode.NoError, "Rule card UI OpenGL error: " + ruleId);
                    FramebufferCapture.SavePng(Path.Combine(output, "rule-card-" + ruleId + ".png"), width, height);
                }
                File.WriteAllText(Path.Combine(output, "road-wear.json"), trafficProject.ToJson());
                using (var trailWorld = new GameWorld(settings, enableRendering: false)) {
                    int trailStart = Enumerable.Range(0, trailWorld.Map.Tiles.Count - 2).First(id => trailWorld.Map.CanCarrySkidTrail(id)
                        && trailWorld.Map.CanCarrySkidTrail(id + 1) && trailWorld.Map.CanCarrySkidTrail(id + 2));
                    trailWorld.QueueSkidTrailPath(trailStart, trailStart + 2, false); trailWorld.ExecutePendingCommands();
                    var trailIds = trailWorld.Map.SkidTrailTiles.OrderBy(id => id).ToArray();
                    foreach (int id in trailIds) trailWorld.Map.SetSkidTrailState(id, .8f, .5f);
                    var trailProject = trailWorld.RuleProjectDocument; trailProject.World = null; trailProject.Algorithms = null;
                    string trailPath = Path.Combine(output, "trail-regeneration.project.json");
                    RuleProject.WriteAtomic(trailPath, trailProject.ToJson());
                    using (var initial = File.Create(Path.Combine(output, "trail-regeneration.world.json"))) trailWorld.Save(initial);
                    var trailEditor = new RuleEditorView(trailPath);
                    void TrailFrames(int count) {
                        for (int frame = 0; frame < count; frame++) {
                            ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                            ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); trailEditor.Draw(trailWorld, ref open);
                            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                        }
                    }
                    TrailFrames(3); trailEditor.ShowRuleBook(trailProject.Rules.FindIndex(r => r.Id == "trail.age")); TrailFrames(12);
                    var removal = trailEditor.InfluenceGraphAnchors["effect:2:0"];
                    ui.MouseMove((int)removal.X, (int)removal.Y); TrailFrames(2);
                    ui.MouseButton(0, true); TrailFrames(2); ui.MouseButton(0, false); ui.MouseMove(0, 0); TrailFrames(12);
                    Require(trailEditor.InfluenceGraphSourceLabels.ContainsKey("idleYears") && trailEditor.InfluenceGraphSourceLabels.ContainsKey("tuning.SkidTrailOvergrowYears"),
                        "Trail graph did not expose both the idle-time condition and the linked overgrowth parameter.");
                    Require(GL.GetError() == ErrorCode.NoError, "Trail graph OpenGL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, "trail-regeneration-graph.png"), width, height);
                    // Connect the existing rut depth as an author-selected regeneration multiplier using a real drag.
                    var a = trailEditor.InfluenceGraphAnchors["source:wear"]; var b = trailEditor.InfluenceGraphAnchors["effect:0:0"];
                    ui.MouseMove((int)a.X, (int)a.Y); TrailFrames(2); ui.MouseButton(0, true); TrailFrames(2);
                    ui.MouseMove((int)b.X, (int)b.Y); TrailFrames(2); ui.MouseButton(0, false); TrailFrames(12); trailWorld.ExecutePendingCommands();
                    var connected = trailWorld.RuleProjectDocument.Rules.Single(r => r.Id == "trail.age").Effects[0].Influence.Terms.Single();
                    Require(connected.Factors.Single().Source == "wear", "Trail drag did not apply its chosen source to the runtime.");
                    trailWorld.RuleBook.Run(trailWorld, .1, 0);
                    Require(Math.Abs(trailWorld.Map.GetSkidTrailWear(trailIds[0]) - .768) < 1e-6, "Runtime did not execute the graph's chosen regeneration multiplier.");
                    ui.MouseMove(0, 0);
                }
                // A reviewable example project, a real four-stump sandbox, and the graph's measured comparison.
                var stumpProject = GameWorld.DefaultRuleProject();
                RuleEngine.AddMissingStarters(stumpProject.Rules, stumpProject.RemovedStarters);
                stumpProject.Name = "Tönklebomlás – nedvesség és átmérő példa";
                stumpProject.World = null; stumpProject.Algorithms = null;
                string stumpProjectPath = Path.Combine(output, "stump-decay-example.project.json");
                RuleProject.WriteAtomic(stumpProjectPath, stumpProject.ToJson());
                using (var stumpWorld = StumpRuleTrial.CreateWorld(stumpProject, "forest.stumps")) {
                    using (var initial = File.Create(Path.Combine(output, "stump-decay-example.world.json"))) stumpWorld.Save(initial);
                    var stumpEditor = new RuleEditorView(stumpProjectPath);
                    for (int frame = 0; frame < 3; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); stumpEditor.Draw(stumpWorld, ref open);
                        ui.Render();
                    }
                    stumpEditor.ResetVisualRule(stumpWorld, "forest.stumps"); stumpWorld.ExecutePendingCommands();
                    var stumpRule = stumpWorld.RuleProjectDocument.Rules.Single(r => r.Id == "forest.stumps");
                    stumpEditor.RunStumpComparison(stumpWorld, stumpRule);
                    var stumpComparison = StumpRuleTrial.Run(stumpProject, "forest.stumps");
                    Require(stumpComparison.Cases.All(c => c.RemovedAtYears.HasValue), "Example did not remove fully decomposed stumps.");
                    File.WriteAllText(Path.Combine(output, "stump-decay-comparison.json"), System.Text.Json.JsonSerializer.Serialize(stumpComparison,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    ui.MouseMove(0, 0);
                    for (int frame = 0; frame < 3; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); stumpEditor.Draw(stumpWorld, ref open);
                        GL.Viewport(0, 0, width, height); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                        ui.Render(); GL.Finish();
                    }
                    Require(stumpEditor.InfluenceGraphSourceLabels["tile.moisture"] == "+ Gyorsítja", "Moisture acceleration was not labeled separately on the graph.");
                    Require(stumpEditor.InfluenceGraphSourceLabels["diameter"] == "- Lassítja", "Diameter slowdown was not labeled separately on the graph.");
                    Require(GL.GetError() == ErrorCode.NoError, "Stump example graph OpenGL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, "stump-decay-example.png"), width, height);
                }
                // Two rules writing the same field: the overview flags it and the rule shows the run order.
                var sharedProject = GameWorld.DefaultRuleProject(); RuleEngine.AddMissingStarters(sharedProject.Rules, sharedProject.RemovedStarters);
                sharedProject.World = null; sharedProject.Algorithms = null;
                var copy = RuleEngine.Clone(new() { sharedProject.Rules.Single(r => r.Id == "road.weather") })[0];
                copy.Id = "road.weather-copy"; copy.Name = "Útromlás – próbamásolat"; copy.Enabled = true;
                sharedProject.Rules.Add(copy);
                Require(RuleEditorView.FieldUses(sharedProject.Rules).Single(u => u.Key == "road.condition").Shared, "Overview did not flag two rules writing road condition.");
                string sharedPath = Path.Combine(output, "shared-writer.project.json");
                RuleProject.WriteAtomic(sharedPath, sharedProject.ToJson());
                var sharedEditor = new RuleEditorView(sharedPath);
                foreach (var screen in new (Action Show, string Name)[] {
                    (() => sharedEditor.ShowRuleOverview(), "world-rule-overview-shared"),
                    (() => sharedEditor.ShowRuleBook(sharedProject.Rules.Count - 1), "world-rule-shared-writer") }) {
                    screen.Show();
                    for (int frame = 0; frame < 3; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); sharedEditor.Draw(world, ref open);
                        GL.Viewport(0, 0, width, height); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    Require(GL.GetError() == ErrorCode.NoError, "Overview OpenGL error: " + screen.Name);
                    FramebufferCapture.SavePng(Path.Combine(output, screen.Name + ".png"), width, height);
                }
                var graphEditor = new RuleEditorView(Path.Combine(output, "road-wear.json"));
                graphEditor.ShowRuleBook(trafficProject.Rules.FindIndex(r => r.Id == "traffic.wear"));
                for (int frame = 0; frame < 3; frame++)
                {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    graphEditor.Draw(world, ref open);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Cached graph editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "road-graph.png"), width, height);
                snapshot = world.RuleProjectDocument.ToJson();
                var current = world.DescribeCurrentRules();
                var catalogView = new CurrentRulesView(); catalogView.Load(current);
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
                Require(world.RuleProjectDocument.ToJson() == snapshot, "Browsing catalog changed executable rules.");
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
                var overrides = WorldBehaviorPolicy.DefaultDocument();
                var millType = overrides.Graphs.Single(g => g.Hook == "mill.process");
                var millLocal = new BehaviorModel { Graphs = new() { millType } }.Clone().Graphs[0];
                millLocal.Id = "local-mill"; millLocal.ObjectId = 7; overrides.Graphs.Add(millLocal);
                behaviorView.Load(overrides); behaviorView.RestoreDefault(millType.Hook, millType.Kind, 7);
                Require(behaviorView.Document.Graphs.Count(g => g.Hook == "mill.process") == 1, "Removing object override duplicated its type graph.");
                _ = new WorldBehaviorPolicy(behaviorView.Document);
                var starter = new BehaviorModel { Graphs = new() { WorldBehaviorPolicy.DefaultGraph(LogisticsRules.Hooks.Single(h => h.Id == "machine.pace" && h.Kind == "logisticsForwarder")) } };
                behaviorView.Load(starter); behaviorView.AddOutputMultiplier();
                var multiplied = behaviorView.Document;
                Require(multiplied.Graphs[0].Nodes.Count == starter.Graphs[0].Nodes.Count + 2, "Starter multiplier did not create an independent connected graph.");
                Require(new CompiledBehaviorModel(multiplied, WorldBehaviorPolicy.Hooks).Evaluate("machine.pace", "logisticsForwarder", 1, new(), LogisticsRules.Hooks.Single(h => h.Id == "machine.pace" && h.Kind == "logisticsForwarder").Inputs.Select(k => k == "step.seconds" ? 1d : k == "machine.pace" ? 2d : 0d).ToArray()) == 2, "Starter multiplier changed the default result.");
                multiplied.Graphs[0].Nodes.Single(n => n.Operation == BehaviorOperation.Constant).Value = 1.1;
                Require(Math.Abs(new CompiledBehaviorModel(multiplied, WorldBehaviorPolicy.Hooks).Evaluate("machine.pace", "logisticsForwarder", 1, new(), LogisticsRules.Hooks.Single(h => h.Id == "machine.pace" && h.Kind == "logisticsForwarder").Inputs.Select(k => k == "step.seconds" ? 1d : k == "machine.pace" ? 2d : 0d).ToArray()) - 2.2) < 1e-10, "Editing starter scale did not change the real graph output.");
                var behavior = new ForesTycoon.Rules.BehaviorModel();
                behavior.Graphs.Add(WorldBehaviorPolicy.DefaultGraph(LogisticsRules.Hooks.Single(h => h.Id == "mill.process")));
                string millOutput = behavior.Graphs[0].Output;
                behavior.Graphs[0].Nodes.Add(new() { Id = "scale", Name = "Termelési szorzó", Operation = BehaviorOperation.Constant, Value = 2, X = 30, Y = 190 });
                behavior.Graphs[0].Nodes.Add(new() { Id = "result", Name = "Feldolgozott térfogat", Operation = BehaviorOperation.Multiply, A = millOutput, B = "scale", X = 300, Y = 120 });
                behavior.Graphs[0].Output = "result";
                behaviorView.Load(behavior);
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Viselkedések", ImGuiWindowFlags.NoSavedSettings); behaviorView.SelectGraph(behavior.Graphs[0].Id); behaviorView.DrawEmbedded(world); ImGui.End();
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
                    ImGui.Begin("Gépvezérlés", ImGuiWindowFlags.NoSavedSettings); behaviorView.SelectControllerById(controller.Id); behaviorView.DrawEmbedded(world); ImGui.End();
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
                    ImGui.Begin("Szállítási gráf", ImGuiWindowFlags.NoSavedSettings); behaviorView.SelectControllerById(transport.Controllers[0].Id); behaviorView.DrawEmbedded(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Truck controller editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "truck-controller.png"), width, height);
                var route = WorldBehaviorPolicy.DefaultGraph(LogisticsRules.Hooks.Single(h => h.Id == "route.cost" && h.Kind == "logisticsTruck"));
                string routeOutput = route.Output;
                route.Nodes.Add(new() { Id = "damage", Name = "Útsérültség", Operation = BehaviorOperation.Input, Key = "route.damage", X = 30, Y = 190 });
                route.Nodes.Add(new() { Id = "cost", Name = "Költség + sérültség", Operation = BehaviorOperation.Add, A = routeOutput, B = "damage", X = 300, Y = 120 }); route.Output = "cost";
                transport.Graphs.Add(route); File.WriteAllText(Path.Combine(output, "transport-behaviors.json"), transport.ToJson());
                behaviorView.Load(transport);
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900));
                    ImGui.Begin("Útvonalszabály", ImGuiWindowFlags.NoSavedSettings); behaviorView.SelectGraph(route.Id); behaviorView.DrawEmbedded(world); ImGui.End();
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Route graph editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "route-policy.png"), width, height);
                // Unified project and stock-flow editor: exercise the actual integration and NamedValue node UI.
                var project = GameWorld.DefaultRuleProject(); project.Name = "Visszacsatolási próba";
                project.Dynamics.Stocks.Add(new() { Id = "wearMultiplier", Name = "Útkopási szorzó", Initial = 1, Maximum = 4 });
                project.Dynamics.Flows.Add(new() { Id = "increase", Name = "Terhelés felhalmozása", To = "wearMultiplier",
                    Rate = new() { Id = "rate", Hook = "flow.increase", Kind = "dynamics", Output = "rate",
                        Nodes = new() { new() { Id = "rate", Name = "Növekedés / s", Operation = BehaviorOperation.Constant, Value = .1 } } } });
                RuleEngine.AddMissingStarters(project.Rules, project.RemovedStarters);
                var roadInfluence = project.Rules.Single(r => r.Id == "traffic.wear").Effects[0].Influence;
                foreach (var term in roadInfluence.Terms) term.Factors.Add(new() { Source = "stock.wearMultiplier", InputHigh = 4, OutputHigh = 4 });
                project.Authoring.Entries["stock.wearMultiplier"] = new() { Topic = 3, Notes = "Útállapot és terhelés visszacsatolása" };
                world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Update(1);
                Require(Math.Abs(world.DynamicsValues["wearMultiplier"] - 1.1) < 1e-9, "Dynamics stock did not integrate.");
                using var projectSave = new MemoryStream(); world.Save(projectSave); projectSave.Position = 0; restored.Load(projectSave);
                Require(restored.DynamicsValues["wearMultiplier"] == world.DynamicsValues["wearMultiplier"], "Save lost dynamics state.");
                string projectFile = Path.Combine(output, "feedback-project.json"); RuleProject.WriteAtomic(projectFile, project.ToJson());
                var projectEditor = new RuleEditorView(projectFile); projectEditor.ShowDynamics();
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); projectEditor.Draw(world, ref open);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Dynamics editor OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "dynamics.png"), width, height);
                foreach (var screen in new (Action Select, string Name)[] {
                    (() => projectEditor.ShowCommonRule("stock:wearMultiplier"), "common-stock"),
                    (() => projectEditor.ShowCommonRule("flow:increase"), "common-flow"),
                    (projectEditor.ShowRuleLibrary, "workshop-library"),
                    (() => projectEditor.ShowThematicStock("wearMultiplier", 3), "workshop-thematic-stock") }) {
                    screen.Select();
                    for (int frame = 0; frame < 3; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); projectEditor.Draw(world, ref open);
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    Require(GL.GetError() == ErrorCode.NoError, "Full workshop UI error: " + screen.Name);
                    FramebufferCapture.SavePng(Path.Combine(output, screen.Name + ".png"), width, height);
                }
                projectEditor.ShowRuleBook(world.RuleProjectDocument.Rules.FindIndex(r => r.Id == "wildlife.body"));
                for (int frame = 0; frame < 3; frame++) {
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); projectEditor.Draw(world, ref open);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Animal object fields OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "animal-fields.png"), width, height);
                // Incomplete projects can be reopened for repair without mutating the running world.
                project.Dynamics.Flows[0].Rate.Nodes.Clear();
                string incompleteFile = Path.Combine(output, "feedback-draft.json"); RuleProject.WriteAtomic(incompleteFile, project.ToJson());
                string activeBeforeDraft = world.RuleProjectDocument.ToJson();
                var incompleteEditor = new RuleEditorView(incompleteFile); incompleteEditor.ShowDynamics();
                ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); incompleteEditor.Draw(world, ref open);
                ui.Render(); GL.Finish();
                Require(world.RuleProjectDocument.ToJson() == activeBeforeDraft, "Opening an incomplete draft changed the active project.");
                Require(GL.GetError() == ErrorCode.NoError, "Incomplete draft editor OpenGL error.");
                // Old startup projects must migrate before the editor validates current hook bindings.
                var legacy = RuleProject.CreateDefault(); legacy.Version = 2;
                legacy.Rules.RemoveAll(r => r.Schedule == "wildlife");
                legacy.Behaviors.Graphs.Add(new() { Id = "default.gameplay.wildlife.forageRecovery", Name = "Legelő regenerációja",
                    Kind = "gameplay", Hook = "wildlife.forageRecovery", Output = "custom",
                    Nodes = new() { new() { Id = "custom", Name = "Saját ütem", Operation = BehaviorOperation.Constant, Value = .123 } } });
                string legacyFile = Path.Combine(output, "legacy-wildlife.project.json"), legacyJson = legacy.ToJson();
                File.WriteAllText(legacyFile, legacyJson);
                var legacyEditor = new RuleEditorView(legacyFile);
                ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); legacyEditor.Draw(world, ref open);
                ui.Render(); GL.Finish();
                Require(legacyEditor.ProjectLoadError == null, "Legacy startup project failed: " + legacyEditor.ProjectLoadError);
                var recovered = legacyEditor.CollectProject(world).Rules.Single(r => r.Id == "wildlife.forageRecovery")
                    .Effects.Single(e => e.Target == "depletion").Computation;
                Require(recovered != null && recovered.Nodes.Single(n => n.Id == "custom").Value == .123, "Legacy editor formula was lost.");
                Require(File.ReadAllText(legacyFile) == legacyJson, "Opening the legacy project overwrote its file.");
                Require(world.RuleProjectDocument.ToJson() == activeBeforeDraft, "Legacy startup project changed active rules.");
                Require(GL.GetError() == ErrorCode.NoError, "Legacy startup editor OpenGL error.");
                // Unsupported project data remains on disk and is reported without crashing Draw.
                string unsupportedFile = Path.Combine(output, "unsupported-traffic.project.json");
                string unsupportedJson = "{\"schema\":\"forest-rule-project\",\"road\":{}}";
                File.WriteAllText(unsupportedFile, unsupportedJson);
                var unsupportedEditor = new RuleEditorView(unsupportedFile);
                ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); unsupportedEditor.Draw(world, ref open);
                ui.Render(); GL.Finish();
                Require(unsupportedEditor.ProjectLoadError != null, "Unsupported project did not report its error.");
                Require(File.ReadAllText(unsupportedFile) == unsupportedJson, "Unsupported project was overwritten.");
                Require(world.RuleProjectDocument.ToJson() == activeBeforeDraft, "Unsupported project changed active rules.");
                // The actual startup file is distinct from the opening scene and can outlive changes to the model.
                if (File.Exists(RuleProject.DefaultPath)) {
                    string startupFile = File.ReadAllText(RuleProject.DefaultPath);
                    string activeBeforeStartup = world.RuleProjectDocument.ToJson();
                    var startupEditor = new RuleEditorView(RuleProject.DefaultPath);
                    for (int frame = 0; frame < 3; frame++) {
                        ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); startupEditor.Draw(world, ref open);
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    Require(File.ReadAllText(RuleProject.DefaultPath) == startupFile, "Opening the startup project changed its file.");
                    Require(world.RuleProjectDocument.ToJson() == activeBeforeStartup, "Opening the startup project changed the running world.");
                    Require(GL.GetError() == ErrorCode.NoError, "Startup project editor OpenGL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, "startup-project.png"), width, height);
                }
                // Run both native runtimes from one checkpoint: changed stock flow must produce a measured difference without touching this world.
                string comparisonFolder = Path.Combine(output, "workshop-comparison"); Directory.CreateDirectory(comparisonFolder);
                string comparisonSave = Path.Combine(comparisonFolder, "world.json");
                world.ExecutePendingCommands(); using (var state = File.Create(comparisonSave)) world.Save(state);
                var baselineProject = world.RuleProjectDocument; var editedProject = baselineProject.Clone();
                editedProject.Dynamics.Flows.Single(f => f.Id == "increase").Rate.Nodes.Single(n => n.Id == "rate").Value = .2;
                string beforeComparison = world.RuleProjectDocument.ToJson(); double beforeStock = world.DynamicsValues["wearMultiplier"];
                var comparison = RuleTrialRunner.Run(baselineProject, editedProject, comparisonSave, comparisonFolder,
                    "smoke-snapshot", "Smoke checkpoint", 30, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                var comparedStock = comparison.Measures.Single(m => m.Key == "stock.wearMultiplier");
                Require(Math.Abs(comparedStock.After - comparedStock.Before - .1) < 1e-8, "Comparison did not measure the edited stock-flow rule.");
                Require(world.RuleProjectDocument.ToJson() == beforeComparison && world.DynamicsValues["wearMultiplier"] == beforeStock,
                    "Comparison changed the editor world.");
                Require(world.ExportRuleWorld("smoke").ProjectRevision == world.RuleProjectDocument.ContentRevision(), "Runtime did not identify its actual project.");
                projectEditor.ShowWorkshopComparison(comparison);
                for (int frame = 0; frame < 2; frame++) {
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    ui.Update(width, height, width, height, Vec2.One, 1f / 60);
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(1300, 900)); projectEditor.Draw(world, ref open);
                    ui.Render(); GL.Finish();
                }
                Require(GL.GetError() == ErrorCode.NoError, "Measured trial result UI OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "workshop-comparison.png"), width, height);
                Console.WriteLine($"Rule editor smoke passed: terrain binding, snapshot, pending commands, journal replay, UI and behavior editor. Capture: {output}");
            }
            finally { RenderDevice.Dispose(); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
