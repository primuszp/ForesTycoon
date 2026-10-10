using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.OpenGl
{
    // Kept separate from the terrain material: only crown buffers use this shader.
    internal sealed class OpenGlForestMaterial : IForestMaterialBackend
    {
        private readonly RenderResourceOwner owner = new();
        private bool disposed;
        private int program;
        private int matrixLocation, widthLocation;
        public void Use(float outlineWorldWidth = 0)
        {
            ObjectDisposedException.ThrowIf(disposed, this); owner.Check();
            if (RenderDevice.Visuals?.Active == true)
            {
                RenderDevice.Visuals.Kind = SurfaceKind.Foliage;
                RenderDevice.Visuals.Use(outlineWorldWidth);
                return;
            }
            if (program == 0) Initialize();
            GL.UseProgram(program);
            GL.Uniform2(GlProgram.Uniform(program,"lod_range"),RenderDevice.LodRange);
            Matrix4 matrix = RenderDevice.Model * RenderDevice.ViewProjection;
            GL.UniformMatrix4(matrixLocation, false, ref matrix);
            GL.Uniform1(widthLocation, outlineWorldWidth);
        }
        private void Initialize()
        {
            string vertexSource = @"#version 330 core
layout(location=0) in vec3 position;
layout(location=1) in vec4 color;
layout(location=2) in vec3 normal;
uniform mat4 matrix;
uniform float outline_width;
out vec4 tint;
" + OpenGlForestGrowthShader.Shader + @"
void main() {
    gl_Position = matrix * vec4(forestPoint(position) + forestNormal(normal) * outline_width, 1);
    tint = outline_width > 0 ? vec4(0.075, 0.12, 0.045, 1) : forestTint(color);
    tint.a=color.a;
}";
            string fragmentSource = @"#version 330 core
in vec4 tint;
out vec4 output_color;
uniform vec2 lod_range;

void lodMask(){
    float rank=fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(0.06711056,0.00583715))));
    if(rank<lod_range.x||rank>=lod_range.y)discard;
}
void main() { lodMask();
    output_color = vec4(tint.rgb,1); }";
            try
            {
                program = GlProgram.Create(vertexSource, fragmentSource);
                matrixLocation = GlProgram.Uniform(program, "matrix");
                widthLocation = GlProgram.Uniform(program, "outline_width");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { if (disposed) return; owner.CheckIfBound(); disposed = true; if (program != 0) GlProgram.Delete(program); program = 0; }
    }
}
