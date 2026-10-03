using System;

namespace ForesTycoon
{
    static class Program
    {
        static void Main(string[] args)
        {
            if (Array.Exists(args, argument => argument == "--procedural-tree-preview")) { ProceduralTreePreview.Run(); return; }
            if (Array.Exists(args, argument => argument == "--forest-model-smoke-test")) { ForestModelSmokeTest.Run(); return; }
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
            if (Array.Exists(args, argument => argument == "--wildlife-smoke-test")) { WildlifeSmokeTest.Run(); return; }
            if (Array.Exists(args, argument => argument == "--graphics-smoke-test")) { GraphicsWeatherSmokeTest.Run(); return; }

            if (Array.Exists(args, argument => argument == "--truck-smoke-test")) { TruckRenderSmokeTest.Run(); return; }
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
