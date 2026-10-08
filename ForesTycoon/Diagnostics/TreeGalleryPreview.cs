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
    // Close-up, open-grown individuals of every main species and phase, each normalised to its
    // cell. Used for before/after comparison of crown meshing (docs/tree-realism-plan.md).
    internal static class TreeGalleryPreview
    {
        private static readonly ForestSpecies[] Species = { ForestSpecies.Oak, ForestSpecies.Beech, ForestSpecies.Birch,
            ForestSpecies.Maple, ForestSpecies.Ash, ForestSpecies.Pine, ForestSpecies.Spruce, ForestSpecies.Hazel };
        private static readonly TreeLifePhase[] Phases = { TreeLifePhase.Young, TreeLifePhase.Mature, TreeLifePhase.Old };

        internal static void Run(string name, ForestLod lod = ForestLod.Near)
        {
            const int width = 1600, height = 900;
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new(width, height), NumberOfSamples = 4, API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                GL.Viewport(0, 0, width, height); GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace);
                GL.ClearColor(0.19f, 0.23f, 0.27f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                // Slightly raised view, close to the in-game isometric camera.
                RenderDevice.SetCamera(Matrix4.CreateRotationX(-MathF.PI / 2.5f)
                    * Matrix4.CreateOrthographicOffCenter(0, Species.Length, -0.1f, Species.Length * (float)height / width - 0.1f, -100, 100));
                using var visuals = new SurfaceVisualRenderer(new GraphicsSettings {
                    Shadows = false, Weather = false, Fog = false, Textures = true }, new WeatherVisualState());
                RenderDevice.Visuals = visuals; visuals.BeginFrame();
                using var material = new ForestMaterial();
                using var wood = new VertexBuffer(PrimitiveTopology.Triangles);
                using var crown = new VertexBuffer(PrimitiveTopology.Triangles);
                var woodVertices = new List<Vertex>(); var crownVertices = new List<Vertex>();
                int crownTriangles = 0;
                for (int row = 0; row < Phases.Length; row++)
                for (int col = 0; col < Species.Length; col++)
                {
                    var species = Species[col]; var phase = Phases[row];
                    var profile = ForestSpeciesProfile.For(species);
                    float age = phase == TreeLifePhase.Young ? profile.MatureAgeYears * 0.3f
                        : phase == TreeLifePhase.Mature ? profile.MatureAgeYears : profile.MaximumAgeYears * 0.75f;
                    var size = ForestTreeGrowth.Initial(species, age, 1);
                    var spec = new TreeShapeSpec(species, 42 + col, phase, size, 1, new TreeSite(1), LeafState.Full, 0.6f);
                    var mesh = DendroTreeGenerator.Generate(spec, lod);
                    crownTriangles += mesh.Crown.Length / 3;
                    float scale = 0.8f / Math.Max(size.Height * TreeScale.MetresToWorld, size.CrownRadius * 2 * TreeScale.MetresToWorld);
                    Vector3 offset = new(col + 0.5f, 0, (Phases.Length - 1 - row) * 1.45f);
                    foreach (var v in mesh.Trunk) woodVertices.Add(new(v.Position * scale + offset, v.Normal, v.Color));
                    foreach (var v in mesh.Branches) woodVertices.Add(new(v.Position * scale + offset, v.Normal, v.Color));
                    foreach (var v in mesh.Crown) crownVertices.Add(new(v.Position * scale + offset, v.Normal, v.Color));
                }
                wood.SetData(woodVertices.ToArray()); crown.SetData(crownVertices.ToArray());
                visuals.Kind = SurfaceKind.Wood; RenderDevice.UseGeometryShader(); wood.DrawArray(false);
                material.Use(); crown.DrawArray(false);
                using var ui = new ImGuiController();
                ui.Update(width, height, width, height, System.Numerics.Vector2.One, 1f / 60);
                var draw = ImGui.GetBackgroundDrawList();
                for (int col = 0; col < Species.Length; col++)
                    draw.AddText(new(col * width / Species.Length + 12, 8), 0xffffffff, Species[col].ToString());
                for (int row = 0; row < Phases.Length; row++)
                    draw.AddText(new(8, 28 + row * height / Phases.Length), 0xffb0b0b0, Phases[row].ToString());
                ui.Render();
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Tree gallery GL error.");
                string output = Path.GetFullPath("artifacts/tree-gallery"); Directory.CreateDirectory(output);
                string file = Path.Combine(output, $"{name}-{lod}.png".ToLowerInvariant());
                FramebufferCapture.SavePng(file, width, height);
                Console.WriteLine($"Tree gallery: {file} ({crownTriangles} crown triangles)");
            }
            finally { RenderDevice.Visuals = null; RenderDevice.Dispose(); }
        }
    }
}
