using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    /// <summary>Shared core-profile geometry shader and explicit camera/model state.</summary>
    static class RenderDevice
    {
        private static readonly Stack<Matrix4> modelStack = new Stack<Matrix4>();
        private static int shader;
        private static int viewProjectionLocation;
        private static int modelLocation;
        private static bool initialized;

        public static Matrix4 ViewProjection { get; private set; } = Matrix4.Identity;
        public static Matrix4 Model { get; private set; } = Matrix4.Identity;
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

            int vertex = Compile(ShaderType.VertexShader, vertexSource);
            int fragment = Compile(ShaderType.FragmentShader, fragmentSource);
            shader = GL.CreateProgram();
            GL.AttachShader(shader, vertex);
            GL.AttachShader(shader, fragment);
            GL.LinkProgram(shader);
            GL.GetProgram(shader, GetProgramParameterName.LinkStatus, out int linked);
            string log = GL.GetProgramInfoLog(shader);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
            if (linked == 0) throw new InvalidOperationException("Geometry shader link failed: " + log);

            viewProjectionLocation = GlProgram.Uniform(shader, "view_projection");
            modelLocation = GlProgram.Uniform(shader, "model");
            initialized = true;
        }

        public static void SetCamera(Matrix4 viewProjection)
        {
            EnsureInitialized();
            ViewProjection = viewProjection;
            Model = Matrix4.Identity;
            modelStack.Clear();
        }

        /// <summary>Replaces only the camera matrix; the model stack is left untouched.</summary>
        internal static void SetViewProjection(Matrix4 viewProjection) => ViewProjection = viewProjection;

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

        public static void PushModel() => modelStack.Push(Model);

        public static void PopModel()
        {
            if (modelStack.Count == 0) throw new InvalidOperationException("Render model stack underflow.");
            Model = modelStack.Pop();
        }

        internal static void SetModel(Matrix4 matrix) => Model = matrix;

        public static void Translate(float x, float y, float z) =>
            Model = Matrix4.CreateTranslation(x, y, z) * Model;

        public static void Dispose()
        {
            if (!initialized) return;
            Disposing?.Invoke();
            Disposing = null;
            DynamicPrimitiveBatch.DisposeDeviceResources();
            GlProgram.Delete(shader);
            shader = 0;
            initialized = false;
            modelStack.Clear();
            Visuals = null;
        }

        private static int Compile(ShaderType type, string source)
        {
            int handle = GL.CreateShader(type);
            GL.ShaderSource(handle, source);
            GL.CompileShader(handle);
            GL.GetShader(handle, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0)
            {
                string log = GL.GetShaderInfoLog(handle);
                GL.DeleteShader(handle);
                throw new InvalidOperationException($"Geometry {type} compilation failed: {log}");
            }
            return handle;
        }

        private static void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("RenderDevice.Initialize must be called with a current GL context.");
        }
    }
}
