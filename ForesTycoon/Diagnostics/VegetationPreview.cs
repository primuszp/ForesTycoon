using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Direct generator contact sheet. Rows: spruce, oak, birch, beech, shrubs.
    // Columns: seedling, young, mature, old, mature in deep shade (shrubs: hazel/hawthorn
    // seeds). Every tree is normalized to its cell height, so silhouettes are comparable.
    internal static class VegetationPreview
    {
        private const int Columns = 5, Rows = 5, Cell = 200;

        internal static void Run()
        {
            int width = Columns * Cell, height = Rows * Cell;
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new(width, height), NumberOfSamples = 4, API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            Check("initialization");
            try
            {
                GL.Viewport(0, 0, width, height); GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace);
                GL.ClearColor(0.19f, 0.23f, 0.27f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                RenderDevice.SetCamera(Matrix4.CreateRotationX(-MathF.PI / 2.25f)
                    * Matrix4.CreateOrthographicOffCenter(0, Columns, -0.06f, Rows - 0.06f, -100, 100));
                using var visuals = new SurfaceVisualRenderer(new GraphicsSettings { Shadows = false, Weather = false,
                    Fog = false, Textures = true }, new WeatherVisualState());
                RenderDevice.Visuals = visuals; visuals.BeginFrame();
                Check("visuals");
                using var material = new ForestMaterial();
                Check("material");
                using var wood = new VertexBuffer(PrimitiveTopology.Triangles);
                using var crown = new VertexBuffer(PrimitiveTopology.Triangles);
                var woodVertices = new List<Vertex>(); var crownVertices = new List<Vertex>();
                var species = new[] { ForestSpecies.Spruce, ForestSpecies.Oak, ForestSpecies.Birch, ForestSpecies.Beech };
                Console.WriteLine("triangles near/medium/far (crown+wood)");
                for (int row = 0; row < Rows; row++)
                {
                    for (int column = 0; column < Columns; column++)
                    {
                        Func<ForestLod, DendroTreeGenerator.Mesh> build;
                        string label;
                        float boxHeight;
                        if (row == Rows - 1)
                        {
                            var form = column < 3 ? ShrubForm.Hazel : ShrubForm.Hawthorn;
                            var size = form == ShrubForm.Hazel ? new ForestTreeDimensions(0.05f, 4.5f, 2.6f) : new ForestTreeDimensions(0.12f, 5f, 2.3f);
                            int seed = 42 + column * 7;
                            build = lod => DendroTreeGenerator.GenerateShrub(seed, TreeLifeStage.Mature, 0.85f, size, 0.4f, lod, form);
                            label = form + "#" + seed; boxHeight = 0.62f;
                        }
                        else
                        {
                            var s = species[row];
                            var stage = column == 4 ? TreeLifeStage.Mature : (TreeLifeStage)column;
                            float light = column == 4 ? 0.15f : 0.85f;
                            var profile = ForestSpeciesProfile.For(s);
                            float age = stage switch
                            {
                                TreeLifeStage.Seedling => 1.5f, TreeLifeStage.Young => profile.MatureAgeYears * 0.4f,
                                TreeLifeStage.Mature => profile.MatureAgeYears, _ => profile.MaximumAgeYears * 0.75f
                            };
                            var size = ForestTreeGrowth.Initial(s, age, 1);
                            build = lod => DendroTreeGenerator.Generate(s, 42, stage, light, size, 0.4f, lod);
                            label = s + "/" + stage + (column == 4 ? "/shade" : ""); boxHeight = 0.86f;
                        }
                        var near = build(ForestLod.Near);
                        Console.WriteLine($"{label,-24} {Count(near)}/{Count(build(ForestLod.Medium))}/{Count(build(ForestLod.Far))}");
                        float top = 0;
                        foreach (var v in near.Crown) top = Math.Max(top, v.Position.Z);
                        foreach (var v in near.Trunk) top = Math.Max(top, v.Position.Z);
                        float scale = boxHeight / Math.Max(1e-4f, top);
                        Vector3 offset = new(column + 0.5f, 0, Rows - 1 - row);
                        Add(near.Trunk, woodVertices); Add(near.Branches, woodVertices); Add(near.Crown, crownVertices);
                        void Add(Vertex[] input, List<Vertex> output)
                        { foreach (var v in input) output.Add(new(v.Position * scale + offset, v.Normal, v.Color)); }
                    }
                }
                wood.SetData(woodVertices.ToArray()); crown.SetData(crownVertices.ToArray());
                Check("buffers");
                visuals.Kind = SurfaceKind.Wood; RenderDevice.UseGeometryShader(); wood.DrawArray(false);
                Check("wood");
                material.Use(); crown.DrawArray(false);
                Check("crown");
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Vegetation preview GL error.");
                string output = Path.GetFullPath("artifacts/vegetation-benchmark/vegetation.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)); FramebufferCapture.SavePng(output, width, height);
                Console.WriteLine(output);
            }
            finally { RenderDevice.Visuals = null; RenderDevice.Dispose(); }

            static string Count(DendroTreeGenerator.Mesh mesh) =>
                $"{mesh.Crown.Length / 3}+{(mesh.Trunk.Length + mesh.Branches.Length) / 3}";
            static void Check(string stage)
            {
                var error = GL.GetError();
                if (error != ErrorCode.NoError) throw new InvalidOperationException($"Vegetation preview {stage}: {error}");
            }
        }
    }
}
