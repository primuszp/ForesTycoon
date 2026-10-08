using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon.Rendering.OpenGl
{
    internal sealed class OpenGlGeometryProgram : IDisposable
    {
        private int shader, viewProjectionLocation, modelLocation;
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
        internal void Use()
        {
            if (!initialized) throw new InvalidOperationException("Graphics backend is not initialized.");
            if (RenderDevice.Visuals?.Active == true) { RenderDevice.Visuals.Use(); return; }
            GL.UseProgram(shader);
            GL.Uniform2(GlProgram.Uniform(shader,"lod_range"),RenderDevice.LodRange);
            Matrix4 viewProjection = RenderDevice.ViewProjection;
            Matrix4 model = RenderDevice.Model;
            GL.UniformMatrix4(viewProjectionLocation, false, ref viewProjection);
            GL.UniformMatrix4(modelLocation, false, ref model);
        }
        public void Dispose() { if (shader != 0) GlProgram.Delete(shader); shader = 0; initialized = false; }
    }
}
