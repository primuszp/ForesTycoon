using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class DendroTreeSmokeTest
    {
        internal static void Run()
        {
            const int size = 256;
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(size, size), API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                GL.Viewport(0, 0, size, size); GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
                RenderDevice.SetCamera(Matrix4.Identity);
                uint green = (uint)Terrain.SurfaceSpeciesCode(ForestSpecies.Oak) << 24 | 0x0040c020;
                using var quad = new VertexBuffer(PrimitiveTopology.Triangles);
                quad.SetData(new[] {
                    V(-1,-1), V(1,-1), V(1,1), V(-1,-1), V(1,1), V(-1,1) });
                var settings = new GraphicsSettings { Enhanced = true, Textures = true,
                    Lighting = false, Shadows = false, Weather = false, Quality = GraphicsQuality.Low };
                using var visuals = new SurfaceVisualRenderer(settings, new WeatherVisualState());
                using var material = new ForestMaterial();
                RenderDevice.Visuals = visuals; visuals.BeginFrame();
                Clear(); material.Use(); quad.DrawArray(false);
                bool[] main = Coverage();
                Require(main.All(v => v),
                    "Opaque crown must cover every rasterized pixel.");
                VerifyDepth(main);
                string output = Path.GetFullPath("artifacts/dendro-trees"); Directory.CreateDirectory(output);
                FramebufferCapture.SavePng(Path.Combine(output, "crown-opaque.png"), size, size);
                settings.Textures = false; visuals.BeginFrame();
                Clear(); material.Use(); quad.DrawArray(false);
                Require(main.SequenceEqual(Coverage()), "Disabling color textures changed crown coverage.");
                settings.Enhanced = false;
                Clear(); material.Use(); quad.DrawArray(false);
                Require(main.SequenceEqual(Coverage()), "Legacy crown coverage differs from enhanced coverage.");

                var speciesImages = new System.Collections.Generic.HashSet<string>();
                settings.Enhanced = true;
                foreach (var species in Enum.GetValues<ForestSpecies>().Where(s => s != ForestSpecies.None))
                {
                    green = (uint)TreeScale.CrownSpeciesCode(species) << 24 | 0x0040c020;
                    quad.SetData(new[] { V(-1,-1), V(1,-1), V(1,1), V(-1,-1), V(1,1), V(-1,1) });
                    settings.Textures = true; visuals.BeginFrame();
                    Clear(); material.Use(); quad.DrawArray(false);
                    byte[] textured = Pixels();
                    Require(main.SequenceEqual(Coverage()), "Species foliage texture changed the crown silhouette.");
                    VerifyDepth(main);
                    Require(speciesImages.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(textured))),
                        "Two species rendered the same foliage material.");
                    FramebufferCapture.SavePng(Path.Combine(output, "foliage-" + species + ".png"), size, size);
                    settings.Textures = false; visuals.BeginFrame();
                    Clear(); material.Use(); quad.DrawArray(false);
                    Require(!textured.AsSpan().SequenceEqual(Pixels()), "Species foliage texture has no visible effect.");
                    settings.Textures = true; visuals.BeginFrame();
                    Clear(); material.Use(); quad.DrawArray(false);
                    Require(textured.AsSpan().SequenceEqual(Pixels()), "Species foliage texture did not restore exactly.");
                }
                Console.WriteLine("Species foliage: 16 distinct materials, texture toggles/restoration and unchanged opaque color/depth coverage passed.");

                settings.Enhanced = true; settings.Lighting = true; settings.Shadows = true;
                visuals.BeginFrame();
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                bool[] shadow = null;
                visuals.RenderShadows(terrain, () => {
                    // Cancel only the light-camera projection to test the same UV raster.
                    RenderDevice.SetModel(visuals.ShadowCamera.Inverted());
                    material.Use(); quad.DrawArray(false);
                    GL.GetInteger(GetPName.DrawFramebufferBinding, out int framebuffer);
                    GL.GetInteger(GetPName.ReadFramebufferBinding, out int previous);
                    GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
                    try {
                        int shadowSize = settings.ShadowResolution;
                        float[] depth = new float[shadowSize * shadowSize];
                        GL.ReadPixels(0, 0, shadowSize, shadowSize, PixelFormat.DepthComponent, PixelType.Float, depth);
                        shadow = depth.Select(d => d < 0.99f).ToArray();
                    }
                    finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous); RenderDevice.SetModel(Matrix4.Identity); }
                });
                // Both framebuffer sizes must be fully covered by the opaque quad.
                Require(shadow != null && shadow.All(v => v), "Opaque crown must cast a solid shadow.");
                Require(Math.Abs(main.Count(v => v) / (double)main.Length - shadow.Count(v => v) / (double)shadow.Length) < 0.03,
                    "Crown shadow and color pass coverage diverged.");
                Require(GL.GetError() == ErrorCode.NoError, "Dendro crown OpenGL error.");
                RenderDevice.Visuals = null;
                CheckSeasonalGpuCrowns();
                CheckLightRebuilds();
                Console.WriteLine($"Dendro crown GL smoke passed: {main.Count(v => !v)} uncovered pixels; opaque color/legacy coverage, depth writes and solid shadow.");
                Vertex V(float x, float y) => new(new(x, y, 0), Vector3.UnitZ, green);
                void Clear() { GL.DepthMask(true); GL.ClearColor(1, 0, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); }
                byte[] Pixels()
                {
                    byte[] pixels = new byte[size * size * 4];
                    GL.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return pixels;
                }
                bool[] Coverage()
                {
                    byte[] pixels = new byte[size * size * 4];
                    GL.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return Enumerable.Range(0, size * size).Select(i => pixels[i * 4 + 1] > 0).ToArray();
                }
                void VerifyDepth(bool[] expected)
                {
                    float[] depth = new float[size * size];
                    GL.ReadPixels(0, 0, size, size, PixelFormat.DepthComponent, PixelType.Float, depth);
                    Require(expected.SequenceEqual(depth.Select(d => d < 0.99f)), "Opaque crown color and depth coverage differ.");
                }
            }
            finally { RenderDevice.Visuals = null; RenderDevice.Dispose(); }
        }
        private static void CheckSeasonalGpuCrowns()
        {
            const int size = 256;
            using var quad = new VertexBuffer(PrimitiveTopology.Triangles);
            using var state = RenderDevice.CreateForestStateBuffer();
            using var material = new ForestMaterial();
            var settings = new GraphicsSettings { Weather = false, Lighting = false, Shadows = true,
                Quality = GraphicsQuality.Low, Textures = true };
            using var visuals = new SurfaceVisualRenderer(settings, new WeatherVisualState());
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
            var autumnColours = new System.Collections.Generic.HashSet<string>();
            try
            {
                RenderDevice.Visuals = visuals; RenderDevice.SetCamera(Matrix4.Identity);
                foreach (var species in Enum.GetValues<ForestSpecies>().Where(s => s != ForestSpecies.None))
                {
                    var spec = new TreeShapeSpec(species, 42, TreeLifePhase.Mature,
                        ForestTreeGrowth.Initial(species, 40, 1), 1, TreeSite.Open, LeafState.Full, 0);
                    uint colour = new TreeForm(spec).CrownColor;
                    Vertex V(float x, float y) => new(new Vector3(x, y, 0), Vector3.UnitZ, colour);
                    quad.SetData(new[] { V(-1, -1), V(1, -1), V(1, 1), V(-1, -1), V(1, 1), V(-1, 1) });
                    quad.SetForestGrowth(Enumerable.Repeat(new ForestVertexGrowth(Vector3.Zero, new Vector3(-1, 0, 0), 0), 6).ToArray());
                    var season = ForestSeasonRenderState.Create(spec);
                    state.SetData(new[] { Vector4.One, new Vector4(0, 0, 0, 1), season.Tint, season.Bounds }, 4);
                    quad.ForestState = state;
                    byte[] summer = Frame(.3f, true);
                    byte[] autumn = Frame(.56f, true);
                    bool evergreen = TreePhenology.Evergreen(species);
                    if (!evergreen)
                    {
                        Require(!summer.AsSpan().SequenceEqual(autumn), $"{species} did not change autumn foliage colour.");
                        Require(autumnColours.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(autumn))),
                            $"{species} did not have a distinct autumn appearance.");
                    }
                    foreach (float winter in new[] { .75f, .85f, .97f })
                    foreach (bool enhanced in new[] { true, false })
                    {
                        byte[] pixels = Frame(winter, enhanced);
                        Require(Count(pixels) == (evergreen ? size * size : 0),
                            $"{species} winter foliage coverage is wrong at {winter}, enhanced={enhanced}.");
                    }
                    settings.Enhanced = true; settings.Lighting = true; visuals.BeginFrame();
                    quad.ForestCurrentYear = .85f;
                    int shadowPixels = -1;
                    visuals.RenderShadows(terrain, () => {
                        RenderDevice.SetModel(visuals.ShadowCamera.Inverted());
                        material.Use(); quad.DrawArray(false);
                        GL.GetInteger(GetPName.DrawFramebufferBinding, out int framebuffer);
                        GL.GetInteger(GetPName.ReadFramebufferBinding, out int previous);
                        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
                        try {
                            float[] depth = new float[settings.ShadowResolution * settings.ShadowResolution];
                            GL.ReadPixels(0, 0, settings.ShadowResolution, settings.ShadowResolution, PixelFormat.DepthComponent, PixelType.Float, depth);
                            shadowPixels = depth.Count(d => d < .99f);
                        }
                        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous); RenderDevice.SetModel(Matrix4.Identity); }
                    });
                    Require(shadowPixels == (evergreen ? settings.ShadowResolution * settings.ShadowResolution : 0),
                        $"{species} leafless winter crown cast a leaf shadow.");
                    Require(summer.AsSpan().SequenceEqual(Frame(1.3f, true)), $"{species} foliage did not return identically next summer.");
                }
                Require(GL.GetError() == ErrorCode.NoError, "Seasonal GPU foliage OpenGL error.");
                Console.WriteLine("GPU phenology: 16 species, distinct autumn colours, all-winter leaflessness, evergreen needles, matching shadows and exact next-summer restoration without mesh changes passed.");
            }
            finally { RenderDevice.Visuals = null; }

            byte[] Frame(float year, bool enhanced)
            {
                settings.Enhanced = enhanced; settings.Lighting = false; visuals.BeginFrame();
                quad.ForestCurrentYear = year; GL.Viewport(0, 0, size, size);
                RenderDevice.Clear(new Vector4(0, 0, 0, 1)); material.Use(); quad.DrawArray(false);
                var pixels = new byte[size * size * 4];
                GL.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                return pixels;
            }
            static int Count(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4)
                .Count(i => pixels[i * 4] != 0 || pixels[i * 4 + 1] != 0 || pixels[i * 4 + 2] != 0);
        }

        private static void CheckLightRebuilds()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42), (_, _) => 4);
            var stands = new ForestStand[1024];
            int[] ids = { 34, 50, 546, 562 }; // One occupied tile in each of four chunks.
            foreach (int id in ids) stands[id] = new(ForestSpecies.Oak, 40, 0.5f, 1);
            var forest = new ForestSystem(terrain.Map, stands);
            void Light(float light)
            {
                foreach (int id in ids)
                {
                    forest.IndividualTrees.TryGet(id, out var patch);
                    for (int i = 0; i < patch.Count; i++) patch.Trees[i] = patch.Trees[i] with { Resources = new(light, 1, 1) };
                }
            }
            Light(1); forest.NotifyIndividualVisualEdit();
            var graphics = new GraphicsSettings { Enhanced = false };
            var context = new RenderContext(0,0,0,0,0,0,false,false,1,-60,-45,-10000,-10000,10000,10000,12);
            terrain.UpdateVisibleTiles(context); terrain.WarmIndividualForest(forest, graphics);
            long before = terrain.TotalForestChunkRebuilds;
            forest.RefreshEnvironmentRates(); // Ordinary resource revision, no topology edit.
            Light(0.1f);
            terrain.DrawTrees(forest, context, graphics);
            Require(terrain.ForestChunkRebuilds == 0, "Light changes rebuilt chunks synchronously.");
            // Near replacements also schedule the adjacent Medium level. Drain both, rather
            // than treating a legitimate prefetched replacement as a repeated Near rebuild.
            for (int frame = 0; frame < 1000 && terrain.HasPendingForestBuild; frame++)
                terrain.DrawTrees(forest, context, graphics);
            Require(!terrain.HasPendingForestBuild && terrain.TotalForestChunkRebuilds - before == 8,
                "Light changes did not replace exactly four Near and four adjacent Medium chunks.");
            for (int frame = 0; frame < 3; frame++)
            {
                terrain.DrawTrees(forest, context, graphics);
                Require(terrain.ForestChunkRebuilds == 0, "Light geometry cache did not settle.");
            }
            Require(GL.GetError() == ErrorCode.NoError, "Light topology OpenGL error.");
            Console.WriteLine("Light topology: four Near and four adjacent Medium chunks published asynchronously once each, then stable reuse.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
