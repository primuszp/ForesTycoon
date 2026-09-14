using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Kept separate from the terrain material: only crown buffers use this shader.
    internal sealed class ForestMaterial : IDisposable
    {
        private int program;
        private int matrixLocation, widthLocation;
        internal void Use(float outlineWorldWidth = 0)
        {
            if (program == 0) Initialize();
            GL.UseProgram(program);
            Matrix4 matrix = RenderDevice.Model * RenderDevice.ViewProjection;
            GL.UniformMatrix4(matrixLocation, false, ref matrix);
            GL.Uniform1(widthLocation, outlineWorldWidth);
        }
        private void Initialize()
        {
            const string vertexSource = @"#version 330 core
layout(location=0) in vec3 position;
layout(location=1) in vec4 color;
layout(location=2) in vec3 normal;
uniform mat4 matrix;
uniform float outline_width;
out vec4 tint;
void main() {
    gl_Position = matrix * vec4(position + normal * outline_width, 1);
    tint = outline_width > 0 ? vec4(0.075, 0.12, 0.045, 1) : color;
}";
            const string fragmentSource = @"#version 330 core
in vec4 tint;
out vec4 output_color;
void main() { output_color = tint; }";
            int vertex = 0, fragment = 0;
            try
            {
                vertex = Compile(ShaderType.VertexShader, vertexSource);
                fragment = Compile(ShaderType.FragmentShader, fragmentSource);
                program = GL.CreateProgram();
                GL.AttachShader(program, vertex); GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
                if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
                matrixLocation = GL.GetUniformLocation(program, "matrix");
                widthLocation = GL.GetUniformLocation(program, "outline_width");
            }
            catch { Dispose(); throw; }
            finally { if (vertex != 0) GL.DeleteShader(vertex); if (fragment != 0) GL.DeleteShader(fragment); }
        }
        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0) { string error = GL.GetShaderInfoLog(shader); GL.DeleteShader(shader); throw new InvalidOperationException(error); }
            return shader;
        }
        public void Dispose() { if (program != 0) GL.DeleteProgram(program); program = 0; }
    }
}
