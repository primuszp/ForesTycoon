using System;

namespace ForesTycoon
{
    static class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--site-response-benchmark") { RuleStepBenchmark.Run(64, 256); return; }
            if (args.Length == 1 && args[0] == "--tree-vitality-benchmark") { TreeVitalityBenchmark.Run(); return; }
            if (args.Length == 1 && args[0] == "--traffic-benchmark") { TrafficRuleBenchmark.Run(); return; }
            if (args.Length == 2 && args[0] == "--record-opening-reference") {
                using var reference = new GameWorld(TerrainSettings.Default, enableRendering: false);
                System.IO.Directory.CreateDirectory(args[1]);
                System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "fingerprints.json"),
                    System.Text.Json.JsonSerializer.Serialize(OpeningSceneReference.Fingerprints(reference), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "catalog.json"), CurrentGameRules.Build().ToJson());
                var project = reference.RuleProjectDocument; project.World = WorldLoader.CaptureScenario(reference);
                System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "opening.project.json"), project.ToJson());
                using var save = System.IO.File.Create(System.IO.Path.Combine(args[1], "opening.world.json.gz"));
                using var compressed = new System.IO.Compression.GZipStream(save, System.IO.Compression.CompressionLevel.Optimal);
                reference.Save(compressed);
                return;
            }
            if (GraphRuntimeHost.RunHeadless(args)) return;
            if ((args.Length == 0 || Array.IndexOf(args, "--algorithm-handoff") >= 0) && GraphRuntimeHost.DispatchProjectRuntime(args)) return;
            RenderBackendSelection.UseOpenGl();
            int forwarderExport = Array.IndexOf(args, "--export-forwarder-animation");
            if (forwarderExport >= 0) { ForwarderAnimationExport.Run(args.Length > forwarderExport + 1 ? args[forwarderExport + 1] : "artifacts/forwarder-ik.glb"); return; }
            int loadingPreview = Array.IndexOf(args, "--forwarder-loading-preview");
            if (loadingPreview >= 0) { ForestMachineSmokeTest.LoadingPreview(args.Length > loadingPreview + 1 ? args[loadingPreview + 1] : "artifacts/forwarder-loading"); return; }
            int largeBenchmark = Array.FindIndex(args, argument => argument == "--large-world-benchmark");
            if (largeBenchmark >= 0) { LargeWorldBenchmark.Run(args, largeBenchmark); return; }
            if (Array.Exists(args, a => a == "--render-environment-smoke-test")) { RenderEnvironmentSmokeTest.Run(); return; }
            if (Array.Exists(args, a => a == "--oak-study-preview")) { OakStudyPreview.Run(Array.Exists(args, a => a == "--before")); return; }
            if (Array.Exists(args, a => a == "--broadleaf-canopy-preview")) { BroadleafCanopyPreview.Run(Array.Exists(args, a => a == "--before")); return; }
            int gallery = Array.FindIndex(args, a => a == "--tree-gallery");
            if (gallery >= 0)
            {
                string name = gallery + 1 < args.Length && !args[gallery + 1].StartsWith("--") ? args[gallery + 1] : "current";
                foreach (var lod in Enum.GetValues<ForestLod>()) TreeGalleryPreview.Run(name, lod);
                return;
            }
            if (Array.Exists(args, a => a == "--ui-font-smoke-test")) { UiFontSmokeTest.Run(); return; }
            if (Array.Exists(args, a => a == "--ui-render-smoke-test")) { UiRenderSmokeTest.Run(); return; }
            if (Array.Exists(args, a => a == "--forest-icon-smoke-test")) { ForestIconSmokeTest.Run(); return; }
            if (Array.Exists(args, a => a == "--tycoon-icon-preview")) { TycoonIconPreview.Run(); return; }
            if (Array.Exists(args, a => a == "--vegetation-preview")) { VegetationPreview.Run(); return; }
            if (Array.Exists(args, a => a == "--vegetation-benchmark")) { VegetationBenchmark.Run(Array.Exists(args, a => a == "--before") ? "before" : "after"); return; }
            if (Array.Exists(args, argument => argument == "--forest-tempo-benchmark")) { ForestTempoBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--road-build-benchmark")) { RoadBuildBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--world-benchmark")) { WorldFrameBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--map-benchmark")) { TerrainMapBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--foreground-render-benchmark")) { ForegroundWorkBenchmark.Run(true); return; }
            if (Array.Exists(args, argument => argument == "--foreground-benchmark")) { ForegroundWorkBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--rule-benchmark")) { RuleStepBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--simulation-benchmark")) { ForestSimulationBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--plantation-smoke-test"))
            {
                try { PlantationSmokeTest.Run(); }
                catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
                return;
            }
            if (Array.Exists(args, argument => argument == "--procedural-tree-preview")) { ProceduralTreePreview.Run(); return; }
            if (Array.Exists(args, argument => argument == "--dendro-tree-preview")) { ProceduralTreePreview.Run(true, true); return; }
            if (Array.Exists(args, argument => argument == "--dendro-tree-smoke-test")) { DendroTreeSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--tree-life-stage-preview")) { ProceduralTreePreview.Run(true); return; }
            int treePreview = Array.FindIndex(args, argument => argument == "--tree-asset-preview");
            if (treePreview >= 0) {
                if (treePreview + 1 >= args.Length) throw new ArgumentException("--tree-asset-preview requires a GLB path.");
                TreeAssetPreview.Run(args[treePreview + 1]); return;
            }
            if (Array.Exists(args, argument => argument == "--material-alpha-smoke-test")) { MaterialAlphaSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--spruce-preview")) { SprucePreview.Run(); return; }
            if (Array.Exists(args, argument => argument == "--tree-growth-smoke-test")) { ForestIndividualSmokeTest.Run(); return; }
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Console.Error.WriteLine(args.ExceptionObject?.ToString() ?? "Unknown fatal error");

            if (Array.Exists(args, argument => argument == "--logistics-smoke-test")) { ForestryLogisticsSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--environment-smoke-test")) { EnvironmentSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--flood-render-smoke-test")) { FloodRenderSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--soil-raster-smoke-test")) { SoilRasterSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--management-smoke-test")) { ManagementViewSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--checkpoint-smoke-test")) { WorldCheckpointSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--wildlife-smoke-test")) { WildlifeSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--graphics-smoke-test")) { GraphicsWeatherSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--seasonal-weather-smoke-test")) { SeasonalWeatherSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--road-network-smoke-test")) { RoadNetworkSmokeTest.Run(); return; }

            if (Array.Exists(args, argument => argument == "--truck-smoke-test")) { TruckRenderSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--daylight-preview")) { DaylightPreview.Run(); return; }
            if (Array.Exists(args, argument => argument == "--forest-machine-smoke-test")) { ForestMachineSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--camera-benchmark")) { ForestBenchmark.Run(true); return; }
            if (Array.Exists(args, argument => argument == "--engine-stress-benchmark")) { EngineStressBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--engine-benchmark")) { ForestBenchmark.Run(false, true); return; }
            if (Array.Exists(args, argument => argument == "--forest-benchmark")) { ForestBenchmark.Run(); return; }
            if (Array.Exists(args, argument => string.Equals(argument, "--forest-preview", StringComparison.OrdinalIgnoreCase)))
            {
                int capture = Array.FindIndex(args, argument => argument == "--capture");
                if (capture >= 0 && capture + 1 >= args.Length) throw new ArgumentException("--capture requires an output directory.");
                using var preview = new ForestPreviewWindow(capture >= 0 ? args[capture + 1] : null);
                preview.Run();
                return;
            }
            if (Array.Exists(args, argument => string.Equals(argument, "--forest-smoke-test", StringComparison.OrdinalIgnoreCase)))
            {
                ForestRenderSmokeTest.Run();
                return;
            }
            bool smokeTest = Array.Exists(args, argument =>
                string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase));
            int captureFrame = Array.FindIndex(args, argument => argument == "--capture-frame");
            if (captureFrame >= 0 && captureFrame + 1 >= args.Length) throw new ArgumentException("--capture-frame requires an output directory.");
            int captureSeasons = Array.FindIndex(args, argument => argument == "--capture-seasons");
            int handoff = Array.IndexOf(args, "--algorithm-handoff");
            int sceneProject = Array.IndexOf(args, "--scene-project");
            if (sceneProject >= 0 && (sceneProject + 1 >= args.Length || handoff >= 0))
                throw new ArgumentException("--scene-project jelenetprojekt.json szükséges; algoritmusátadással együtt nem használható.");
            if (handoff >= 0 && handoff + 2 >= args.Length) throw new ArgumentException("Az algoritmusátadás mentést és nyugtázási útvonalat kér.");
            if (captureSeasons >= 0 && captureSeasons + 1 >= args.Length) throw new ArgumentException("--capture-seasons requires an output directory.");
            using Viewport game = new Viewport(smokeTest ? 120UL : null,
                captureSeasons >= 0 ? args[captureSeasons + 1] : captureFrame >= 0 ? args[captureFrame + 1] : null,
                captureSeasons: captureSeasons >= 0, resumeWorld: handoff >= 0 ? args[handoff + 1] : null,
                runtimeAcknowledgement: handoff >= 0 ? args[handoff + 2] : null,
                sceneProject: sceneProject >= 0 ? args[sceneProject + 1] : null);
            game.Run();
        }
    }
}
