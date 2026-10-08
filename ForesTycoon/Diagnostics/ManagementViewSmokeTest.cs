using System;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vector2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    // Captures every management lens of a grown world and checks that drawing it never changes the simulation.
    internal static class ManagementViewSmokeTest
    {
        internal static void Run()
        {
            const int width = 1000, height = 760;
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(width, height), API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(65, 42));
                // A few forest years, so stands compete, dry out and mature.
                for (int step = 0; step < 600; step++) world.Update(1);
                using var ui = new ImGuiController();
                string output = Path.GetFullPath("artifacts/management-view"); Directory.CreateDirectory(output);
                var view = new ManagementView();
                var survey = world.BuildManagementSurvey();
                foreach (var issue in Enum.GetValues<ManagementIssue>())
                {
                    var group = Array.FindAll(survey, s => s.Issue == issue && s.IsForest);
                    if (group.Length == 0) continue;
                    float Mean(Func<ForestTileSurvey, float> f) { float sum = 0; foreach (var s in group) sum += f(s); return sum / group.Length; }
                    Console.WriteLine($"{issue}: {group.Length} stands · health {Mean(s => s.Health):0.00} · dead {Mean(s => s.DeadShare):0.00} · " +
                        $"stressed {Mean(s => s.StressedShare):0.00} · crowded {Mean(s => s.CrowdedShare):0.00} · trees {Mean(s => s.Trees):0.0} · age {Mean(s => s.AgeYears):0}");
                }
                int focus = Array.FindIndex(survey, s => s.Issue != ManagementIssue.None && s.IsForest);
                view.SelectTile(focus >= 0 ? focus : 32 * 64 + 32);
                ulong revision = world.ForestRevision; int trees = world.ForestTreeCount;
                double stored = world.Environment.StoredWater;
                byte[] previous = null;
                foreach (var (lens, layer, name) in new[] {
                    (ManagementView.Lens.Todo, 0, "todo"), (ManagementView.Lens.Water, 0, "water"), (ManagementView.Lens.Water, 1, "drought"),
                    (ManagementView.Lens.Soil, 0, "soil"), (ManagementView.Lens.Soil, 1, "fertility"),
                    (ManagementView.Lens.Health, 0, "vitality"), (ManagementView.Lens.Health, 1, "limit"),
                    (ManagementView.Lens.Stand, 0, "species"), (ManagementView.Lens.Stand, 1, "maturity") })
                {
                    view.Select(lens, layer);
                    byte[] pixels = Capture(name);
                    if (previous != null && pixels.AsSpan().SequenceEqual(previous)) throw new InvalidOperationException($"Lens {name} drew nothing new.");
                    previous = pixels;
                }
                if (revision != world.ForestRevision || trees != world.ForestTreeCount || stored != world.Environment.StoredWater)
                    throw new InvalidOperationException("Management view changed the simulation.");
                Console.WriteLine($"Management view smoke passed: 9 lens captures, read-only state. Captures: {output}");

                byte[] Capture(string name)
                {
                    // Warm the ImGui layout, then render.
                    for (int frame = 0; frame < 2; frame++)
                    {
                        ui.Update(width, height, width, height, Vector2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20));
                        ImGui.Begin("Erdőgazdálkodás", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
                        view.Draw(world);
                        ImGui.End();
                        GL.Viewport(0, 0, width, height); GL.ClearColor(.08f, .1f, .12f, 1);
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Management UI produced a GL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), width, height);
                    var pixels = new byte[width * height * 4];
                    GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return pixels;
                }
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
