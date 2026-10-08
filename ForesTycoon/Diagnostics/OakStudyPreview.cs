using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Same open-grown oak, summer and bare, from three directions. Physical sizes
    // are fixed so crown meshing and architecture can be compared independently.
    internal static class OakStudyPreview
    {
        internal static void Run(bool before)
        {
            const int width = 1200, height = 840;
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new(width, height), NumberOfSamples = 4, API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                GL.Viewport(0, 0, width, height); GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace);
                GL.ClearColor(0.19f, 0.23f, 0.27f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                RenderDevice.SetCamera(Matrix4.CreateRotationX(-MathF.PI / 2.1f)
                    * Matrix4.CreateOrthographicOffCenter(0, 3, -0.04f, 2.06f, -100, 100));
                using var visuals = new SurfaceVisualRenderer(new GraphicsSettings {
                    Shadows = false, Weather = false, Fog = false, Textures = true }, new WeatherVisualState());
                RenderDevice.Visuals = visuals; visuals.BeginFrame();
                using var material = new ForestMaterial();
                using var wood = new VertexBuffer(PrimitiveTopology.Triangles);
                using var crown = new VertexBuffer(PrimitiveTopology.Triangles);
                var woodVertices = new List<Vertex>(); var crownVertices = new List<Vertex>();
                var size = new ForestTreeDimensions(0.85f, 18, 8);
                for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                {
                    var spec = new TreeShapeSpec(ForestSpecies.Oak, 42, TreeLifePhase.Mature,
                        size, 1, new TreeSite(1), row == 0 ? LeafState.Full : LeafState.Bare,
                        col * MathF.Tau / 3);
                    var mesh = DendroTreeGenerator.Generate(spec, ForestLod.Near);
                    Console.WriteLine($"view {col}, {(row == 0 ? "summer" : "bare")}: wood {(mesh.Trunk.Length + mesh.Branches.Length) / 3}, crown {mesh.Crown.Length / 3} triangles");
                    Add(mesh.Trunk); Add(mesh.Branches); Add(mesh.Crown, true);
                    void Add(Vertex[] source, bool foliage = false)
                    {
                        float scale = 0.85f / (size.Height * TreeScale.MetresToWorld);
                        foreach (var v in source) (foliage ? crownVertices : woodVertices).Add(
                            new(v.Position * scale + new Vector3(col + 0.5f, 0, 1 - row), v.Normal, v.Color));
                    }
                }
                wood.SetData(woodVertices.ToArray()); crown.SetData(crownVertices.ToArray());
                visuals.Kind = SurfaceKind.Wood; RenderDevice.UseGeometryShader(); wood.DrawArray(false);
                material.Use(); crown.DrawArray(false);
                using var ui = new ImGuiController();
                ui.Update(width, height, width, height, System.Numerics.Vector2.One, 1f / 60);
                var draw = ImGui.GetBackgroundDrawList();
                for (int col = 0; col < 3; col++)
                {
                    draw.AddText(new(col * 400 + 24, 12), 0xffffffff, $"Kocsányos tölgy – {col * 120}° – lombosan");
                    draw.AddText(new(col * 400 + 24, 422), 0xffffffff, $"Ugyanaz az ágváz – {col * 120}° – lomb nélkül");
                }
                ui.Render();
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Oak study GL error.");
                string output = Path.GetFullPath("artifacts/oak-study"); Directory.CreateDirectory(output);
                FramebufferCapture.SavePng(Path.Combine(output, before ? "before.png" : "after.png"), width, height);
            }
            finally { RenderDevice.Visuals = null; RenderDevice.Dispose(); }
        }
    }
}
