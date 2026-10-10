using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Effects.OpenGl
{
    internal sealed class OpenGlWeatherRenderer : IWeatherRenderBackend
    {
        private readonly RenderResourceOwner owner = new();
        private int program, vao, heightTexture;
        private float[] heights;
        private ulong surfaceRevision = ulong.MaxValue;
        private IWeatherSurface cachedSurface;
        private int heightColumns, heightRows, particles;
        private bool disposed;
        internal (int Program, int VertexArray, int HeightTexture) CaptureResources() => (program, vao, heightTexture);
        public EffectMetrics Metrics => new(particles, 0, (long)(heights?.Length ?? 0) * sizeof(float), (long)heightColumns * heightRows * sizeof(float), 0);

        public void Draw(IWeatherSurface surface, WeatherVisualState weather, RenderContext context, IWeatherSettings settings)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            owner.Check();
            ArgumentNullException.ThrowIfNull(surface); ArgumentNullException.ThrowIfNull(weather); ArgumentNullException.ThrowIfNull(settings);
            particles = 0;
            surface.GetVisibleBounds(out Vector2 visibleMin, out Vector2 visibleMax);
            var plan = WeatherParticlePlan.Create(visibleMin, visibleMax, settings.RainBudget);
            float intensity = Math.Max(weather.Rain, weather.Snowfall);
            if (!settings.Weather || intensity < 0.01f || plan.Count == 0) return;
            long samples = (long)surface.Columns * surface.Rows;
            if (surface.Columns <= 0 || surface.Rows <= 0 || samples > WeatherParticlePlan.MaxHeightSamples)
                throw new ArgumentOutOfRangeException(nameof(surface), "Weather height field exceeds its 16 MiB payload budget.");
            if (program == 0) Initialize();
            if (heights == null || !ReferenceEquals(surface, cachedSurface) || surfaceRevision != surface.Revision
                || heightColumns != surface.Columns || heightRows != surface.Rows)
            {
                GL.GetInteger(GetPName.MaxTextureSize, out int maxSize);
                if (surface.Columns > maxSize || surface.Rows > maxSize) throw new NotSupportedException("Weather height field exceeds the device texture limit.");
                var candidate = heights?.Length == samples ? heights : new float[(int)samples];
                surface.FillHeights(candidate);
                foreach (float height in candidate) if (!float.IsFinite(height)) throw new InvalidOperationException("Weather surface contains a non-finite height.");
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, heightTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f,
                    surface.Columns, surface.Rows, 0, PixelFormat.Red, PixelType.Float, candidate);
                var error = GL.GetError();
                if (error != ErrorCode.NoError) { cachedSurface = null; throw new InvalidOperationException("Weather texture allocation failed: " + error); }
                heights = candidate; heightColumns = surface.Columns; heightRows = surface.Rows; cachedSurface = surface;
                surfaceRevision = surface.Revision;
            }
            surface.GetBounds(out Vector3 min, out Vector3 max);
            float snow = weather.Snowfall > weather.Rain ? 1 : 0;
            float zoom = Math.Max(1, context.PixelsPerWorldUnit);
            // World cells keep drops anchored while the camera pans.
            GL.UseProgram(program);
            GL.Uniform4(GlProgram.Uniform(program, "grid"), plan.Origin.X, plan.Origin.Y, plan.Columns, plan.PerCell);
            GL.Uniform2(GlProgram.Uniform(program, "wind"), weather.Wind);
            GL.Uniform1(GlProgram.Uniform(program, "cell_size"), plan.CellSize);
            Matrix4 matrix = RenderDevice.ViewProjection;
            GL.UniformMatrix4(GlProgram.Uniform(program, "camera"), false, ref matrix);
            GL.Uniform4(GlProgram.Uniform(program, "footprint"), min.X, min.Y, max.X - min.X, max.Y - min.Y);
            GL.Uniform4(GlProgram.Uniform(program, "emitter"), visibleMin.X, visibleMin.Y, visibleMax.X - visibleMin.X, visibleMax.Y - visibleMin.Y);
            GL.Uniform4(GlProgram.Uniform(program, "params"), (float)(weather.Time % 4096), snow, intensity, max.Z);
            var basis = FogParticleMotion.CameraBasis(context.CameraYaw, context.CameraTilt);
            GL.Uniform3(GlProgram.Uniform(program, "right"), basis.Right);
            GL.Uniform3(GlProgram.Uniform(program, "up"), basis.Up);
            GL.Uniform1(GlProgram.Uniform(program, "size"), Math.Clamp((snow > 0 ? 1.7f : 0.8f) / zoom, 0.035f, 0.28f));
            GL.Uniform1(GlProgram.Uniform(program, "heights"), 2);
            GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, heightTexture);
            using (RenderDevice.CreateStateScope().Enable(RenderCapability.DepthTest).Disable(RenderCapability.CullFace).AlphaBlend().DepthWrite(false))
            {
                GL.BindVertexArray(vao);
                GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, plan.Count);
                particles = plan.Count;
                RenderMetrics.RecordDraw(plan.Count * 6);
                GL.BindVertexArray(0);
            }
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void Initialize()
        {
            try { InitializeCore(); }
            catch { ReleaseResources(); throw; }
        }
        private void InitializeCore()
        {
            program = GlProgram.Create(@"#version 330 core
uniform mat4 camera;
uniform vec4 footprint, emitter, params, grid;
uniform vec2 wind;
uniform vec3 right, up;
uniform float size, cell_size;
uniform sampler2D heights;
out vec2 uv;
out float opacity;
out float flake_shape;
const vec2 corners[6] = vec2[6](vec2(-1,-1),vec2(1,-1),vec2(1,1),vec2(-1,-1),vec2(1,1),vec2(-1,1));
void main(){
    float t=params.x, snow=params.y;
    int cellid=gl_InstanceID/int(grid.w);
    vec2 cell=grid.xy+vec2(cellid%int(grid.z),cellid/int(grid.z));
    float id=float(gl_InstanceID%int(grid.w));
    vec3 random=fract(sin(vec3(dot(cell,vec2(127.1,311.7))+id*19.19,
        dot(cell,vec2(269.5,183.3))+id*73.17,dot(cell,vec2(419.2,371.9))+id*31.7))*43758.5453);
    // Shape-dependent settling and periodic flutter approximate the regimes in
    // Stout et al. (2024), doi:10.5194/acp-24-11133-2024. World units are stylised.
    float speed=mix(22.0,0.4+random.x*0.75,snow);
    float span=max(params.w,8);
    float age=fract(random.z+t*speed/span)*span/speed;
    vec2 xy=cell*cell_size+random.xy*cell_size+wind*age*mix(1.0,0.22,snow);
    float angle=t*(0.85+random.y*1.3)+random.z*6.283185;
    float flutter=(0.18+random.x*0.65);
    xy+=snow*flutter*vec2(sin(angle)+0.22*sin(angle*0.47),cos(angle*0.83+random.x*6.283185));
    vec2 mapuv=(xy-footprint.xy)/footprint.zw;
    float floorz=texture(heights,mapuv).r;
    vec3 p=vec3(xy,params.w-age*speed);
    float phase=age*speed/span;
    vec2 corner=corners[gl_VertexID]; uv=corner;
    flake_shape=random.z;
    if(snow>0.5){
        // Camera-facing compact aggregates: no velocity-aligned rain streak.
        float rotation=t*(random.z-0.5)*1.2+random.y*6.283185;
        vec2 c=mat2(cos(rotation),sin(rotation),-sin(rotation),cos(rotation))*corner;
        float radius=size*(0.6+random.y*0.85);
        float tilt=0.72+0.28*cos(angle);
        p+=right*c.x*radius+up*c.y*radius*tilt;
    } else {
        vec3 stretch=normalize(vec3(-wind,speed));
        p+=right*corner.x*size+stretch*corner.y*speed*0.025;
    }
    opacity=params.z * smoothstep(0,0.08,phase)*(1-smoothstep(0.94,1,phase));
    if(p.z < floorz+0.05) opacity=0;
    if(any(lessThan(mapuv,vec2(0))) || any(greaterThan(mapuv,vec2(1)))) opacity=0;
    gl_Position=camera*vec4(p,1);
}", @"#version 330 core
in vec2 uv;
in float opacity;
in float flake_shape;
uniform vec4 params;
out vec4 output_color;
void main(){
    float mask=(1-abs(uv.x))*(1-abs(uv.y));
    if(params.y>0.5){
        float radial=length(uv), angle=atan(uv.y,uv.x);
        float edge=mix(0.72+0.1*cos(angle*5+flake_shape*18),0.6+0.18*cos(angle*6),step(0.75,flake_shape));
        mask=1-smoothstep(edge*0.4,edge,radial);
    }
    float alpha=mask*opacity*mix(0.36,0.78,params.y);
    if(alpha<0.01) discard;
    output_color=vec4(mix(vec3(0.69,0.79,0.88),vec3(0.92,0.96,1),params.y),alpha);
}");
            vao = GL.GenVertexArray();
            heightTexture = GL.GenTexture(); GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, heightTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        public void Dispose()
        {
            if (disposed) return; owner.CheckIfBound(); disposed = true; ReleaseResources();
        }
        private void ReleaseResources()
        {
            if(program != 0) GlProgram.Delete(program);
            if(vao != 0) GL.DeleteVertexArray(vao);
            if(heightTexture != 0) GL.DeleteTexture(heightTexture);
            program = vao = heightTexture = 0;
            heights = null; cachedSurface = null; heightColumns = heightRows = particles = 0;
        }
    }
}
