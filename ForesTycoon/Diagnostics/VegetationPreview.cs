using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Direct generator contact sheet: deciduous, conifer and multi-stem shrub.
    internal static class VegetationPreview
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new(1200, 600), NumberOfSamples = 4, API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                GL.Viewport(0, 0, 1200, 600); GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace);
                GL.ClearColor(0.19f, 0.23f, 0.27f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                RenderDevice.SetCamera(Matrix4.CreateRotationX(-MathF.PI / 2.4f)
                    * Matrix4.CreateOrthographicOffCenter(-10, 10, -1, 9, -100, 100));
                using var visuals = new SurfaceVisualRenderer(new GraphicsSettings { Shadows = false, Weather = false,
                    Fog = false, Textures = true }, new WeatherVisualState());
                RenderDevice.Visuals = visuals; visuals.BeginFrame();
                using var material = new ForestMaterial();
                using var wood = new VertexBuffer(PrimitiveType.Triangles);
                using var crown = new VertexBuffer(PrimitiveType.Triangles);
                var woodVertices = new List<Vertex>(); var crownVertices = new List<Vertex>();
                for (int i = 0; i < 3; i++)
                {
                    var mesh = i == 2
                        ? DendroTreeGenerator.GenerateShrub(42, TreeLifeStage.Mature, 0.85f, new(0.12f, 4.5f, 3.3f), 0, ForestLod.Near)
                        : DendroTreeGenerator.Generate(i == 0 ? ForestSpecies.Oak : ForestSpecies.Spruce,
                            42, TreeLifeStage.Mature, 0.85f, new(0.65f, 21, i == 0 ? 6 : 4.5f), 0, ForestLod.Near);
                    Vector3 offset = new((i - 1) * 6, 0, 0);
                    Add(mesh.Trunk, woodVertices); Add(mesh.Branches, woodVertices); Add(mesh.Crown, crownVertices);
                    void Add(Vertex[] input, List<Vertex> output)
                    { foreach (var v in input) output.Add(new(v.Position + offset, v.Normal, v.Color)); }
                }
                wood.SetData(woodVertices.ToArray()); crown.SetData(crownVertices.ToArray());
                visuals.Kind = SurfaceKind.Wood; RenderDevice.UseGeometryShader(); wood.DrawArray(false);
                material.Use(); crown.DrawArray(false);
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Vegetation preview GL error.");
                string output = Path.GetFullPath("artifacts/vegetation-benchmark/vegetation.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)); FramebufferCapture.SavePng(output, 1200, 600);
                Console.WriteLine(output);
            }
            finally { RenderDevice.Visuals = null; RenderDevice.Dispose(); }
        }
    }
}
