using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.Rendering.OpenGl
{
    /// <summary>Program-owned forest uniforms, shared by retained and immediate geometry.</summary>
    internal sealed class OpenGlForestUniforms
    {
        private readonly int elapsedLocation, yearLocation, dynamicLocation, springLocation;
        private float elapsed = float.NaN, year = float.NaN;
        private int dynamic = -1;
        private float[] spring;

        internal OpenGlForestUniforms(int program)
        {
            elapsedLocation = GlProgram.Uniform(program, "forest_elapsed");
            yearLocation = GlProgram.Uniform(program, "forest_year");
            dynamicLocation = GlProgram.Uniform(program, "forest_dynamic");
            springLocation = GlProgram.Uniform(program, "forest_spring[0]");
            // Even inactive samplers must use a unit distinct from sampler2D/Array.
            GL.Uniform1(GlProgram.Uniform(program, "forest_state"), 7);
        }

        internal void Apply(float elapsedYears, float currentYear, int stateTexture, float[] springTints)
        {
            if (elapsed != elapsedYears) { GL.Uniform1(elapsedLocation, elapsedYears); elapsed = elapsedYears; }
            if (year != currentYear) { GL.Uniform1(yearLocation, currentYear); year = currentYear; }
            int enabled = stateTexture != 0 ? 1 : 0;
            if (dynamic != enabled) { GL.Uniform1(dynamicLocation, enabled); dynamic = enabled; }
            if (!ReferenceEquals(spring, springTints))
            {
                GL.Uniform3(springLocation, 18, springTints);
                spring = springTints;
            }
            // Texture bindings are shared across programs; other passes may change them.
            if (stateTexture == 0) return;
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.TextureBuffer, stateTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
    }
}
