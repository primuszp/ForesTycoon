using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon.Rendering.OpenGl
{
    internal sealed class OpenGlGeometryProgram : IDisposable
    {
        private int shader, viewProjectionLocation, modelLocation, lodRangeLocation;
        private bool initialized;
        internal void Initialize()
        {
            if (initialized) return;
            string vertexSource = @"#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in vec4 in_color;
uniform mat4 view_projection;
uniform mat4 model;
out vec4 vertex_color;
" + OpenGlForestGrowthShader.Shader + @"
void main()
{
    gl_Position = view_projection * model * vec4(forestPoint(in_position), 1.0);
    vertex_color = forestTint(in_color);
}";
            string fragmentSource = @"#version 330 core
" + OpenGlForestGrowthShader.FragmentShader + OpenGlForestGrowthShader.LodMaskShader + @"
in vec4 vertex_color;
out vec4 output_color;
void main()
{
    forestLodMask();
    forestLeafMask();
    output_color = vertex_color;
}";

            shader = GlProgram.Create(vertexSource, fragmentSource);

            viewProjectionLocation = GlProgram.Uniform(shader, "view_projection");
            modelLocation = GlProgram.Uniform(shader, "model");
            lodRangeLocation = GlProgram.Uniform(shader, "lod_range");
            initialized = true;
        }
        internal void Use()
        {
            if (!initialized) throw new InvalidOperationException("Graphics backend is not initialized.");
            if (RenderDevice.Visuals?.Active == true) { RenderDevice.Visuals.Use(); return; }
            GL.UseProgram(shader);
            GL.Uniform2(lodRangeLocation, RenderDevice.LodRange);
            Matrix4 viewProjection = RenderDevice.ViewProjection;
            Matrix4 model = RenderDevice.Model;
            GL.UniformMatrix4(viewProjectionLocation, false, ref viewProjection);
            GL.UniformMatrix4(modelLocation, false, ref model);
        }
        public void Dispose() { if (shader != 0) GlProgram.Delete(shader); shader = 0; initialized = false; }
    }
}
