using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using ForesTycoon.Rendering.OpenGl;

namespace ForesTycoon.Rendering
{
    internal static class GlProgram
    {
        private static readonly object uniformKey = new();
        private sealed class ProgramCache
        {
            internal readonly Dictionary<(int Program, string Name), int> Uniforms = new();
            internal readonly Dictionary<int, OpenGlForestUniforms> Forests = new();
        }
        private static ProgramCache Cache => RenderDevice.GetState(uniformKey, () => new ProgramCache());
        internal static int Uniform(int program, string name)
        {
            RenderDevice.Environment.VerifyAccess();
            var uniforms = Cache.Uniforms;
            var key = (program, name);
            if (!uniforms.TryGetValue(key, out int location))
                uniforms.Add(key, location = GL.GetUniformLocation(program, name));
            return location;
        }
        internal static void ApplyForestState(float elapsed = 0, float year = 0, int texture = 0)
        {
            RenderDevice.Environment.VerifyAccess();
            GL.GetInteger(GetPName.CurrentProgram, out int current);
            var states = Cache.Forests;
            if (!states.TryGetValue(current, out var state)) states.Add(current, state = new OpenGlForestUniforms(current));
            state.Apply(elapsed, year, texture, OpenGlVertexBuffer.ForestSpringTints);
        }
        internal static void Delete(int program)
        {
            RenderDevice.Environment.VerifyAccess();
            // GL names can be reused after deletion; never retain old locations.
            var cache = Cache;
            cache.Forests.Remove(program);
            var keys = new List<(int Program, string Name)>();
            foreach (var key in cache.Uniforms.Keys) if (key.Program == program) keys.Add(key);
            foreach (var key in keys) cache.Uniforms.Remove(key);
            GL.GetInteger(GetPName.CurrentProgram, out int current);
            if (current == program) GL.UseProgram(0); // Bound programs otherwise remain alive after deletion.
            GL.DeleteProgram(program);
        }
        internal static int Create(string vertexSource, string fragmentSource, string geometrySource = null)
        {
            RenderDevice.Environment.VerifyAccess();
            int vertex = 0, fragment = 0, geometry = 0, program = 0;
            try
            {
                vertex = Compile(ShaderType.VertexShader, vertexSource);
                fragment = Compile(ShaderType.FragmentShader, fragmentSource);
                program = GL.CreateProgram();
                GL.AttachShader(program, vertex); GL.AttachShader(program, fragment);
                if (geometrySource != null) { geometry = Compile(ShaderType.GeometryShader, geometrySource); GL.AttachShader(program, geometry); }
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
                return program;
            }
            catch { if (program != 0) GL.DeleteProgram(program); throw; }
            finally { if (geometry != 0) GL.DeleteShader(geometry); if (vertex != 0) GL.DeleteShader(vertex); if (fragment != 0) GL.DeleteShader(fragment); }
        }

        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            if (compiled != 0) return shader;
            string error = GL.GetShaderInfoLog(shader); GL.DeleteShader(shader);
            throw new InvalidOperationException($"{type}: {error}");
        }
    }
}
