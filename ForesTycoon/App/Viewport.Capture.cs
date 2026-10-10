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

        private void RunCaptureScript()
        {
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
                    simulationClock.Speed = 256;
                    simulationClock.IsPaused = false;
                    break;
                case 285: Save("07-fast-forward-menu"); break;
                case 286:
                    simulationClock.Speed = 1;
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
    }
}
