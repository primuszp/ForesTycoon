using System;
using System.Collections.Generic;
using System.Diagnostics;
using ImGuiNET;
using OpenTK.Mathematics;
using NVec2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    sealed partial class Viewport
    {
        private IEnumerator<float> mapPreparation;
        private bool loadingPresented;
        private float loadingProgress;

        private void RenderLoadingFrame()
        {
            isLoaded = false;
            // Present once before constructing the world so the very first expensive
            // initialization is covered too. GPU preparation remains on its owner thread.
            if (loadingPresented)
            {
                if (world == null)
                {
                    world = new GameWorld(TerrainSettings.Default);
                    interaction = new WorldInteractionController(world) { TargetPicked = SendTargetPicked };
                }
                mapPreparation ??= world.PrepareMapGeometry().GetEnumerator();
                long started = Stopwatch.GetTimestamp();
                do
                {
                    if (!mapPreparation.MoveNext())
                    {
                        mapPreparation.Dispose(); mapPreparation = null;
                        loadingProgress = 1;
                        loadingPresented = false;
                        isLoaded = true;
                        frameClock.Reset();
                        SetupViewport();
                        break;
                    }
                    loadingProgress = mapPreparation.Current;
                }
                while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 12);
            }
            else
            {
                loadingProgress = 0;
                loadingPresented = true;
            }

            RenderDevice.SetViewport(FramebufferWidth, FramebufferHeight);
            RenderDevice.Clear(new Vector4(.045f, .075f, .055f, 1));
            float scale = DpiScale;
            imgui.Update(Width, Height, FramebufferWidth, FramebufferHeight, new NVec2(scale, scale), 1f / 60);
            ImGui.SetNextWindowPos(new NVec2(Width * .5f, Height * .5f), ImGuiCond.Always, new NVec2(.5f));
            ImGui.SetNextWindowSize(new NVec2(Math.Min(460, Width), 0));
            ImGui.Begin("Pálya betöltése", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
            ImGui.TextUnformatted("ForesTycoon");
            ImGui.Spacing();
            ImGui.TextUnformatted(world == null ? "A táj létrehozása…" : "Erdők és pályaelemek előkészítése…");
            ImGui.ProgressBar(loadingProgress, new NVec2(-1, 0));
            ImGui.TextWrapped("A teljes pálya és a fák részletességi szintjei betöltődnek.");
            ImGui.End();
            imgui.Render();
            RenderBackendSelection.Window.Present(this);
        }
    }
}
