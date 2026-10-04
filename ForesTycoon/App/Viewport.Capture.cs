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
                Directory.CreateDirectory(captureDirectory);
                FramebufferCapture.SavePng(Path.Combine(captureDirectory, name + ".png"), FramebufferWidth, FramebufferHeight);
            }

            switch (frameIndex)
            {
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
                case 286: Close(); break;
            }
        }
    }
}
