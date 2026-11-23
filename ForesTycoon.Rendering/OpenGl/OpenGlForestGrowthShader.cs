namespace ForesTycoon.Rendering
{
    internal static class OpenGlForestGrowthShader
    {
        // Identical transform in legacy, textured, outline and depth passes.
        internal const string Shader = @"
layout(location=3) in vec3 forest_origin;
layout(location=4) in vec3 forest_rate;
layout(location=5) in float forest_tree;
layout(location=6) in vec4 coverage_root_radius;
layout(location=7) in vec4 coverage_form;
layout(location=8) in vec4 coverage_growth;
layout(location=9) in vec4 coverage_crown;
layout(location=10) in vec4 coverage_wood;
layout(location=11) in vec4 coverage_season_tint;
layout(location=12) in vec4 coverage_season_bounds;
uniform int forest_coverage; // 0: explicit mesh, 1: resident crown, 2: resident bole, 3: whorled crown
uniform float forest_elapsed;
uniform float forest_year;
uniform samplerBuffer forest_state;
uniform int forest_dynamic;
// Spring colour of each species' crown relative to its summer colour, by crown code (vertex alpha - 224).
uniform vec3 forest_spring[18];
out float forest_foliage;
bool forestCrown(vec4 c){
    if(forest_coverage != 0) return forest_coverage != 2;
    int code=int(c.a*255.0+0.5);
    return forest_dynamic!=0 && forest_tree>=0 && forest_rate.x > -1.5 && code>=226 && code<=241;
}
vec4 forestSeasonTint(){
    return forest_coverage != 0 ? coverage_season_tint : texelFetch(forest_state,int(forest_tree)*4+2);
}
vec4 forestSeasonBounds(){
    return forest_coverage != 0 ? coverage_season_bounds : texelFetch(forest_state,int(forest_tree)*4+3);
}
vec2 forestSeason(vec4 c){
    if(!forestCrown(c)) return vec2(1,0);
    float start=forestSeasonTint().w;
    if(start<0) return vec2(1,0); // evergreen needles
    float year=fract(forest_year);
    if(year>=0.75) return vec2(0,1); // winter is leafless, including late-winter buds
    vec4 bounds=forestSeasonBounds();
    float phase=fract(forest_year-start);
    float cover=smoothstep(0.0,bounds.x,phase)*(1.0-smoothstep(bounds.z,bounds.w,phase));
    float autumn=smoothstep(bounds.y,mix(bounds.y,bounds.z,0.55),phase);
    return vec2(cover,autumn);
}
// Fresh leaves: full spring colour while the leaves unfold, fading to the summer colour by full leaf.
float forestSpring(vec4 c){
    if(!forestCrown(c)) return 0.0;
    float start=forestSeasonTint().w;
    if(start<0 || fract(forest_year)>=0.75) return 0.0;
    vec4 bounds=forestSeasonBounds();
    return 1.0-smoothstep(bounds.x,bounds.y,fract(forest_year-start));
}
float forestLight(){
    return forest_dynamic != 0 && forest_tree >= 0 && forest_rate.x > -1.5
        ? clamp(texelFetch(forest_state,int(forest_tree)*4+1).w,0,1) : 1;
}
vec3 forestScale(){
    if(forest_rate.x < -1.5) return vec3(1);
    if(forest_dynamic != 0 && forest_tree >= 0 && forest_rate.x > -1.5) {
        int slot=int(forest_tree)*4;
        vec3 scale=texelFetch(forest_state,slot).xyz + texelFetch(forest_state,slot+1).xyz * max(0,forest_elapsed);
        return forest_rate.x < 0 ? scale.yyz : scale.xxz;
    }
    return vec3(1) + forest_rate * max(0, forest_elapsed);
}
// Redistribute the original twelve rim vertices across three whorled rings.
const vec3 whorlHeight = vec3(0.10, 0.53, 0.61);
const vec3 whorlRadius = vec3(1.11, 0.40, 0.68);
const vec3 whorlSlope = vec3(-0.30, 0.66, -0.32);
int whorlRing(){
    return (gl_VertexID - 1) / 4;
}
float coverageElevation(){
    if(gl_VertexID == 0) return 0.0;
    if(gl_VertexID == 13) return 1.0;
    if(forest_coverage == 3) return whorlHeight[whorlRing()];
    return gl_VertexID < 7 ? 0.25 : (gl_VertexID < 13 ? 0.7 : 1.0);
}
vec4 forestTint(vec4 c){
    if(forest_coverage != 0) {
        c = forest_coverage == 2 ? coverage_wood : coverage_crown;
        c.rgb *= mix(0.70, 1.04, coverageElevation());
        c.rgb = mix(c.rgb,vec3(146,118,58)/255.0,clamp(1-coverage_growth.w,0,1)*0.55);
    }
    vec2 season=forestSeason(c);
    forest_foliage=season.x;
    if(forestCrown(c)) {
        c.rgb*=mix(vec3(1),forestSeasonTint().rgb,season.y);
        c.rgb*=mix(vec3(1),forest_spring[clamp(int(c.a*255.0+0.5)-224,0,17)],forestSpring(c));
    }
    if(forest_dynamic != 0 && forest_tree >= 0 && forest_rate.x > -1.5) {
        float health=texelFetch(forest_state,int(forest_tree)*4).w;
        c.rgb=mix(c.rgb,vec3(146,118,58)/255.0,clamp(1-health,0,1)*0.55);
    }
    return c;
}
vec3 forestDeadVector(vec3 v){
    float c=cos(forest_tree), s=sin(forest_tree);
    return vec3(v.z*c-v.y*s, v.z*s+v.y*c, -v.x);
}
bool forestFallen(){return forest_rate.x < -1.5 && forest_year >= forest_rate.y + 2;}
vec2 coverageRadial(){
    if(gl_VertexID == 0 || gl_VertexID == 13) return vec2(0);
    int spoke = gl_VertexID - 1;
    float angle = (forest_coverage == 3 ? float(spoke % 4) * 1.5707963268 : float(spoke % 6) * 1.0471975512) +
        6.2831853072 * fract(dot(coverage_root_radius.xy, vec2(0.067, 0.031)));
    return vec2(cos(angle), sin(angle));
}
vec3 coveragePoint(){
    float height = max(0.01, coverage_form.x + coverage_growth.y * forest_elapsed);
    if(forest_coverage == 3) {
        float t = coverageElevation();
        float reach = max(0.01, coverage_root_radius.w + coverage_growth.x * forest_elapsed);
        float radius = (gl_VertexID == 0 || gl_VertexID == 13) ? 0.0 : reach * whorlRadius[whorlRing()];
        float z = height * (1.0 - coverage_form.y * (1.0 - t));
        return coverage_root_radius.xyz + vec3(coverageRadial() * radius, z);
    }
    float cone = coverage_form.w;
    bool bottom = gl_VertexID == 0, top = gl_VertexID == 13, lower = gl_VertexID < 7;
    if(forest_coverage == 2) {
        float bole = max(0.004, coverage_form.z + coverage_growth.z * forest_elapsed);
        float radius = (lower ? 1.15 : 0.38) * bole;
        float z = bottom || lower ? 0.0 : (top ? 1 : 0.72) * height;
        return coverage_root_radius.xyz + vec3(coverageRadial() * radius, z);
    }
    float reach = max(0.01, coverage_root_radius.w + coverage_growth.x * forest_elapsed);
    float ringHeight = bottom ? 0.0 : (top ? 1.0 : (lower ? mix(0.25, 0.13, cone) : 0.69));
    float ringRadius = reach * (lower ? mix(0.94, 1.11, cone) : mix(0.96, 0.40, cone));
    float z = height * (1.0 - coverage_form.y * (1.0 - ringHeight));
    return coverage_root_radius.xyz + vec3(coverageRadial() * ringRadius, z);
}
vec3 forestPoint(vec3 p){
    if(forest_coverage != 0) return coveragePoint();
    return forestFallen()
        ? forest_origin + forestDeadVector(p-forest_origin) + vec3(0,0,forest_rate.z)
        : forest_origin + (p - forest_origin) * forestScale();
}
vec3 forestNormal(vec3 n){
    if(forest_coverage != 0) {
        float slope = gl_VertexID == 0 ? -1.0 : 1.0;
        if(gl_VertexID > 0 && gl_VertexID < 13)
            slope = forest_coverage == 3 ? whorlSlope[whorlRing()] : (gl_VertexID < 7 ? -0.36 : 0.42);
        return normalize(vec3(coverageRadial(), slope));
    }
    vec3 v=forestFallen() ? forestDeadVector(n) : n / forestScale();
    return length(v)>0 ? normalize(v) : vec3(0);
}
";

        // Shared by textured, plain, outline and shadow passes: bare crowns cast no leaf shadow.
        internal const string FragmentShader = @"
in float forest_foliage;
void forestLeafMask(){
    // Seasonal crowns keep their complete silhouette until leaf fall is finished.
    // Partial coverage must never perforate healthy spring or autumn foliage.
    if(forest_foliage<=0.0) discard;
}
";

        internal const string LodMaskShader = @"
uniform vec2 lod_range;
void forestLodMask(){
    // Stable screen-space ranks with complementary coverage at the same pixel.
    // Fully visible draws skip the hash, including the rest of the scene.
    if(lod_range.x <= 0.0 && lod_range.y >= 1.0) return;
    float rank = fract(52.9829189 * fract(dot(floor(gl_FragCoord.xy), vec2(0.06711056, 0.00583715))));
    if(rank < lod_range.x || rank >= lod_range.y) discard;
}
";
    }
}
