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
        private int matrixLocation, widthLocation, lodRangeLocation;
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
            GL.Uniform2(lodRangeLocation, RenderDevice.LodRange);
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
    vec4 leaf_tint = forestTint(color);
    tint = outline_width > 0 ? vec4(0.075, 0.12, 0.045, 1) : leaf_tint;
    tint.a=color.a;
}";
            string fragmentSource = @"#version 330 core
" + OpenGlForestGrowthShader.FragmentShader + OpenGlForestGrowthShader.LodMaskShader + @"
in vec4 tint;
out vec4 output_color;
void main() { forestLodMask(); forestLeafMask();
    output_color = vec4(tint.rgb,1); }";
            try
            {
                program = GlProgram.Create(vertexSource, fragmentSource);
                matrixLocation = GlProgram.Uniform(program, "matrix");
                widthLocation = GlProgram.Uniform(program, "outline_width");
                lodRangeLocation = GlProgram.Uniform(program, "lod_range");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { if (disposed) return; owner.CheckIfBound(); disposed = true; if (program != 0) GlProgram.Delete(program); program = 0; }
    }
}
