using System.IO;

namespace ForesTycoon
{
    /// <summary>
    /// --capture-frame: scripted HUD and diorama review shots, saved after the HUD is drawn
    /// and before the swap, so the PNGs show exactly what a player sees.
    /// </summary>
    sealed partial class Viewport
    {
        private readonly string captureDirectory;
        private readonly bool captureSeasons;
        private bool seasonalCaptureStarted;
        private int seasonalCaptureSample;
        private ulong seasonalCaptureFrames;

        private void RunCaptureScript()
        {
            if (captureSeasons) { RunSeasonalCapture(); return; }
            void Save(string name)
            {
                System.Console.WriteLine($"Capture {name}: forest resident={world.ForestResidentLods}, rebuilt={world.ForestChunkRebuilds}, visible={world.VisibleChunkCount}, CPU={world.ForestCpuPayloadBytes}, GPU={world.ForestGpuPayloadBytes}, enhanced={world.Graphics.Enhanced}, grid={world.Graphics.ShowGrid}");
                Directory.CreateDirectory(captureDirectory);
                FramebufferCapture.SavePng(Path.Combine(captureDirectory, name + ".png"), FramebufferWidth, FramebufferHeight);
            }

            switch (frameIndex)
            {
                case 1: Save("00-first-frame"); break;
                case 60: Save("01-hud-default"); break;
                case 61:
                    showGraphics = showDeveloper = showForestry = true;
                    SelectTool(TerrainEditTool.PlantForest);
                    break;
                case 90: Save("02-hud-windows"); break;
                case 91:
                    showGraphics = showDeveloper = showForestry = false;
                    SelectTool(TerrainEditTool.Inspect);
                    world.Graphics.Diorama = false;
                    break;
                case 120: Save("03-without-diorama"); break;
                case 121: world.Graphics.Diorama = true; FocusOnDeer(); break;
                case 170: Save("04-deer-closeup"); break;
                case 171:
                    world.Graphics.ShowGrid = true;
                    if (world.DiagnosticFellForestBlock(out var block)) FocusOn(block, 30f);
                    break;
                case 230: Save("05-stumps-grid"); break;
                case 231: FocusOn(cameraFocus, 60f); world.Graphics.ShowGrid = false; break;
                case 270: Save("06-species-closeup"); break;
                case 271:
                    world.Graphics.ShowGrid = true;
                    SelectTool(TerrainEditTool.Inspect);
                    GameSpeed = 256;
                    simulationClock.IsPaused = false;
                    break;
                case 285: Save("07-fast-forward-menu"); break;
                case 286:
                    GameSpeed = 1;
                    simulationClock.IsPaused = true;
                    ResetCamera();
                    showManagement = true;
                    management.Select(ManagementView.Lens.Todo);
                    break;
                case 320: Save("08-management-todo"); break;
                case 321: management.Select(ManagementView.Lens.Water); break;
                case 350: Save("09-management-water"); break;
                case 351: management.Select(ManagementView.Lens.Health); break;
                case 380: Save("10-management-health"); break;
                case 381: showManagement = false; break;
                case 400: Save("11-management-closed"); break;
                case 401: ChooseVerb(Verb.Build); break;
                case 420: Save("12-verb-build"); break;
                case 421: ChooseVerb(Verb.Transport); break;
                case 440: Save("13-verb-transport"); break;
                case 441: Close(); break;
            }
        }

        // Runs the real viewport, HUD, world, wall-clock simulation runner and diorama.
        // No synchronous meshes, fixture world, artificial weather or accelerated year.
        private void RunSeasonalCapture()
        {
            if (frameIndex == 1)
            {
                simulationClock.IsPaused = true;
                int rows = world.Map.Settings.TileRows, columns = world.Map.Settings.TileColumns;
                int bestId = 0, bestScore = -1;
                for (int u = 3; u < columns - 3; u++)
                for (int v = 3; v < rows - 3; v++)
                {
                    int trees = 0;
                    var species = new System.Collections.Generic.HashSet<ForestSpecies>();
                    for (int du = -2; du <= 2; du++)
                    for (int dv = -2; dv <= 2; dv++)
                        if (world.TryGetForestStand((u + du) * rows + v + dv, out var stand))
                        { trees++; species.Add(stand.Species); }
                    int score = trees + species.Count * 10;
                    if (score > bestScore) { bestScore = score; bestId = u * rows + v; }
                }
                if (!world.TryGetTileCenter(bestId, out var centre))
                    throw new System.InvalidOperationException("No seasonal capture forest centre.");
                FocusOn(centre + new OpenTK.Mathematics.Vector3(0, 0, 2), 45);
            }
            if (frameClock.TotalTimeSeconds > 180)
                throw new System.InvalidOperationException("Seasonal viewport capture timed out.");
            int visible = world.VisibleChunkCount, near = world.ReadyVisibleForestChunks(ForestLod.Near);
            if (!seasonalCaptureStarted)
            {
                if (frameIndex < 30 || visible == 0 || near != visible) return;
                seasonalCaptureStarted = true;
                GameSpeed = 256;
                simulationClock.IsPaused = false;
            }
            if (near != visible || world.ReadyVisibleForestChunks(ForestLod.Medium) != 0
                || world.ReadyVisibleForestChunks(ForestLod.Far) != 0)
                throw new System.InvalidOperationException($"Close-up seasonal LOD degraded: near={near}, visible={visible}.");
            if (!world.Graphics.Diorama || GameSpeed != 256)
                throw new System.InvalidOperationException("Seasonal capture lost the diorama or 256x speed.");
            seasonalCaptureFrames++;
            double year = world.Environment.Time / world.Environment.ForestYearSeconds;
            if (year + 1e-6 >= seasonalCaptureSample / 24.0)
            {
                Directory.CreateDirectory(captureDirectory);
                string name = $"season-{seasonalCaptureSample:D2}";
                FramebufferCapture.SavePng(Path.Combine(captureDirectory, name + ".png"), FramebufferWidth, FramebufferHeight);
                System.Console.WriteLine($"{name}: year={year:F4}, speed={GameSpeed}, Near={near}/{visible}, diorama={world.Graphics.Diorama}, frame={frameIndex}, rebuilt={world.ForestChunkRebuilds}");
                seasonalCaptureSample++;
            }
            if (seasonalCaptureSample > 48)
            {
                RenderDevice.CheckErrors("256x seasonal close-up viewport");
                System.Console.WriteLine($"Seasonal viewport passed: {seasonalCaptureFrames} frames, two years at 256x, all visible chunks stayed Near, diorama retained.");
                Close();
            }
        }
    }
}
