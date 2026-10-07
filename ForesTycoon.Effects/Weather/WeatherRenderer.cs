using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Effects
{
    internal sealed class WeatherRenderer : IDisposable
    {
        private int program, vao, heightTexture;
        private float[] heights;
        private ulong surfaceRevision = ulong.MaxValue;

        internal void Draw(IWeatherSurface surface, WeatherVisualState weather, RenderContext context, IWeatherSettings settings)
        {
            float intensity = Math.Max(weather.Rain, weather.Snowfall);
            if (intensity < 0.01f) return;
            surface.GetVisibleBounds(out Vector2 visibleMin, out Vector2 visibleMax);
            if (visibleMax.X <= visibleMin.X || visibleMax.Y <= visibleMin.Y) return;
            if (program == 0) Initialize();
            if (heights == null || surfaceRevision != surface.Revision)
            {
                heights ??= new float[surface.Columns * surface.Rows];
                surface.FillHeights(heights);
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, heightTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f,
                    surface.Columns, surface.Rows, 0, PixelFormat.Red, PixelType.Float, heights);
                surfaceRevision = surface.Revision;
            }
            surface.GetBounds(out Vector3 min, out Vector3 max);
            float snow = weather.Snowfall > weather.Rain ? 1 : 0;
            float zoom = Math.Max(1, context.PixelsPerWorldUnit);
            // World cells keep drops anchored while the camera pans.
            float cell = 8;
            Vector2 extent = visibleMax - visibleMin;
            while((MathF.Ceiling(extent.X / cell) + 5) * (MathF.Ceiling(extent.Y / cell) + 5) > settings.RainBudget / 2) cell *= 2;
            Vector2 origin = new Vector2(MathF.Floor(visibleMin.X / cell) - 2, MathF.Floor(visibleMin.Y / cell) - 2);
            int columns = (int)MathF.Ceiling(visibleMax.X / cell) - (int)origin.X + 2;
            int rows = (int)MathF.Ceiling(visibleMax.Y / cell) - (int)origin.Y + 2;
            int perCell = Math.Clamp(settings.RainBudget / Math.Max(1, columns * rows), 1, 24);
            int count = columns * rows * perCell;
            GL.UseProgram(program);
            GL.Uniform4(GlProgram.Uniform(program, "grid"), origin.X, origin.Y, columns, perCell);
            GL.Uniform2(GlProgram.Uniform(program, "wind"), weather.Wind);
            GL.Uniform1(GlProgram.Uniform(program, "cell_size"), cell);
            Matrix4 matrix = RenderDevice.ViewProjection;
            GL.UniformMatrix4(GlProgram.Uniform(program, "camera"), false, ref matrix);
            GL.Uniform4(GlProgram.Uniform(program, "footprint"), min.X, min.Y, max.X - min.X, max.Y - min.Y);
            GL.Uniform4(GlProgram.Uniform(program, "emitter"), visibleMin.X, visibleMin.Y, visibleMax.X - visibleMin.X, visibleMax.Y - visibleMin.Y);
            GL.Uniform4(GlProgram.Uniform(program, "params"), (float)(weather.Time % 4096), snow, intensity, max.Z);
            float yaw = MathHelper.DegreesToRadians(context.CameraYaw);
            GL.Uniform3(GlProgram.Uniform(program, "right"), MathF.Cos(yaw), -MathF.Sin(yaw), 0);
            GL.Uniform1(GlProgram.Uniform(program, "size"), Math.Clamp((snow > 0 ? 1.7f : 0.8f) / zoom, 0.035f, 0.28f));
            GL.Uniform1(GlProgram.Uniform(program, "heights"), 2);
            GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, heightTexture);
            using (new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace).AlphaBlend().DepthWrite(false))
            {
                GL.BindVertexArray(vao);
                GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, count);
                RenderMetrics.RecordDraw(count * 6);
                GL.BindVertexArray(0);
            }
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void Initialize()
        {
            program = GlProgram.Create(@"#version 330 core
uniform mat4 camera;
uniform vec4 footprint, emitter, params, grid;
uniform vec2 wind;
uniform vec3 right;
uniform float size, cell_size;
uniform sampler2D heights;
out vec2 uv;
out float opacity;
const vec2 corners[6] = vec2[6](vec2(-1,-1),vec2(1,-1),vec2(1,1),vec2(-1,-1),vec2(1,1),vec2(-1,1));
void main(){
    float t=params.x, snow=params.y;
    int cellid=gl_InstanceID/int(grid.w);
    vec2 cell=grid.xy+vec2(cellid%int(grid.z),cellid/int(grid.z));
    float id=float(gl_InstanceID%int(grid.w));
    vec3 random=fract(sin(vec3(dot(cell,vec2(127.1,311.7))+id*19.19,
        dot(cell,vec2(269.5,183.3))+id*73.17,dot(cell,vec2(419.2,371.9))+id*31.7))*43758.5453);
    float speed=mix(22.0,2.5,snow);
    float span=max(params.w,8);
    float age=fract(random.z+t*speed/span)*span/speed;
    vec2 xy=cell*cell_size+random.xy*cell_size+wind*age;
    vec2 mapuv=(xy-footprint.xy)/footprint.zw;
    float floorz=texture(heights,mapuv).r;
    vec3 p=vec3(xy,params.w-age*speed);
    float phase=age*speed/span;
    vec2 corner=corners[gl_VertexID]; uv=corner;
    vec3 stretch=normalize(vec3(-wind,speed));
    float length=mix(speed*0.025,size*1.2,snow);
    p += right*corner.x*size + stretch*corner.y*length;
    opacity=params.z * smoothstep(0,0.08,phase)*(1-smoothstep(0.94,1,phase));
    if(p.z < floorz+0.05) opacity=0;
    if(any(lessThan(mapuv,vec2(0))) || any(greaterThan(mapuv,vec2(1)))) opacity=0;
    gl_Position=camera*vec4(p,1);
}", @"#version 330 core
in vec2 uv;
in float opacity;
uniform vec4 params;
out vec4 output_color;
void main(){
    float mask=params.y > 0.5 ? 1-smoothstep(0.45,1,length(uv)) : (1-abs(uv.x))*(1-abs(uv.y));
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
            if(program != 0) GlProgram.Delete(program);
            if(vao != 0) GL.DeleteVertexArray(vao);
            if(heightTexture != 0) GL.DeleteTexture(heightTexture);
            program = vao = heightTexture = 0;
        }
    }
}
