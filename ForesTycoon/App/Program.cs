using System;

namespace ForesTycoon
{
    static class Program
    {
        static void Main(string[] args)
        {
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
            using Viewport game = new Viewport(smokeTest ? 120UL : null);
            game.Run();
        }
    }
}
