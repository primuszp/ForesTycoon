using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal enum SurfaceKind { Plain = 0, Ground = 1, Skirt = 2, Road = 3, Vehicle = 4, Wood = 5, Foliage = 6, Water = 7, ForestFloor = 8, Rubber = 9, LogCargo = 10 }

    internal sealed class SurfaceVisualRenderer : IDisposable
    {
        private int program, depthProgram, textures, shadowTexture, shadowFramebuffer;
        private int environmentTexture;
        private float[] environmentPixels;
        private ulong environmentRevision=ulong.MaxValue;
        private readonly EnvironmentSystem environment;
        private readonly Terrain environmentTerrain;
        private readonly GraphicsSettings settings;
        private readonly WeatherVisualState weather;
        internal SurfaceKind Kind;
        internal bool ShadowPass { get; private set; }
        internal Matrix4 ShadowCamera => lightMatrix;
        internal Vector4 Atmosphere => settings.Weather ? new Vector4(weather.Cloud,weather.Storm,weather.Flash,weather.Wetness) : Vector4.Zero;
        internal bool ShadowsReady => shadowsReady;
        internal bool Active => settings.Enhanced || ShadowPass;
        private Matrix4 lightMatrix;
        private Vector3 sun;
        private bool shadowsReady;
        private int shadowSize;
        private bool globalsUploaded, mainCameraUploaded, shadowCameraUploaded;
        private Matrix4 mainCamera, shadowCamera;

        internal SurfaceVisualRenderer(GraphicsSettings settings, WeatherVisualState weather,EnvironmentSystem environment=null,Terrain terrain=null)
        { this.settings = settings; this.weather = weather;this.environment=environment;environmentTerrain=terrain; }

        internal void BeginFrame()
        {
            float az = MathHelper.DegreesToRadians(settings.SunAzimuth), el = MathHelper.DegreesToRadians(settings.SunElevation);
            sun = new Vector3(MathF.Cos(az) * MathF.Cos(el), MathF.Sin(az) * MathF.Cos(el), MathF.Sin(el));
            shadowsReady = false;
            globalsUploaded = mainCameraUploaded = shadowCameraUploaded = false;
            if (settings.Enhanced && program == 0) Initialize();
            if(settings.Enhanced&&environment!=null)UploadEnvironment();
            if (settings.Enhanced && shadowSize != settings.ShadowResolution)
            {
                shadowSize = settings.ShadowResolution;
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, shadowTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, shadowSize, shadowSize, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            }
        }

        internal void RenderShadows(Terrain terrain, Action draw)
        {
            if (!settings.Enhanced || !settings.Lighting || !settings.Shadows) return;
            terrain.GetWeatherBounds(out Vector3 min, out Vector3 max);
            Vector3 center = (min + max) * 0.5f;
            float radius = Math.Max(24, (max - min).Length * 0.6f + 12);
            lightMatrix = Matrix4.LookAt(center + sun * radius * 2, center, Vector3.UnitZ) *
                Matrix4.CreateOrthographic(radius * 2, radius * 2, 1, radius * 4);
            int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int oldFramebuffer);
            try
            {
                ShadowPass = true;
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, shadowFramebuffer);
                GL.Viewport(0, 0, shadowSize, shadowSize);
                using (new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.Blend).DepthWrite(true).PolygonOffset(2, 4))
                {
                    GL.Clear(ClearBufferMask.DepthBufferBit);
                    draw();
                }
                shadowsReady = true;
            }
            finally
            {
                ShadowPass = false;
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, oldFramebuffer);
                GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            }
        }

        internal void Use(float outlineWidth = 0)
        {
            int shader = ShadowPass ? depthProgram : program;
            GL.UseProgram(shader);
            GL.Uniform2(GlProgram.Uniform(shader,"lod_range"),RenderDevice.LodRange);
            Matrix4 model = RenderDevice.Model;
            Matrix4 camera = ShadowPass ? lightMatrix : RenderDevice.ViewProjection;
            GL.UniformMatrix4(GlProgram.Uniform(shader, "model"), false, ref model);
            if (ShadowPass ? !shadowCameraUploaded || shadowCamera != camera : !mainCameraUploaded || mainCamera != camera)
            {
                GL.UniformMatrix4(GlProgram.Uniform(shader, "camera"), false, ref camera);
                if (ShadowPass) { shadowCamera = camera; shadowCameraUploaded = true; }
                else { mainCamera = camera; mainCameraUploaded = true; }
            }
            if (ShadowPass) return;
            GL.Uniform1(GlProgram.Uniform(shader, "kind"), (int)Kind);
            GL.Uniform1(GlProgram.Uniform(shader, "outline_width"), outlineWidth);
            if (!globalsUploaded)
            {
                GL.UniformMatrix4(GlProgram.Uniform(shader, "light_matrix"), false, ref lightMatrix);
                GL.Uniform1(GlProgram.Uniform(shader, "textured"), settings.Textures ? 1 : 0);
                GL.Uniform1(GlProgram.Uniform(shader, "lit"), settings.Lighting ? 1 : 0);
                GL.Uniform1(GlProgram.Uniform(shader, "shadowed"), shadowsReady ? 1 : 0);
                GL.Uniform3(GlProgram.Uniform(shader, "sun"), sun);
                GL.Uniform4(GlProgram.Uniform(shader, "climate"), settings.Weather ? new Vector4(weather.Cloud, weather.Wetness, weather.SnowCover, weather.Rain) : Vector4.Zero);
                GL.Uniform1(GlProgram.Uniform(shader, "clouds"), settings.Weather && settings.Clouds ? 1 : 0);
                GL.Uniform1(GlProgram.Uniform(shader, "flash"), settings.Weather ? weather.Flash : 0);
                GL.Uniform1(GlProgram.Uniform(shader, "storm"), settings.Weather ? weather.Storm : 0);
                GL.Uniform1(GlProgram.Uniform(shader, "time"), (float)(weather.Time % 4096));
                GL.Uniform1(GlProgram.Uniform(shader, "materials"), 0);
                GL.Uniform1(GlProgram.Uniform(shader,"environment_map"),5);
                GL.Uniform1(GlProgram.Uniform(shader,"environment_active"),environment!=null&&settings.AutomaticWeather&&settings.Weather?1:0);
                if(environmentTerrain!=null){environmentTerrain.GetWeatherBounds(out var min,out var max);
                    GL.Uniform4(GlProgram.Uniform(shader,"environment_bounds"),min.X,min.Y,max.X-min.X,max.Y-min.Y);}
                GL.Uniform1(GlProgram.Uniform(shader, "shadow_map"), 1);
                globalsUploaded = true;
            }
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2DArray, textures);
            GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, shadowTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void UploadEnvironment()
        {
            GL.ActiveTexture(TextureUnit.Texture5);
            if(environmentTexture==0){environmentTexture=GL.GenTexture();GL.BindTexture(TextureTarget.Texture2D,environmentTexture);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge);}
            GL.BindTexture(TextureTarget.Texture2D,environmentTexture);
            if(environmentRevision!=environment.Revision){
                int columns=environmentTerrain.Settings.TileColumns,rows=environmentTerrain.Settings.TileRows;
                environmentPixels??=new float[columns*rows*4];
                for(int id=0;id<environment.CellCount;id++){
                    var cell=environment.Cell(id);int offset=((id%rows)*columns+id/rows)*4;
                    environmentPixels[offset]=(float)Math.Clamp(cell.Surface/2+cell.Canopy/4,0,1);
                    environmentPixels[offset+1]=(float)(cell.Soil/180);
                    environmentPixels[offset+2]=(float)cell.Drought;
                    environmentPixels[offset+3]=(float)cell.Waterlogging;
                }
                GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba32f,columns,rows,0,PixelFormat.Rgba,PixelType.Float,environmentPixels);
                environmentRevision=environment.Revision;
            }
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void Initialize()
        {
            const string vertex = @"#version 330 core
layout(location=0) in vec3 position;
layout(location=1) in vec4 color;
layout(location=2) in vec3 normal;
uniform mat4 model, camera, light_matrix;
uniform float outline_width;
out vec3 world, smooth_normal;
out vec4 tint, light_position;
void main() {
    vec4 p = model * vec4(position + normal * outline_width, 1);
    world = p.xyz; smooth_normal = mat3(transpose(inverse(model))) * normal;
    tint = color; light_position = light_matrix * p;
    gl_Position = camera * p;
}";
            const string fragment = @"#version 330 core
in vec3 world, smooth_normal;
in vec4 tint, light_position;
out vec4 output_color;
uniform vec2 lod_range;
void lodMask(){
    float rank=fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(0.06711056,0.00583715))));
    if(rank<lod_range.x||rank>=lod_range.y)discard;
}
uniform sampler2DArray materials;
uniform sampler2DShadow shadow_map;
uniform sampler2D environment_map;
uniform vec4 environment_bounds;
uniform int environment_active;
uniform int kind, textured, lit, shadowed;
uniform float outline_width, time, flash, storm;
uniform int clouds;
uniform vec3 sun;
uniform vec4 climate;
float shadow(vec3 n) {
    if(shadowed == 0) return 1;
    vec3 p = light_position.xyz / light_position.w * 0.5 + 0.5;
    if(p.z <= 0 || p.z >= 1 || any(lessThan(p.xy, vec2(0))) || any(greaterThan(p.xy, vec2(1)))) return 1;
    float bias = max(0.00025, 0.0012 * (1 - max(dot(n, sun), 0)));
    float s = 0;
    for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
        s += texture(shadow_map, vec3(p.xy + vec2(x,y)/vec2(textureSize(shadow_map,0)), p.z-bias));
    return s/9;
}
float hash2(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise2(vec2 p){
    vec2 i=floor(p), f=fract(p); f=f*f*(3-2*f);
    return mix(mix(hash2(i),hash2(i+vec2(1,0)),f.x),
        mix(hash2(i+vec2(0,1)),hash2(i+vec2(1)),f.x),f.y);
}
float hash3(vec3 p){return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453);}
float noise3(vec3 p){
    vec3 i=floor(p), f=fract(p); f=f*f*(3-2*f);
    return mix(mix(mix(hash3(i),hash3(i+vec3(1,0,0)),f.x),mix(hash3(i+vec3(0,1,0)),hash3(i+vec3(1,1,0)),f.x),f.y),
        mix(mix(hash3(i+vec3(0,0,1)),hash3(i+vec3(1,0,1)),f.x),mix(hash3(i+vec3(0,1,1)),hash3(i+vec3(1,1,1)),f.x),f.y),f.z);
}
// Species codes from the vertex alpha: 1 cut end grain, 2 spruce, 3 birch, 4 oak, 5 beech.
vec3 barkPattern(int code, vec3 c, vec3 w, vec3 n){
    // Around-the-stem coordinate: world diagonal plus the face direction, so each face of
    // the bole prism continues the pattern instead of repeating it.
    vec2 b = vec2(w.x*0.6 + w.y*0.8 + atan(n.y, n.x)*0.12, w.z);
    if(code == 1) {
        // Fresh end grain: fine radial noise and faint growth bands.
        float grain = noise2(w.xy*7.0)*0.6 + noise2(w.xy*24.0)*0.4;
        float rings = 0.5 + 0.5*sin(noise2(w.xy*2.0)*18.0);
        return c * (0.84 + 0.18*grain + 0.06*rings);
    }
    if(code == 2) {
        // Spruce: reddish-brown, flaking in small rounded scales.
        float scale = noise2(vec2(b.x*8.0, b.y*4.0));
        float seam = smoothstep(0.43,0.5,scale)*smoothstep(0.57,0.5,scale);
        return c * vec3(1.06,0.97,0.90) * (0.76 + 0.34*scale - 0.32*seam);
    }
    if(code == 3) {
        // Birch: chalk-white with dark horizontal lenticels and black patches.
        float lenticel = smoothstep(0.78,0.86, noise2(vec2(b.x*2.5, b.y*26.0)));
        float blotch = smoothstep(0.70,0.80, noise2(b*vec2(1.6,3.2)));
        c *= 0.94 + 0.08*noise2(b*30.0);
        return mix(c, vec3(0.05,0.045,0.04), clamp(lenticel*0.9 + blotch*0.75, 0.0, 1.0));
    }
    if(code == 4) {
        // Oak: deep vertical furrows between rough grey-brown ridges.
        float warp = noise2(vec2(b.x*3.0, b.y*1.2));
        float ridge = smoothstep(0.15,0.65, abs(sin(b.x*16.0 + warp*5.0)));
        return c * (0.50 + 0.62*ridge) * (0.94 + 0.12*noise2(b*vec2(20.0,6.0)));
    }
    // Beech: smooth silver-grey, softly mottled, with green-grey lichen patches.
    float mottle = noise2(b*2.5)*0.6 + noise2(b*9.0)*0.4;
    c *= 0.90 + 0.18*mottle;
    return mix(c, c*vec3(0.86,1.06,0.84), smoothstep(0.62,0.80, noise2(b*vec2(1.3,2.0)))*0.6);
}
vec3 foliagePattern(int code, vec3 c, vec3 w, vec3 n){
    // Leaves on the underside of a crown sit in its own shade.
    float under = 0.78 + 0.26*smoothstep(-0.5, 0.7, n.z);
    if(code == 2) {
        // Spruce: drooping needle sprays in whorled tiers, blue-green.
        float tiers = smoothstep(0.15, 0.85, 0.5 + 0.5*sin(w.z*5.5 + noise3(w*1.5)*3.0));
        float needles = noise3(vec3(w.xy*11.0, w.z*3.0));
        return c * vec3(0.92,1.0,1.06) * (0.64 + 0.32*tiers + 0.22*needles) * under;
    }
    if(code == 3) {
        // Birch: small, loose, light-catching leaves, yellow-green.
        float leaves = noise3(w*7.5);
        float glint = smoothstep(0.72,0.90, noise3(w*16.0));
        return (c * vec3(1.05,1.03,0.90) * (0.80 + 0.30*leaves) + glint*vec3(0.05,0.06,0.015)) * under;
    }
    if(code == 4) {
        // Oak: heavy lobed clumps with deep shadowed gaps.
        float clump = noise3(w*1.9)*0.65 + noise3(w*4.2)*0.35;
        return c * (0.56 + 0.56*smoothstep(0.28,0.72,clump) + 0.12*noise3(w*10.0)) * under;
    }
    // Beech: flat layered sprays, dense and smooth.
    float layer = 0.5 + 0.5*sin(w.z*7.0 + noise3(w*vec3(1.2,1.2,0.6))*4.0);
    return c * vec3(1.0,1.02,0.94) * (0.76 + 0.18*layer + 0.14*noise3(w*vec3(8.0,8.0,14.0))) * under;
}
float cloudDensity(vec2 p){
    p=p/42.0-vec2(time*0.012,time*0.004);
    return smoothstep(0.28,0.72,noise2(p)*0.65+noise2(p*2.03)*0.25+noise2(p*4.1)*0.1);
}
void main() {
    lodMask();
    if(kind == 0) { output_color = tint; return; }
    if(outline_width > 0 && (kind == 4 || kind == 9 || kind == 10)) { output_color = vec4(0.07,0.085,0.09,1); return; }
    if(outline_width > 0) { output_color = vec4(mix(vec3(0.075,0.12,0.045),vec3(0.27,0.32,0.33),climate.z),1); return; }
    vec3 face = cross(dFdx(world),dFdy(world));
    vec3 n = length(face)>0.000001 ? normalize(face) : vec3(0,0,1);
    if(!gl_FrontFacing) n = -n;
    if((kind == 6 || kind == 4 || kind == 9 || kind == 10) && length(smooth_normal)>0.1) n = normalize(smooth_normal);
    if(kind == 1 || kind == 3 || kind == 7 || kind == 8) { if(n.z<0) n=-n; }
    vec3 base = pow(max(tint.rgb,vec3(0)),vec3(2.2));
    int species_code = (kind == 5 || kind == 6) ? int(tint.a*255.0 + 0.5) - 246 : 0;
    if(species_code < 1 || species_code > 5 || (kind == 6 && species_code == 1)) species_code = 0;
    float detail = 1;
    float patch = texture(materials, vec3(world.xy/24.0,1)).r;
    if(textured != 0) {
        float layer = kind == 3 ? 2 : (kind == 5 ? 3 : (kind == 8 || kind == 2 ? 1 : 0));
        vec2 uv = kind == 5 || kind == 2 ? vec2(world.x+world.y,world.z)/4.0 : world.xy/5.0;
        detail = texture(materials,vec3(uv,layer)).r / 0.84;
        detail *= 0.94 + patch * 0.1;
        if(kind == 6) detail = 0.94 + 0.1 * texture(materials,vec3(world.xy/2.5,0)).r;
        if(kind == 4 || kind == 9) detail = 1;
        if(kind == 10) detail = 0.88 + 0.16*noise2(vec2(world.x+world.y,world.z)*12);
        if(kind == 2) detail *= 0.92 + 0.08*sin(world.z*1.8 + patch*2);
        if(species_code > 0) {
            detail = 1;
            base = kind == 5 ? barkPattern(species_code, base, world, n) : foliagePattern(species_code, base, world, n);
        }
    }
    base *= detail;
    vec4 local_environment=environment_active!=0?texture(environment_map,(world.xy-environment_bounds.xy)/environment_bounds.zw):vec4(0);
    float wetness=environment_active!=0?local_environment.r:climate.y;
    if(environment_active!=0&&kind==6)base=mix(base,base*vec3(1.22,0.83,0.52),local_environment.b*0.65);
    if(kind != 2 && kind != 7) base *= 1 - wetness * (kind == 6 ? 0.08 : 0.24);
    float snow = 0;
    if(kind != 2 && kind != 5 && kind != 7) {
        float up = smoothstep(0.25,0.8,n.z);
        snow = smoothstep(0.12,0.85,climate.z - (1-patch)*0.25) * up;
        if(kind == 8) snow *= 0.55;
        base = mix(base,vec3(0.83,0.88,0.93) * (textured != 0 ? texture(materials,vec3(world.xy/3,4)).r : 1),snow);
    }
    if(lit != 0) {
        float direct = max(dot(n,sun),0);
        vec3 ambient = mix(vec3(0.62,0.70,0.79),vec3(0.76,0.81,0.88),climate.x);
        vec3 sunlight = mix(vec3(0.62,0.55,0.43),vec3(0.13,0.14,0.15),climate.x);
        // Beer-Lambert transmittance of a moving, layered coverage field.
        float transmission = clouds != 0 ? exp(-cloudDensity(world.xy + sun.xy*18)*climate.x*(1.2+storm)) : 1;
        base *= ambient * (1-storm*0.16) + sunlight * direct * shadow(n) * transmission;
        base += vec3(0.65,0.75,1) * flash * (0.35+max(n.z,0)*0.65);
    }
    if(lit != 0 && (kind == 4 || kind == 9 || kind == 10)) {
        // Readable form without applying terrain texture to the vehicle paint.
        float edge = pow(1-abs(dot(n,normalize(vec3(0,-0.7,0.7)))),3);
        base += edge * vec3(0.025,0.032,0.04);
        if(kind == 4) base += vec3(0.07)*pow(max(dot(n,normalize(sun+vec3(0,-0.7,0.7))),0),28);
    }
    if(kind == 7) {
        float waves = sin(world.x*2.1+time*1.3)*sin(world.y*1.7-time);
        base *= 0.93 + waves*0.07;
        base += vec3(0.04,0.065,0.08) * pow(max(waves,0),8) * (1-climate.x*0.7);
        // Seeded impacts: each cell has an independent birth time and centre.
        vec2 cell=floor(world.xy/2.5);
        float rings=0;
        for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++){
            vec2 c=cell+vec2(x,y);
            float seed=hash2(c);
            float cycle=floor(time*1.7+seed*7);
            float age=fract(time*1.7+seed*7);
            vec2 centre=(c+vec2(hash2(c+cycle),hash2(c+cycle+13)))*2.5;
            float distance=length(world.xy-centre);
            float ring=exp(-pow((distance-age*1.6)/0.055,2));
            rings+=ring*(1-age)*smoothstep(0,0.08,age);
        }
        base += climate.w * vec3(0.045,0.065,0.08) * rings;
    }
    if(kind == 3 || kind == 1) {
        float wet=wetness*smoothstep(0.7,0.98,patch)*smoothstep(0.75,0.98,n.z);
        vec3 halfVector=normalize(sun+vec3(0,-0.7,0.7));
        base += wet * pow(max(dot(n,halfVector),0),48) * vec3(0.18,0.21,0.25);
    }
    base = mix(base,base*vec3(0.91,0.97,1.07),climate.x*0.35);
    output_color = vec4(pow(max(base,vec3(0)),vec3(1.0/2.2)),species_code > 0 ? 1.0 : tint.a);
}";
            program = GlProgram.Create(vertex, fragment);
            depthProgram = GlProgram.Create(@"#version 330 core
layout(location=0) in vec3 position;
uniform mat4 model, camera;
void main(){gl_Position=camera*model*vec4(position,1);}",
                "#version 330 core\n" + @"uniform vec2 lod_range;
void lodMask(){
    float rank=fract(52.9829189*fract(dot(floor(gl_FragCoord.xy),vec2(0.06711056,0.00583715))));
    if(rank<lod_range.x||rank>=lod_range.y)discard;
}
void main(){lodMask();}");
            textures = ProceduralSurfaceTextures.Upload();
            shadowTexture = GL.GenTexture();
            shadowSize = settings.ShadowResolution;
            GL.BindTexture(TextureTarget.Texture2D, shadowTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, settings.ShadowResolution, settings.ShadowResolution, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int oldFramebuffer);
            shadowFramebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, shadowFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, shadowTexture, 0);
            GL.DrawBuffer(DrawBufferMode.None);
            if(GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Shadow framebuffer is incomplete.");
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, oldFramebuffer);
        }

        public void Dispose()
        {
            if(program != 0) GlProgram.Delete(program);
            if(depthProgram != 0) GlProgram.Delete(depthProgram);
            if(textures != 0) GL.DeleteTexture(textures);
            if(shadowTexture != 0) GL.DeleteTexture(shadowTexture);
            if(shadowFramebuffer != 0) GL.DeleteFramebuffer(shadowFramebuffer);
            if(environmentTexture!=0)GL.DeleteTexture(environmentTexture);environmentTexture=0;
            program = depthProgram = textures = shadowTexture = shadowFramebuffer = 0;
        }
    }
}
