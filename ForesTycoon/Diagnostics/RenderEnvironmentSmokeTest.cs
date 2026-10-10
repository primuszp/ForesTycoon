using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ForesTycoon.Rendering.OpenGl;

namespace ForesTycoon
{
    internal static class RenderEnvironmentSmokeTest
    {
        internal static void Run()
        {
            using var first = Window(); using var second = Window();
            var a = new RenderEnvironment(new OpenGlGraphicsBackend(), () => Require(first.Context.IsCurrent, "First native context is not current."));
            var b = new RenderEnvironment(new OpenGlGraphicsBackend(), () => Require(second.Context.IsCurrent, "Second native context is not current."));
            first.Context.MakeCurrent();
            using (a.Activate()) {
                int programA = GlProgram.Create(VertexShader, "#version 330 core\nuniform float marker; out vec4 color; void main(){color=vec4(marker);}");
                Require(GlProgram.Uniform(programA, "marker") >= 0, "First marker uniform is missing.");
                RenderDevice.Initialize(); RenderDevice.InitializeFrameState(); RenderDevice.SetViewport(32, 32);
                RenderDevice.SetCamera(Matrix4.Identity);
                var red = Buffer(0xff0000ff);
                Draw(red, 0); var firstHandles = CurrentHandles();
                var fixtureA = new SceneFixture(); fixtureA.Draw(); var modelA = fixtureA.ModelHandles;
                second.Context.MakeCurrent();
                using (b.Activate()) {
                    int programB = GlProgram.Create(VertexShader, "#version 330 core\nout vec4 color; void main(){color=vec4(1);}");
                    Require(programA == programB, "Fixture did not exercise overlapping native program names.");
                    Require(GlProgram.Uniform(programB, "marker") == -1, "Uniform cache leaked between native contexts.");
                    RenderDevice.Initialize(); RenderDevice.InitializeFrameState(); RenderDevice.SetViewport(32, 32);
                    RenderDevice.SetCamera(Matrix4.Identity);
                    var green = Buffer(0xff00ff00); Draw(green, 1);
                    var fixtureB = new SceneFixture(); fixtureB.Draw(); var modelB = fixtureB.ModelHandles;
                    RequireRejected(() => red.DrawArray()); RequireRejected(() => red.Dispose());
                    fixtureA.RejectForeignAccess();
                    RenderDevice.SetCamera(Matrix4.Identity); Draw(green, 1); var secondHandles = CurrentHandles();
                    fixtureB.Dispose(); RequireModelDeleted(modelB);
                    green.Dispose(); GlProgram.Delete(programB); b.Dispose(); b.Dispose();
                    Require(!GL.IsProgram(secondHandles.Program) && !GL.IsBuffer(secondHandles.Buffer), "Second environment retained its device resources.");
                    Require(GL.GetError() == ErrorCode.NoError, "Second context produced a GL error.");
                }
                // Restoring the managed scope cannot silently substitute the native context.
                RequireRejected(() => red.DrawArray()); first.Context.MakeCurrent();
                Require(GL.IsProgram(firstHandles.Program) && GL.IsBuffer(firstHandles.Buffer), "Closing the second environment deleted the first's resources.");
                Require(GL.IsProgram(modelA.Program), "Closing the second environment deleted the first model.");
                fixtureA.Draw(); fixtureA.Dispose(); RequireModelDeleted(modelA);
                RenderDevice.SetCamera(Matrix4.Identity); Draw(red, 0); red.Dispose(); GlProgram.Delete(programA); a.Dispose(); a.Dispose();
                Require(!GL.IsProgram(firstHandles.Program) && !GL.IsBuffer(firstHandles.Buffer), "First environment retained its device resources.");
                Require(GL.GetError() == ErrorCode.NoError, "First context produced a GL error.");
            }
            Console.WriteLine("Render environments: two native contexts, overlapping names, isolated uniforms/pixels, worlds/models/weather/postprocess/UI, foreign access rejection and independent disposal passed.");
        }

        private sealed class SceneFixture : IDisposable
        {
            private readonly GameWorld world = new(TerrainSettings.Default.WithNodeSize(5, 42));
            private readonly AnimatedGlbModel model = AnimatedGlbModel.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Vehicles", "log-truck.glb"));
            private readonly ForesTycoon.Models.OpenGl.OpenGlModelRenderer modelBackend;
            private readonly AnimatedModelRenderer modelRenderer;
            private readonly AnimatedGlbModel.Pose pose;
            private readonly IModelRenderBatch batch;
            private readonly ForesTycoon.Effects.OpenGl.OpenGlWeatherRenderer rainBackend = new();
            private readonly ForesTycoon.Effects.OpenGl.OpenGlCloudRenderer cloudBackend = new();
            private readonly WeatherRenderer rain;
            private readonly CloudRenderer clouds;
            private readonly DioramaPostProcess post = new();
            private readonly ImGuiController ui = new();
            private readonly Surface surface = new();
            private readonly WeatherVisualState weather = new();
            private readonly RenderPassProfiler profiler = new();
            private readonly RenderContext context = new(9, 0, 0, 9, 0, 0, false, false, 1, -60, -45, -1000, -1000, 1000, 1000, 16);
            internal (int Program, int BoneBuffer, int[] VertexArrays, int[] VertexBuffers, int[] IndexBuffers, int[] Textures) ModelHandles => modelBackend.CaptureResources();
            internal SceneFixture()
            {
                rain = new(rainBackend); clouds = new(cloudBackend);
                world.Graphics.Enhanced = true; world.Graphics.Quality = GraphicsQuality.Low;
                world.Graphics.Lighting = false; world.Graphics.AutomaticWeather = false; world.Graphics.Preset = WeatherPreset.Storm;
                world.Graphics.AmbientOcclusion = false; world.Graphics.Diorama = true;
                modelBackend = new(model); modelRenderer = new(model, modelBackend);
                pose = model.CreatePose(); pose.Evaluate(null, 0); batch = modelRenderer.BeginBatch();
                for (int i = 0; i < 10; i++) weather.Update(i, world.Graphics);
            }
            internal void Draw()
            {
                profiler.BeginFrame();
                Require(post.Begin(world.Graphics, 32, 32), "Postprocess fixture did not initialize.");
                post.DrawBackdrop(world.Graphics, Vector3.One);
                RenderDevice.SetCamera(Matrix4.CreateScale(.1f)); world.Draw(context);
                RenderMetrics.BeginFrame(); modelRenderer.Draw(pose, Matrix4.Identity, world.Graphics, sharedState: batch);
                Require(RenderMetrics.DrawCalls > 0 && modelBackend.HasGpuResources, "Model fixture did not submit GPU draws.");
                rain.Draw(surface, weather, context, world.Graphics); clouds.Draw(surface, weather, world.Graphics);
                Require(rain.Metrics.Particles > 0 && clouds.Metrics.CloudSteps > 0, "Weather fixture did not submit its effects.");
                post.End(world.Graphics, 16, 1, 9);
                ui.Update(32, 32, 32, 32, System.Numerics.Vector2.One, 1f / 60);
                ImGuiNET.ImGui.Begin("Context fixture"); ImGuiNET.ImGui.Text("Owned UI"); ImGuiNET.ImGui.End(); ui.Render();
                GL.Finish(); Require(profiler.ReadCompletedFrame().Length > 0, "Frame profiler did not record owned queries.");
            }
            internal void RejectForeignAccess()
            {
                RequireRejected(() => profiler.BeginFrame()); RequireRejected(() => profiler.ReadCompletedFrame()); RequireRejected(() => profiler.Dispose());
                RequireRejected(() => world.Draw(context)); RequireRejected(() => world.Update(.1)); RequireRejected(() => world.Dispose());
                RequireRejected(() => modelRenderer.Draw(pose, Matrix4.Identity, world.Graphics, sharedState: batch));
                RequireRejected(() => modelRenderer.Dispose()); RequireRejected(() => batch.Dispose());
                RequireRejected(() => rain.Draw(surface, weather, context, world.Graphics)); RequireRejected(() => rain.Dispose());
                RequireRejected(() => clouds.Draw(surface, weather, world.Graphics)); RequireRejected(() => clouds.Dispose());
                RequireRejected(() => post.Begin(world.Graphics, 32, 32)); RequireRejected(() => post.Dispose()); RequireRejected(() => ui.Dispose());
            }
            public void Dispose()
            {
                var rainHandles = rainBackend.CaptureResources(); var cloudHandles = cloudBackend.CaptureResources();
                var queries = profiler.CaptureQueries(); profiler.Dispose(); profiler.Dispose();
                ui.Dispose(); batch.Dispose(); modelRenderer.Dispose(); rain.Dispose(); clouds.Dispose(); post.Dispose(); world.Dispose();
                Require(!GL.IsProgram(rainHandles.Program) && !GL.IsVertexArray(rainHandles.VertexArray) && !GL.IsTexture(rainHandles.HeightTexture), "Rain retained native resources.");
                Require(!GL.IsProgram(cloudHandles.Program) && !GL.IsVertexArray(cloudHandles.VertexArray), "Clouds retained native resources.");
                Require(rain.Metrics.GpuPayloadBytes == 0 && clouds.Metrics.GpuPayloadBytes == 0, "Disposed weather retained its metrics.");
                foreach (int query in queries) Require(!GL.IsQuery(query), "Profiler retained a native query.");
            }
        }
        private sealed class Surface : IWeatherSurface
        {
            public int Columns => 2; public int Rows => 2; public ulong Revision => 1;
            public void FillHeights(float[] heights) => Array.Fill(heights, 0);
            public void GetBounds(out Vector3 min, out Vector3 max) { min = new(-1, -1, 0); max = new(1, 1, 10); }
            public void GetVisibleBounds(out Vector2 min, out Vector2 max) { min = new(-1); max = new(1); }
        }
        private static void RequireModelDeleted((int Program, int BoneBuffer, int[] VertexArrays, int[] VertexBuffers, int[] IndexBuffers, int[] Textures) handles)
        {
            Require(!GL.IsProgram(handles.Program) && !GL.IsBuffer(handles.BoneBuffer), "Model retained program/palette resources.");
            foreach (int id in handles.VertexArrays) Require(!GL.IsVertexArray(id), "Model retained a VAO.");
            foreach (int id in handles.VertexBuffers) Require(!GL.IsBuffer(id), "Model retained a VBO.");
            foreach (int id in handles.IndexBuffers) Require(!GL.IsBuffer(id), "Model retained an index buffer.");
            foreach (int id in handles.Textures) Require(!GL.IsTexture(id), "Model retained a texture.");
        }

        private const string VertexShader = "#version 330 core\nvoid main(){gl_Position=vec4(0,0,0,1);}";
        private static NativeWindow Window() => new(new NativeWindowSettings {
            StartVisible = false, ClientSize = new Vector2i(32, 32), API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3), Profile = ContextProfile.Core
        });
        private static VertexBuffer Buffer(uint color)
        {
            var buffer = new VertexBuffer(PrimitiveTopology.Triangles);
            buffer.SetData(new[] {
                new Vertex(new Vector3(-1, -1, 0), Vector3.UnitZ, color),
                new Vertex(new Vector3(1, -1, 0), Vector3.UnitZ, color),
                new Vertex(new Vector3(0, 1, 0), Vector3.UnitZ, color)
            });
            return buffer;
        }
        private static void Draw(VertexBuffer buffer, int channel)
        {
            RenderDevice.Clear(new Vector4(0, 0, 0, 1)); buffer.DrawArray();
            var pixels = new byte[4]; RenderDevice.ReadPixels(16, 16, 1, 1, pixels);
            Require(pixels[channel] >= 250 && pixels[1 - channel] <= 5 && pixels[2] <= 5, "An environment drew another environment's pixels.");
        }
        private static (int Program, int Buffer) CurrentHandles()
        {
            GL.GetInteger(GetPName.CurrentProgram, out int program); GL.GetInteger(GetPName.ArrayBufferBinding, out int buffer);
            Require(program > 0 && buffer > 0, "Native draw did not bind its device resources."); return (program, buffer);
        }
        private static void RequireRejected(Action action)
        {
            bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Foreign render environment/native context access was accepted.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

