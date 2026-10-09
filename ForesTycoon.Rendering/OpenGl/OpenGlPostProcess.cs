using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering.OpenGl
{
    /// <summary>
    /// Miniature-photography finish for the world view. The scene is drawn into an offscreen
    /// multisampled target, then composited in full-screen passes:
    ///   1. SSAO at half resolution: hemisphere samples around the surface normal rebuilt from the
    ///      (orthographic, so linear) depth, then a depth-aware blur that keeps edges crisp;
    ///   2. the occlusion applied, and colour grading;
    ///   3. depth-of-field tilt-shift: the ground at the centre of the view is in focus, blur grows
    ///      with the depth distance from it (as with a macro lens over a model), with a gather that
    ///      lets sharp foreground not bleed into the blurred background; vignette and dither.
    /// A studio backdrop replaces the flat clear colour so the terrain slab reads as a model
    /// standing on a table. Purely visual: nothing here feeds back into simulation or picking.
    /// </summary>
    internal sealed class OpenGlPostProcess : IPostProcessBackend
    {
        private int msaaFramebuffer, msaaColor, msaaDepth;
        private int resolveFramebuffer, sceneTexture, depthTexture;
        private int gradeFramebuffer, gradeTexture;
        private int aoFramebuffer, aoTexture, aoBlurFramebuffer, aoBlurTexture, aoWidth, aoHeight;
        private int gradeProgram, finishProgram, backdropProgram, aoProgram, aoBlurProgram, vao;
        private int width, height, samples;
        private bool active;

        public bool Active => active;
        private static readonly bool DebugOcclusion = Environment.GetEnvironmentVariable("FORES_DEBUG_AO") == "1";

        internal static bool Enabled(IPostProcessSettings settings) => settings.Enhanced && settings.Diorama;

        /// <summary>Redirects drawing into the offscreen target. Returns false when the effect is off.</summary>
        public bool Begin(IPostProcessSettings settings, int framebufferWidth, int framebufferHeight)
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
        public void DrawBackdrop(IPostProcessSettings settings, Vector3 clearColor)
        {
            if (!active || !settings.StudioBackdrop) return;
            using var state = new OpenGlRenderStateScope().Disable(RenderCapability.DepthTest).Disable(RenderCapability.Blend).DepthWrite(false);
            GL.UseProgram(backdropProgram);
            GL.Uniform3(GlProgram.Uniform(backdropProgram, "base_color"), clearColor);
            GL.Uniform2(GlProgram.Uniform(backdropProgram, "aspect"), new Vector2(width / (float)Math.Max(1, height), 1));
            DrawFullScreen();
        }

        /// <summary>Resolves the offscreen frame and composites it into the default framebuffer.</summary>
        public void End(IPostProcessSettings settings, float pixelsPerWorldUnit, float dpiScale, float time)
        {
            if (!active) return;
            active = false;

            // Multisample resolve: colour and depth go to plain textures the shaders can read.
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, msaaFramebuffer);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, resolveFramebuffer);
            GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height,
                ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);

            using var state = new OpenGlRenderStateScope().Disable(RenderCapability.DepthTest).Disable(RenderCapability.Blend)
                .Disable(RenderCapability.CullFace).DepthWrite(false);
            GL.Viewport(0, 0, width, height);

            // World units per framebuffer texel of the orthographic view, and the camera's depth span.
            float worldPerTexel = 1f / Math.Max(1e-3f, pixelsPerWorldUnit * dpiScale);
            const float DepthRange = 2000f;
            int samples = settings.AmbientOcclusion ? settings.OcclusionPairs * 2 : 0;

            // Pass 1 – SSAO at half resolution, then a depth-aware blur.
            if (samples > 0)
            {
                GL.Viewport(0, 0, aoWidth, aoHeight);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, aoFramebuffer);
                GL.UseProgram(aoProgram);
                BindTexture(TextureUnit.Texture0, depthTexture);
                GL.Uniform1(GlProgram.Uniform(aoProgram, "depth"), 0);
                GL.Uniform2(GlProgram.Uniform(aoProgram, "size"), new Vector2(width, height));
                GL.Uniform1(GlProgram.Uniform(aoProgram, "world_per_texel"), worldPerTexel);
                GL.Uniform1(GlProgram.Uniform(aoProgram, "depth_range"), DepthRange);
                // About two metres of world: crevices between trunks, under machines and stacks, along banks.
                GL.Uniform1(GlProgram.Uniform(aoProgram, "radius"), Math.Max(0.6f, 0.75f + 6f * worldPerTexel));
                GL.Uniform1(GlProgram.Uniform(aoProgram, "samples"), samples);
                DrawFullScreen();

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, aoBlurFramebuffer);
                GL.UseProgram(aoBlurProgram);
                BindTexture(TextureUnit.Texture0, aoTexture);
                BindTexture(TextureUnit.Texture1, depthTexture);
                GL.Uniform1(GlProgram.Uniform(aoBlurProgram, "ao"), 0);
                GL.Uniform1(GlProgram.Uniform(aoBlurProgram, "depth"), 1);
                GL.Uniform2(GlProgram.Uniform(aoBlurProgram, "texel"), new Vector2(1f / aoWidth, 1f / aoHeight));
                GL.Uniform1(GlProgram.Uniform(aoBlurProgram, "depth_range"), DepthRange);
                GL.Uniform1(GlProgram.Uniform(aoBlurProgram, "edge"), 1f / Math.Max(0.05f, 3f * worldPerTexel));
                DrawFullScreen();
                GL.Viewport(0, 0, width, height);
            }

            // Pass 2 – occlusion applied and colour grade, into an intermediate texture.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, gradeFramebuffer);
            GL.UseProgram(gradeProgram);
            BindTexture(TextureUnit.Texture0, sceneTexture);
            BindTexture(TextureUnit.Texture1, aoBlurTexture);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "scene"), 0);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "ao"), 1);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "occlusion_strength"), samples > 0 ? settings.AmbientOcclusionStrength : 0f);
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "grade"), settings.ColorGrading ? 1f : 0f);
            // Diagnostics: FORES_DEBUG_AO=1 shows the occlusion buffer alone.
            GL.Uniform1(GlProgram.Uniform(gradeProgram, "debug_ao"), DebugOcclusion ? 1f : 0f);
            DrawFullScreen();

            // Pass 2 – tilt-shift, vignette and dither, onto the window.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.UseProgram(finishProgram);
            BindTexture(TextureUnit.Texture0, gradeTexture);
            BindTexture(TextureUnit.Texture1, depthTexture);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "graded"), 0);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "depth"), 1);
            GL.Uniform1(GlProgram.Uniform(finishProgram, "depth_range"), DepthRange);
            // In focus: most of the play area; only the far background and the nearest foreground soften.
            GL.Uniform1(GlProgram.Uniform(finishProgram, "focus_range"), Math.Max(2f, height * worldPerTexel * 0.5f));
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

            // Half-resolution occlusion and its blurred copy; white means unoccluded (also when AO is off).
            aoWidth = Math.Max(1, width / 2); aoHeight = Math.Max(1, height / 2);
            aoTexture = CreateTexture(PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte, TextureMinFilter.Linear, aoWidth, aoHeight);
            aoFramebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, aoFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, aoTexture, 0);
            Check("Diorama occlusion");
            aoBlurTexture = CreateTexture(PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte, TextureMinFilter.Linear, aoWidth, aoHeight);
            aoBlurFramebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, aoBlurFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, aoBlurTexture, 0);
            Check("Diorama occlusion blur");
            GL.ClearColor(1, 1, 1, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private int CreateTexture(PixelInternalFormat internalFormat, PixelFormat format, PixelType type, TextureMinFilter filter,
            int textureWidth = 0, int textureHeight = 0)
        {
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, textureWidth > 0 ? textureWidth : width,
                textureHeight > 0 ? textureHeight : height, 0, format, type, IntPtr.Zero);
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
uniform sampler2D scene, ao;
uniform float occlusion_strength, grade, debug_ao;
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
    // Occlusion darkens the ambient share of the light; keep a little of it even in full contact.
    float o = texture(ao, uv).r;
    c *= mix(1.0, pow(o, 1.6), clamp(occlusion_strength, 0.0, 1.5));
    if(grade > 0.5) c = graded(c);
    if(debug_ao > 0.5) c = vec3(o);
    output_color = vec4(clamp(c, 0.0, 1.0), 1.0);
}");

            aoProgram = GlProgram.Create(FullScreenVertex, @"#version 330 core
in vec2 uv;
out float occlusion;
uniform sampler2D depth;
uniform vec2 size;
uniform float world_per_texel, depth_range, radius;
uniform int samples;
const float GOLDEN = 2.39996323;
// View-space position: the view is orthographic, so x/y scale with the texel and depth is linear.
vec3 view_position(vec2 p) { return vec3(p * size * world_per_texel, texture(depth, p).r * depth_range); }
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void main() {
    float d = texture(depth, uv).r;
    if(d >= 0.99999) { occlusion = 1.0; return; }
    vec3 p = view_position(uv);
    // Normal from the depth of the neighbours, taking the smaller step on each axis so silhouettes stay sharp.
    vec2 t = 1.0 / size;
    vec3 l = view_position(uv - vec2(t.x, 0.0)), r = view_position(uv + vec2(t.x, 0.0));
    vec3 b = view_position(uv - vec2(0.0, t.y)), u = view_position(uv + vec2(0.0, t.y));
    vec3 dx = abs(r.z - p.z) < abs(p.z - l.z) ? r - p : p - l;
    vec3 dy = abs(u.z - p.z) < abs(p.z - b.z) ? u - p : p - b;
    vec3 n = normalize(cross(dx, dy));
    if(n.z > 0.0) n = -n;   // face the camera (depth grows away from it)
    // A 4x4 interleaved rotation pattern: every block of 16 pixels covers all directions, so the blur pass averages it away.
    ivec2 cell = ivec2(gl_FragCoord.xy) & 3;
    const float bayer[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
    float spin = (bayer[cell.y * 4 + cell.x] + 0.5) / 16.0 * 6.2831;
    vec3 helper = abs(n.x) < 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    vec3 tx = normalize(cross(helper, n)), ty = cross(n, tx);
    float hidden = 0.0;
    for(int i = 0; i < samples; i++) {
        float fi = (float(i) + 0.5) / float(samples);
        float a = float(i) * GOLDEN + spin;
        float z = sqrt(1.0 - fi);                 // cosine-weighted hemisphere
        float s = sqrt(fi);
        vec3 dir = tx * cos(a) * s + ty * sin(a) * s + n * z;
        float scale = mix(0.3, 1.0, fi * fi);      // more samples close to the surface
        vec3 q = p + dir * radius * scale;
        vec2 qp = q.xy / (size * world_per_texel);
        if(qp.x < 0.0 || qp.y < 0.0 || qp.x > 1.0 || qp.y > 1.0) continue;
        float surface = texture(depth, qp).r * depth_range;
        // Occluded when the scene surface there lies in front of the sample point; distant geometry does not count.
        float range = 1.0 - smoothstep(radius * 0.6, radius * 2.0, p.z - surface);
        // Ignore the slight folds of the low-poly ground: only something standing clearly in front occludes.
        hidden += smoothstep(0.15 * radius, 0.35 * radius, q.z - surface) * range;
    }
    occlusion = clamp(1.0 - hidden / float(samples), 0.0, 1.0);
}");

            aoBlurProgram = GlProgram.Create(FullScreenVertex, @"#version 330 core
in vec2 uv;
out float occlusion;
uniform sampler2D ao, depth;
uniform vec2 texel;
uniform float depth_range, edge;
void main() {
    // 5x5 bilateral blur: neighbours at a different depth (another object) do not mix in.
    float centre = texture(depth, uv).r * depth_range;
    float sum = 0.0, weight = 0.0;
    for(int y = -2; y <= 2; y++)
        for(int x = -2; x <= 2; x++) {
            vec2 p = uv + vec2(x, y) * texel;
            float w = exp(-abs(texture(depth, p).r * depth_range - centre) * edge) * (1.0 - 0.12 * float(abs(x) + abs(y)));
            sum += texture(ao, p).r * w;
            weight += w;
        }
    occlusion = sum / max(weight, 1e-4);
}");

            finishProgram = GlProgram.Create(FullScreenVertex, @"#version 330 core
in vec2 uv;
out vec4 output_color;
uniform sampler2D graded, depth;
uniform vec2 texel, aspect;
uniform float blur_radius, vignette, time, depth_range, focus_range;
uniform int blur_taps;
const float GOLDEN = 2.39996323;
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
float linear_depth(vec2 p) { return texture(depth, p).r * depth_range; }
// The focus plane: the ground at the centre of the view (median of five samples, so a single trunk
// or the backdrop at the centre does not throw it off).
float focus_depth() {
    float s[5];
    s[0] = linear_depth(vec2(0.5, 0.46)); s[1] = linear_depth(vec2(0.42, 0.46)); s[2] = linear_depth(vec2(0.58, 0.46));
    s[3] = linear_depth(vec2(0.5, 0.40)); s[4] = linear_depth(vec2(0.5, 0.52));
    for(int i = 0; i < 5; i++) for(int j = i + 1; j < 5; j++) if(s[j] < s[i]) { float t = s[i]; s[i] = s[j]; s[j] = t; }
    return s[2];
}
// Circle of confusion as a share of the full blur: 0 within the focus range, rising beyond it.
// The backdrop counts as far; a gentle screen band keeps the miniature look on flat views.
float coc(vec2 p, float focus) {
    float d = texture(depth, p).r;
    float depth_blur = d >= 0.99999 ? 1.0 : smoothstep(0.7, 1.4, abs(d * depth_range - focus) / focus_range);
    float band = smoothstep(0.38, 0.62, abs(p.y - 0.46));
    return max(depth_blur, 0.3 * band);
}
void main() {
    vec3 c = texture(graded, uv).rgb;
    float focus = focus_depth();
    float own = coc(uv, focus);
    float r = blur_radius * own;
    if(r > 0.6) {
        vec3 sum = c;
        float weight = 1.0;
        for(int i = 0; i < blur_taps; i++) {
            float a = float(i) * GOLDEN + hash(gl_FragCoord.xy) * 6.2831;
            float k = sqrt((float(i) + 0.5) / float(blur_taps));
            vec2 p = uv + vec2(cos(a), sin(a)) * k * r * texel;
            vec3 s = texture(graded, p).rgb;
            // A tap counts only as far as its own blur reaches: sharp objects do not smear into the blurred background.
            float reach = clamp(coc(p, focus) * 1.4 - k * 0.5, 0.0, 1.0);
            // Bright taps weigh more: out-of-focus highlights bloom like a real lens.
            float w = reach * (1.0 + 1.5 * smoothstep(0.7, 1.0, dot(s, vec3(0.333))));
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
            if (aoFramebuffer != 0) GL.DeleteFramebuffer(aoFramebuffer);
            if (aoBlurFramebuffer != 0) GL.DeleteFramebuffer(aoBlurFramebuffer);
            if (aoTexture != 0) GL.DeleteTexture(aoTexture);
            if (aoBlurTexture != 0) GL.DeleteTexture(aoBlurTexture);
            aoFramebuffer = aoBlurFramebuffer = aoTexture = aoBlurTexture = 0;
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
            if (aoProgram != 0) GlProgram.Delete(aoProgram);
            if (aoBlurProgram != 0) GlProgram.Delete(aoBlurProgram);
            aoProgram = aoBlurProgram = 0;
            if (vao != 0) GL.DeleteVertexArray(vao);
            gradeProgram = finishProgram = backdropProgram = vao = 0;
        }
    }
}
