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
        private IEnumerator<int> worldCreation;
        private readonly WorkResult<GameWorld> createdWorld = new();
        private bool loadingPresented;
        private float loadingProgress;
        private string loadingStage = "A táj létrehozása…";
        private readonly string sceneProject;

        private void RenderLoadingFrame()
        {
            isLoaded = false;
            // Present once before constructing the world so the very first expensive
            // initialization is covered too. GPU preparation remains on its owner thread.
            if (loadingPresented)
            {
                if (world == null)
                {
                    worldCreation ??= WorldLoader.CreateStartWork(createdWorld, sceneProject, reportStage: stage => { if (!string.IsNullOrEmpty(stage)) loadingStage = stage; }).GetEnumerator();
                    long creationStarted = Stopwatch.GetTimestamp();
                    int slices = 0;
                    while (slices++ < 4096 && Stopwatch.GetElapsedTime(creationStarted).TotalMilliseconds < 4) {
                        if (worldCreation.MoveNext()) continue;
                        worldCreation.Dispose(); worldCreation = null; world = createdWorld.Value; break;
                    }
                    if (world == null) { DrawLoadingScreen(); return; }
                    if (resumeWorld != null) {
                        // The rule project link applies the published project to the resumed world on its first poll.
                        using var input = System.IO.File.OpenRead(resumeWorld); world.Load(input, allowAlgorithmMigration: true);
                    }
                    interaction = new WorldInteractionController(world) { TargetPicked = SendTargetPicked };
                }
                if (world.ForegroundPending) {
                    long commandsStarted = Stopwatch.GetTimestamp();
                    do { world.ContinueCommands(); }
                    while (world.ForegroundPending && Stopwatch.GetElapsedTime(commandsStarted).TotalMilliseconds < 4);
                    if (world.ForegroundPending) { DrawLoadingScreen(); return; }
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
                        if (runtimeAcknowledgement != null) RuleProject.WriteAtomic(runtimeAcknowledgement, "ready");
                        break;
                    }
                    loadingProgress = mapPreparation.Current;
                }
                while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 4);
            }
            else
            {
                loadingProgress = 0;
                loadingPresented = true;
            }

            DrawLoadingScreen();
        }

        private void DrawLoadingScreen()
        {
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
            ImGui.TextUnformatted(world == null ? loadingStage : "Erdők és pályaelemek előkészítése…");
            if (world == null) {
                // Generation has no meaningful total yet: show activity, not a fabricated percentage.
                var start = ImGui.GetCursorScreenPos();
                var size = new NVec2(Math.Max(1, ImGui.GetContentRegionAvail().X), ImGui.GetFrameHeight());
                ImGui.InvisibleButton("##loading-activity", size);
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(start, start + size, ImGui.GetColorU32(ImGuiCol.FrameBg), 3);
                float block = size.X * .22f;
                float x = (float)(.5 - .5 * Math.Cos(ImGui.GetTime() * 3)) * (size.X - block);
                draw.AddRectFilled(start + new NVec2(x, 0), start + new NVec2(x + block, size.Y), ImGui.GetColorU32(ImGuiCol.PlotHistogram), 3);
            }
            else ImGui.ProgressBar(loadingProgress, new NVec2(-1, 0));
            ImGui.TextWrapped("A teljes pálya és a fák részletességi szintjei betöltődnek.");
            ImGui.End();
            imgui.Render();
            RenderBackendSelection.Window.Present(this);
        }
    }
}
