using System;
using System.Diagnostics;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon;

internal static class StartupSmokeTest
{
    internal static void Run()
    {
        using var window = new NativeWindow(new NativeWindowSettings {
            StartVisible = false, ClientSize = new Vector2i(320, 240), API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
        window.Context.MakeCurrent(); RenderDevice.Initialize(); RenderDevice.InitializeFrameState();
        var result = new WorkResult<GameWorld>();
        try {
            var stages = new HashSet<string>();
            using var work = WorldLoader.CreateStartWork(result, enableRendering: true, reportStage: stage => stages.Add(stage)).GetEnumerator();
            var timer = Stopwatch.StartNew(); int yields = 0;
            while (work.MoveNext()) {
                yields++;
                if (yields % 4096 == 0) System.Threading.Thread.Sleep(1); // Let background preparation run between batches, like the frame host.
                if (timer.Elapsed > TimeSpan.FromMinutes(3)) throw new InvalidOperationException("Startup timed out.");
            }
            if (result.Value == null || !result.Value.HasPresentation || yields < 2 || stages.Count < 3)
                throw new InvalidOperationException("Startup did not publish a rendered world in slices.");
            result.Value.Update(1d / 30);
            // Closing after publication can interrupt old-world retirement. It must still release both worlds.
            result.Value.RequestRegenerate(TerrainSettings.Default.WithNodeSize(17, 42));
            while (result.Value.GenerationPending && result.Value.GenerationStage != "Régi pálya felszabadítása") {
                result.Value.ContinueGeneration(1);
                if (timer.Elapsed > TimeSpan.FromMinutes(3)) throw new InvalidOperationException("Retirement test timed out.");
                System.Threading.Thread.Yield();
            }
            if (result.Value.GenerationError != null || !result.Value.GenerationPending)
                throw new InvalidOperationException(result.Value.GenerationError ?? "Retirement did not suspend.");
            result.Value.Dispose(); result.Value.Dispose();
            Console.WriteLine($"Rendered opening-scene startup and interrupted retirement passed: {yields} yields, {stages.Count} stage reports, {result.Value.ForestTreeCount} trees.");
        }
        finally { result.Value?.Dispose(); RenderDevice.Dispose(); }
    }
}
