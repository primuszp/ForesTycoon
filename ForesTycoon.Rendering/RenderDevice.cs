using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    /// <summary>Shared core-profile geometry shader and explicit camera/model state.</summary>
    static class RenderDevice
    {
        private static readonly RenderTransformState transforms = new RenderTransformState();
        private static int shader;
        private static int viewProjectionLocation;
        private static int modelLocation;
        private static bool initialized;

        public static Matrix4 ViewProjection => transforms.ViewProjection;
        public static Matrix4 Model => transforms.Model;
        internal static Vector2 LodRange = new Vector2(0,1);
        internal static ISurfaceVisuals Visuals { get; set; }
        /// <summary>Raised once when the device shuts down, so owners of GPU resources can release them first.</summary>
        internal static event Action Disposing;

        public static void Initialize()
        {
            if (initialized) return;
            string vertexSource = @"#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in vec4 in_color;
uniform mat4 view_projection;
uniform mat4 model;
out vec4 vertex_color;
" + ForestVertexGrowth.Shader + @"
void main()
{
    gl_Position = view_projection * model * vec4(forestPoint(in_position), 1.0);
    vertex_color = forestTint(in_color);
}";
            const string fragmentSource = @"#version 330 core
in vec4 vertex_color;
out vec4 output_color;
uniform vec2 lod_range;
void lodMask(){
    float rank=fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(0.06711056,0.00583715))));
    if(rank<lod_range.x||rank>=lod_range.y)discard;
}
void main()
{
    lodMask();
    output_color = vertex_color;
}";

            shader = GlProgram.Create(vertexSource, fragmentSource);

            viewProjectionLocation = GlProgram.Uniform(shader, "view_projection");
            modelLocation = GlProgram.Uniform(shader, "model");
            initialized = true;
        }

        public static void SetCamera(Matrix4 viewProjection)
        {
            EnsureInitialized();
            transforms.SetCamera(viewProjection);
        }

        /// <summary>Replaces only the camera matrix; the model stack is left untouched.</summary>
        internal static void SetViewProjection(Matrix4 viewProjection) => transforms.SetViewProjection(viewProjection);

        public static void UseGeometryShader()
        {
            EnsureInitialized();
            if (Visuals?.Active == true) { Visuals.Use(); return; }
            GL.UseProgram(shader);
            GL.Uniform2(GlProgram.Uniform(shader,"lod_range"),LodRange);
            Matrix4 viewProjection = ViewProjection;
            Matrix4 model = Model;
            GL.UniformMatrix4(viewProjectionLocation, false, ref viewProjection);
            GL.UniformMatrix4(modelLocation, false, ref model);
        }

        public static void PushModel() => transforms.PushModel();

        public static void PopModel() => transforms.PopModel();

        internal static void SetModel(Matrix4 matrix) => transforms.SetModel(matrix);

        public static void Translate(float x, float y, float z) =>
            transforms.Translate(x, y, z);

        public static void Dispose()
        {
            if (!initialized) return;
            Disposing?.Invoke();
            Disposing = null;
            DynamicPrimitiveBatch.DisposeDeviceResources();
            GlProgram.Delete(shader);
            shader = 0;
            initialized = false;
            transforms.Reset();
            Visuals = null;
        }

        private static void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("RenderDevice.Initialize must be called with a current GL context.");
        }
    }
}
