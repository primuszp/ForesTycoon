namespace ForesTycoon.Rendering
{
    internal static class OpenGlForestGrowthShader
    {
        // Identical transform in legacy, textured, outline and depth passes.
        internal const string Shader = @"
layout(location=3) in vec3 forest_origin;
layout(location=4) in vec3 forest_rate;
layout(location=5) in float forest_tree;
uniform float forest_elapsed;
uniform float forest_year;
uniform samplerBuffer forest_state;
uniform int forest_dynamic;
out float forest_foliage;
bool forestCrown(vec4 c){
    int code=int(c.a*255.0+0.5);
    return forest_dynamic!=0 && forest_tree>=0 && forest_rate.x > -1.5 && code>=226 && code<=241;
}
vec2 forestSeason(vec4 c){
    if(!forestCrown(c)) return vec2(1,0);
    int slot=int(forest_tree)*4;
    float start=texelFetch(forest_state,slot+2).w;
    if(start<0) return vec2(1,0); // evergreen needles
    float year=fract(forest_year);
    if(year>=0.75) return vec2(0,1); // winter is leafless, including late-winter buds
    vec4 bounds=texelFetch(forest_state,slot+3);
    float phase=fract(forest_year-start);
    float cover=smoothstep(0.0,bounds.x,phase)*(1.0-smoothstep(bounds.z,bounds.w,phase));
    float autumn=smoothstep(bounds.y,mix(bounds.y,bounds.z,0.55),phase);
    return vec2(cover,autumn);
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
vec4 forestTint(vec4 c){
    vec2 season=forestSeason(c);
    forest_foliage=season.x;
    if(forestCrown(c)) c.rgb*=mix(vec3(1),texelFetch(forest_state,int(forest_tree)*4+2).rgb,season.y);
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
vec3 forestPoint(vec3 p){
    return forestFallen()
        ? forest_origin + forestDeadVector(p-forest_origin) + vec3(0,0,forest_rate.z)
        : forest_origin + (p - forest_origin) * forestScale();
}
vec3 forestNormal(vec3 n){
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
    }
}
