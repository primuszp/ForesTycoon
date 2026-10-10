using System;

namespace ForesTycoon
{
    static class Program
    {
        static void Main(string[] args)
        {
            RenderBackendSelection.UseOpenGl();
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
            if (Array.Exists(args, a => a == "--forest-icon-smoke-test")) { ForestIconSmokeTest.Run(); return; }
            if (Array.Exists(args, a => a == "--tycoon-icon-preview")) { TycoonIconPreview.Run(); return; }
            if (Array.Exists(args, a => a == "--vegetation-preview")) { VegetationPreview.Run(); return; }
            if (Array.Exists(args, a => a == "--vegetation-benchmark")) { VegetationBenchmark.Run(Array.Exists(args, a => a == "--before") ? "before" : "after"); return; }
            if (Array.Exists(args, argument => argument == "--forest-tempo-benchmark")) { ForestTempoBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--road-build-benchmark")) { RoadBuildBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--world-benchmark")) { WorldFrameBenchmark.Run(); return; }
            if (Array.Exists(args, argument => argument == "--map-benchmark")) { TerrainMapBenchmark.Run(); return; }
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
            using Viewport game = new Viewport(smokeTest ? 120UL : null, captureFrame >= 0 ? args[captureFrame + 1] : null);
            game.Run();
        }
    }
}
