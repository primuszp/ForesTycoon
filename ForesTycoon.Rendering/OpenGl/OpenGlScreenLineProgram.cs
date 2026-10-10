using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering.OpenGl
{
    /// <summary>Cached line endpoints expanded on the GPU into antialiased pixel-width ribbons.</summary>
    internal sealed class OpenGlScreenLineProgram : IDisposable
    {
        private int program;
        private readonly int[] viewport = new int[4];
        internal void Use(float widthPixels, Vector4 colorOverride)
        {
            if (program == 0) program = GlProgram.Create(VertexSource, FragmentSource, GeometrySource);
            GL.UseProgram(program);
            Matrix4 camera = RenderDevice.ViewProjection, model = RenderDevice.Model;
            GL.UniformMatrix4(GlProgram.Uniform(program, "view_projection"), false, ref camera);
            GL.UniformMatrix4(GlProgram.Uniform(program, "model"), false, ref model);
            GL.GetInteger(GetPName.Viewport, viewport);
            GL.Uniform2(GlProgram.Uniform(program, "viewport_size"), (float)Math.Max(1, viewport[2]), (float)Math.Max(1, viewport[3]));
            GL.Uniform1(GlProgram.Uniform(program, "half_width"), widthPixels * .5f);
            GL.Uniform4(GlProgram.Uniform(program, "color_override"), colorOverride);
        }
        public void Dispose() { if (program != 0) GlProgram.Delete(program); program = 0; }
        private const string VertexSource = @"#version 330 core
layout(location=0) in vec3 in_position;
layout(location=1) in vec4 in_color;
uniform mat4 view_projection, model;
out vec4 line_color;
void main(){ gl_Position=view_projection*model*vec4(in_position,1); line_color=in_color; }";
        private const string GeometrySource = @"#version 330 core
layout(lines) in;
layout(triangle_strip,max_vertices=4) out;
in vec4 line_color[];
out vec4 color;
noperspective out float distance_pixels;
uniform vec2 viewport_size;
uniform float half_width;
void emitPoint(int endpoint, vec2 offset, float side){
    vec4 p=gl_in[endpoint].gl_Position;
    p.xy+=offset*p.w;
    // Pull the decal forward without moving its world-space endpoints.
    p.z-=0.00001*p.w;
    gl_Position=p; color=line_color[endpoint]; distance_pixels=side; EmitVertex();
}
void main(){
    vec4 a=gl_in[0].gl_Position,b=gl_in[1].gl_Position;
    if(a.w<=0 || b.w<=0) return;
    vec2 delta=(b.xy/b.w-a.xy/a.w)*viewport_size;
    float len=length(delta); if(len<0.001) return;
    vec2 normal=vec2(-delta.y,delta.x)/len;
    float extent=half_width+0.75;
    vec2 offset=normal*extent*2.0/viewport_size;
    emitPoint(0,offset,extent); emitPoint(0,-offset,-extent);
    emitPoint(1,offset,extent); emitPoint(1,-offset,-extent); EndPrimitive();
}";
        private const string FragmentSource = @"#version 330 core
in vec4 color;
noperspective in float distance_pixels;
uniform float half_width;
uniform vec4 color_override;
out vec4 output_color;
void main(){
    float coverage=1.0-smoothstep(half_width-0.25,half_width+0.75,abs(distance_pixels));
    vec4 tint=color_override.a>0.0?color_override:color;
    output_color=vec4(tint.rgb,tint.a*coverage);
}";
    }
}
