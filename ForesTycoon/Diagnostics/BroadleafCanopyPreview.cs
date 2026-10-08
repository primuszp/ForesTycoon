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
    internal static class BroadleafCanopyPreview
    {
        internal static void Run(bool before)
        {
            const int width = 1440, height = 960;
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new(width, height), NumberOfSamples = 4, API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                GL.Viewport(0, 0, width, height); GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace);
                GL.ClearColor(.19f, .23f, .27f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                RenderDevice.SetCamera(Matrix4.CreateRotationX(-1.05f) * Matrix4.CreateOrthographicOffCenter(0, 5.4f, -.3f, 3.3f, -100, 100));
                using var visuals = new SurfaceVisualRenderer(new GraphicsSettings { Shadows = false, Weather = false,
                    Fog = false, Textures = true }, new WeatherVisualState());
                RenderDevice.Visuals = visuals; visuals.BeginFrame();
                using var material = new ForestMaterial();
                using var wood = new VertexBuffer(PrimitiveTopology.Triangles);
                using var crown = new VertexBuffer(PrimitiveTopology.Triangles);
                var woody = new List<Vertex>(); var foliage = new List<Vertex>();
                var size = new ForestTreeDimensions(.45f, 18, 5);
                float displayScale = 1.2f / (size.Height * TreeScale.MetresToWorld);
                for (int row = 0; row < 2; row++)
                for (int column = 0; column < 3; column++)
                {
                    var species = row == 0 ? ForestSpecies.Oak : ForestSpecies.Beech;
                    var positions = new List<Vector2>();
                    if (column == 0) positions.Add(Vector2.Zero);
                    else for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) positions.Add(new(x * 6, y * 6));
                    var neighbours = new List<(Vector2 Position, float Radius, float Height)>();
                    for (int i = 0; i < positions.Count; i++)
                    {
                        neighbours.Clear();
                        foreach (var p in positions) if (p != positions[i]) neighbours.Add((p, 5, 18));
                        // Closed patch includes a surrounding ring. The edge patch ends at this 3x3 group.
                        if (column == 1)
                            for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++)
                                if (Math.Abs(x) == 2 || Math.Abs(y) == 2) neighbours.Add((new(x * 6, y * 6), 5, 18));
                        var space = CrownSpace.Measure(positions[i], 5, 18,
                            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(neighbours));
                        var site = new TreeSite(column == 1 ? .65f : 1, Space: space);
                        var spec = new TreeShapeSpec(species, 42 + i * 7, TreeLifePhase.Mature, size, 1, site, LeafState.Full, i * .7f);
                        var mesh = DendroTreeGenerator.Generate(spec, ForestLod.Near);
                        Vector3 origin = new(column * 1.8f + .9f + positions[i].X * TreeScale.MetresToWorld * displayScale,
                            positions[i].Y * TreeScale.MetresToWorld * displayScale, (1 - row) * 1.8f);
                        Add(mesh.Trunk, woody); Add(mesh.Branches, woody); Add(mesh.Crown, foliage);
                        void Add(Vertex[] vertices, List<Vertex> output)
                        { foreach (var v in vertices) output.Add(new(v.Position * displayScale + origin, v.Normal, v.Color)); }
                    }
                }
                wood.SetData(woody.ToArray()); crown.SetData(foliage.ToArray());
                visuals.Kind = SurfaceKind.Wood; RenderDevice.UseGeometryShader(); wood.DrawArray(false);
                material.Use(); crown.DrawArray(false);
                using var ui = new ImGuiController();
                ui.Update(width, height, width, height, System.Numerics.Vector2.One, 1f / 60);
                var draw = ImGui.GetBackgroundDrawList();
                string[] headings = { "Szabadon álló", "Zárt állomány", "Állományszél" };
                for (int row = 0; row < 2; row++) for (int column = 0; column < 3; column++)
                    draw.AddText(new(column * 480 + 24, row * 480 + 16), 0xffffffff,
                        (row == 0 ? "Tölgy" : "Bükk") + " - " + headings[column]);
                ui.Render(); GL.Finish();
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Broadleaf canopy preview GL error.");
                string output = Path.GetFullPath("artifacts/broadleaf-canopy"); Directory.CreateDirectory(output);
                FramebufferCapture.SavePng(Path.Combine(output, before ? "before.png" : "after.png"), width, height);
                Console.WriteLine($"Broadleaf canopy preview: {output}; {foliage.Count / 3} foliage triangles.");
            }
            finally { RenderDevice.Visuals = null; RenderDevice.Dispose(); }
        }
    }
}
