namespace ForesTycoon.Rendering
{
    // Shared by the live scene and the editor preview. Keep bark family codes and
    // their procedural detail in one place; lighting remains each caller's concern.
    internal static class OpenGlBarkShader
    {
        internal const string Noise = @"float hash2(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float surfaceNoise2(vec2 p){
    vec2 i=floor(p), f=fract(p); f=f*f*(3-2*f);
    return mix(mix(hash2(i),hash2(i+vec2(1,0)),f.x),
        mix(hash2(i+vec2(0,1)),hash2(i+vec2(1)),f.x),f.y);
}
";
        internal const string Pattern = @"// Species codes from the vertex alpha: 1 cut end grain, 2 spruce, 3 birch, 4 oak, 5 beech.
vec3 barkPattern(int code, vec3 c, vec3 w, vec3 n){
    // Around-the-stem coordinate: world diagonal plus the face direction, so each face of
    // the bole prism continues the pattern instead of repeating it.
    vec2 b = vec2(w.x*0.6 + w.y*0.8 + atan(n.y, n.x)*0.12, w.z);
    if(code == 1) {
        // Fresh end grain: fine radial noise and faint growth bands.
        float grain = surfaceNoise2(w.xy*7.0)*0.6 + surfaceNoise2(w.xy*24.0)*0.4;
        float rings = 0.5 + 0.5*sin(surfaceNoise2(w.xy*2.0)*18.0);
        return c * (0.84 + 0.18*grain + 0.06*rings);
    }
    if(code == 2) {
        // Spruce: reddish-brown, flaking in small rounded scales.
        float scale = surfaceNoise2(vec2(b.x*8.0, b.y*4.0));
        float seam = smoothstep(0.43,0.5,scale)*smoothstep(0.57,0.5,scale);
        return c * vec3(1.06,0.97,0.90) * (0.76 + 0.34*scale - 0.32*seam);
    }
    if(code == 3) {
        // Birch: chalk-white with dark horizontal lenticels and black patches.
        float lenticel = smoothstep(0.78,0.86, surfaceNoise2(vec2(b.x*2.5, b.y*26.0)));
        float blotch = smoothstep(0.70,0.80, surfaceNoise2(b*vec2(1.6,3.2)));
        c *= 0.94 + 0.08*surfaceNoise2(b*30.0);
        return mix(c, vec3(0.05,0.045,0.04), clamp(lenticel*0.9 + blotch*0.75, 0.0, 1.0));
    }
    if(code == 4) {
        // Oak: deep vertical furrows between rough grey-brown ridges.
        float warp = surfaceNoise2(vec2(b.x*3.0, b.y*1.2));
        float ridge = smoothstep(0.15,0.65, abs(sin(b.x*16.0 + warp*5.0)));
        return c * (0.50 + 0.62*ridge) * (0.94 + 0.12*surfaceNoise2(b*vec2(20.0,6.0)));
    }
    // Beech: smooth silver-grey, softly mottled, with green-grey lichen patches.
    float mottle = surfaceNoise2(b*2.5)*0.6 + surfaceNoise2(b*9.0)*0.4;
    c *= 0.90 + 0.18*mottle;
    return mix(c, c*vec3(0.86,1.06,0.84), smoothstep(0.62,0.80, surfaceNoise2(b*vec2(1.3,2.0)))*0.6);
}
";
    }
}
