using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    /// <summary>
    /// Miniature-photography finish for the world view. The scene is drawn into an offscreen
    /// multisampled target, then composited in two full-screen passes:
    ///   1. crease occlusion from the depth buffer (contact shadows under trees, buildings,
    ///      along banks) and colour grading;
    ///   2. tilt-shift blur outside a horizontal focus band, vignette and dither.
    /// A studio backdrop replaces the flat clear colour so the terrain slab reads as a model
    /// standing on a table. Purely visual: nothing here feeds back into simulation or picking.
    /// </summary>
    internal sealed class DioramaPostProcess : IDisposable
    {
        private int msaaFramebuffer, msaaColor, msaaDepth;
        private int resolveFramebuffer, sceneTexture, depthTexture;
        private int gradeFramebuffer, gradeTexture;
        private int gradeProgram, finishProgram, backdropProgram, vao;
        private int width, height, samples;
        private bool active;

        internal bool Active => active;

        internal static bool Enabled(IPostProcessSettings settings) => settings.Enhanced && settings.Diorama;

        /// <summary>Redirects drawing into the offscreen target. Returns false when the effect is off.</summary>
        internal bool Begin(IPostProcessSettings settings, int framebufferWidth, int framebufferHeight)
        {
            active = false;
            if (!Enabled(settings) || framebufferWidth <= 0 || framebufferHeight <= 0) return false;
            if (gradeProgram == 0) CreatePrograms();
            EnsureTargets(framebufferWidth, framebufferHeight);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, msaaFramebuffer);
            GL.Viewport(0, 0, width, height);
            active = true;
            return true;
        }

        /// <summary>Soft radial studio backdrop, drawn right after the clear and before the world.</summary>
        internal void DrawBackdrop(IPostProcessSettings settings, Vector3 clearColor)
        {
            if (!active || !settings.StudioBackdrop) return;
            using var state = new RenderStateScope().Disable(EnableCap.DepthTest).Disable(EnableCap.Blend).DepthWrite(false);
            GL.UseProgram(backdropProgram);
            GL.Uniform3(GlProgram.Uniform(backdropProgram, "base_color"), clearColor);
            GL.Uniform2(GlProgram.Uniform(backdropProgram, "aspect"), new Vector2(width / (float)Math.Max(1, height), 1));
            DrawFullScreen();
        }

        /// <summary>Resolves the offscreen frame and composites it into the default framebuffer.</summary>
        internal void End(IPostProcessSettings settings, float pixelsPerWorldUnit, float dpiScale, float time)
        {
            if (!active) return;
            active = false;

            // Multisample resolve: colour and depth go to plain textures the shaders can read.
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, msaaFramebuffer);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, resolveFramebuffer);
            GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height,
                ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);

            using var state = new RenderStateScope().Disable(EnableCap.DepthTest).Disable(EnableCap.Blend)
                .Disable(EnableCap.CullFace).DepthWrite(false);
            GL.Viewport(0, 0, width, height);

            // Pass 1 – occlusion and grade, into an intermediate texture.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, gradeFramebuffer);
            GL.UseProgram(gradeProgram);
            BindTexture(TextureUnit.Texture0, sceneTexture);
            BindTexture(TextureUnit.Texture1, depthTexture);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "scene"), 0);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "depth"), 1);
            GL.Uniform2(GlProgram.Uniform(gradeProgram, "texel"), new Vector2(1f / width, 1f / height));
            // Occlusion radius tracks the world, not the screen: about one metre of terrain.
            float occlusionRadius = Math.Clamp(pixelsPerWorldUnit * 1.1f, 3f * dpiScale, 26f * dpiScale);
            int pairs = settings.AmbientOcclusion ? settings.OcclusionPairs : 0;
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "occlusion_pairs"), pairs);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "occlusion_radius"), occlusionRadius);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "occlusion_strength"), settings.AmbientOcclusionStrength);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "depth_range"), 2000f);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "grade"), settings.ColorGrading ? 1f : 0f);
            DrawFullScreen();

            // Pass 2 – tilt-shift, vignette and dither, onto the window.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.UseProgram(finishProgram);
            BindTexture(TextureUnit.Texture0, gradeTexture);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "graded"), 0);
            GL.Uniform2(GlProgram.Uniform(finishProgram, "texel"), new Vector2(1f / width, 1f / height));
            GL.Uniform2(GlProgram.Uniform(finishProgram, "aspect"), new Vector2(width / (float)Math.Max(1, height), 1));
            // The miniature illusion is strongest from afar; close-ups keep more of the frame sharp.
            float distance = Math.Clamp(1.35f - pixelsPerWorldUnit / 45f, 0.35f, 1f);
            float blur = settings.TiltShift ? settings.TiltShiftStrength * distance * 7.5f * dpiScale : 0f;
            GL.Uniform1(GlProgram.Uniform(finishProgram, "blur_radius"), blur);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "blur_taps"), settings.TiltShiftTaps);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "vignette"), settings.Vignette ? 1f : 0f);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "time"), time % 64f);
            DrawFullScreen();

            GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private static void BindTexture(TextureUnit unit, int texture)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, texture);
        }

        private void DrawFullScreen()
        {
            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            GL.BindVertexArray(0);
            RenderMetrics.RecordDraw(3);
        }

        private void EnsureTargets(int framebufferWidth, int framebufferHeight)
        {
            if (framebufferWidth == width && framebufferHeight == height && msaaFramebuffer != 0) return;
            DeleteTargets();
            width = framebufferWidth;
            height = framebufferHeight;
            GL.GetInteger(GetPName.MaxSamples, out int maxSamples);
            samples = Math.Clamp(4, 1, Math.Max(1, maxSamples));

            msaaColor = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msaaColor);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.Rgba8, width, height);
            msaaDepth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msaaDepth);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.DepthComponent24, width, height);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            msaaFramebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, msaaFramebuffer);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, msaaColor);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, msaaDepth);
            Check("Diorama multisample");

            sceneTexture = CreateTexture(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, TextureMinFilter.Linear);
            depthTexture = CreateTexture(PixelInternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.Float, TextureMinFilter.Nearest);
            resolveFramebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, resolveFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, sceneTexture, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTexture, 0);
            Check("Diorama resolve");

            gradeTexture = CreateTexture(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, TextureMinFilter.Linear);
            gradeFramebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, gradeFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, gradeTexture, 0);
            Check("Diorama grade");
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private int CreateTexture(PixelInternalFormat internalFormat, PixelFormat format, PixelType type, TextureMinFilter filter)
        {
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, type, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(filter == TextureMinFilter.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return texture;
        }

        private static void Check(string name)
        {
            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException($"{name} framebuffer is incomplete: {status}.");
        }

        private const string FullScreenVertex = @"#version 330 core
out vec2 uv;
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    uv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

        private void CreatePrograms()
        {
            vao = GL.GenVertexArray();

            backdropProgram = GlProgram.Create(FullScreenVertex, @"#version 330 core
in vec2 uv;
out vec4 output_color;
uniform vec3 base_color;
uniform vec2 aspect;
void main() {
    // Photographic paper sweep: lit from above, falling off toward the corners.
    vec2 p = (uv - vec2(0.5, 0.58)) * aspect;
    float light = 1.0 - smoothstep(0.05, 1.05, length(p));
    vec3 warm = base_color * vec3(1.55, 1.50, 1.42) + vec3(0.035, 0.035, 0.03);
    vec3 deep = base_color * vec3(0.55, 0.58, 0.64);
    vec3 color = mix(deep, warm, light) + vec3(0.02) * (uv.y - 0.5);
    output_color = vec4(color, 1.0);
}");

            gradeProgram = GlProgram.Create(FullScreenVertex, @"#version 330 core
in vec2 uv;
out vec4 output_color;
uniform sampler2D scene, depth;
uniform vec2 texel;
uniform int occlusion_pairs;
uniform float occlusion_radius, occlusion_strength, depth_range, grade;
const float GOLDEN = 2.39996323;
float occlusion(float centre) {
    if(occlusion_pairs == 0 || centre >= 0.99999) return 1.0;
    float total = 0.0, weight = 0.0;
    for(int i = 0; i < occlusion_pairs; i++) {
        // Opposing pairs measure curvature, not slope: a tilted plane cancels out, a crease
        // (ground meeting a trunk, a wall, a bank) is farther than its two neighbours' mean.
        float a = float(i) * GOLDEN;
        float r = occlusion_radius * sqrt((float(i) + 0.5) / float(occlusion_pairs));
        vec2 o = vec2(cos(a), sin(a)) * r * texel;
        float da = (texture(depth, uv + o).r - centre) * depth_range;
        float db = (texture(depth, uv - o).r - centre) * depth_range;
        // Silhouettes against distant ground are depth jumps, not contact: fade them out.
        float range = 1.0 - smoothstep(1.2, 3.5, max(abs(da), abs(db)));
        float crease = clamp(-(da + db) * 0.5 * 1.6, 0.0, 1.0);
        total += crease * range;
        weight += 1.0;
    }
    return 1.0 - occlusion_strength * 0.7 * total / max(weight, 1.0);
}
vec3 graded(vec3 c) {
    // Miniature look: a touch more saturation, gentle S-curve, cool shade / warm light.
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    c = mix(vec3(luma), c, 1.14);
    c = clamp(c, 0.0, 1.0);
    c = mix(c, c * c * (3.0 - 2.0 * c), 0.28);
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    c += vec3(-0.012, 0.0, 0.022) * (1.0 - smoothstep(0.0, 0.45, l));
    c += vec3(0.030, 0.014, -0.018) * smoothstep(0.45, 1.0, l);
    return c;
}
void main() {
    vec3 c = texture(scene, uv).rgb;
    float d = texture(depth, uv).r;
    c *= occlusion(d);
    if(grade > 0.5) c = graded(c);
    output_color = vec4(clamp(c, 0.0, 1.0), 1.0);
}");

            finishProgram = GlProgram.Create(FullScreenVertex, @"#version 330 core
in vec2 uv;
out vec4 output_color;
uniform sampler2D graded;
uniform vec2 texel, aspect;
uniform float blur_radius, vignette, time;
uniform int blur_taps;
const float GOLDEN = 2.39996323;
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void main() {
    vec3 c = texture(graded, uv).rgb;
    // Tilt-shift: a sharp horizontal band, slightly below centre where the eye rests,
    // with blur growing smoothly toward the top and bottom of the frame.
    float band = abs(uv.y - 0.46);
    float r = blur_radius * smoothstep(0.13, 0.50, band);
    if(r > 0.6) {
        vec3 sum = c;
        float weight = 1.0;
        for(int i = 0; i < blur_taps; i++) {
            float a = float(i) * GOLDEN;
            float k = sqrt((float(i) + 0.5) / float(blur_taps));
            vec3 s = texture(graded, uv + vec2(cos(a), sin(a)) * k * r * texel).rgb;
            // Bright taps weigh more: out-of-focus highlights bloom like a real lens.
            float w = 1.0 + 1.5 * smoothstep(0.7, 1.0, dot(s, vec3(0.333)));
            sum += s * w;
            weight += w;
        }
        c = sum / weight;
    }
    if(vignette > 0.5) {
        vec2 p = (uv - 0.5) * aspect;
        c *= mix(1.0, 0.72, smoothstep(0.42, 1.05, length(p)));
    }
    // Sub-LSB dither keeps the backdrop gradient and the sky free of banding.
    c += (hash(gl_FragCoord.xy + time) - 0.5) / 255.0;
    output_color = vec4(c, 1.0);
}");
        }

        private void DeleteTargets()
        {
            if (msaaFramebuffer != 0) GL.DeleteFramebuffer(msaaFramebuffer);
            if (resolveFramebuffer != 0) GL.DeleteFramebuffer(resolveFramebuffer);
            if (gradeFramebuffer != 0) GL.DeleteFramebuffer(gradeFramebuffer);
            if (msaaColor != 0) GL.DeleteRenderbuffer(msaaColor);
            if (msaaDepth != 0) GL.DeleteRenderbuffer(msaaDepth);
            if (sceneTexture != 0) GL.DeleteTexture(sceneTexture);
            if (depthTexture != 0) GL.DeleteTexture(depthTexture);
            if (gradeTexture != 0) GL.DeleteTexture(gradeTexture);
            msaaFramebuffer = resolveFramebuffer = gradeFramebuffer = msaaColor = msaaDepth = 0;
            sceneTexture = depthTexture = gradeTexture = 0;
            width = height = 0;
        }

        public void Dispose()
        {
            DeleteTargets();
            if (gradeProgram != 0) GlProgram.Delete(gradeProgram);
            if (finishProgram != 0) GlProgram.Delete(finishProgram);
            if (backdropProgram != 0) GlProgram.Delete(backdropProgram);
            if (vao != 0) GL.DeleteVertexArray(vao);
            gradeProgram = finishProgram = backdropProgram = vao = 0;
        }
    }
}
