// UV3 and UV4 share the mesh's CUSTOM1 attribute. CUSTOM0 is reserved for billboards.
varying vec4 lm_uv34;

vec2 mesh_uv(int channel, vec2 first, vec2 second, vec4 extra) {
    if (channel == 1) { return second; }
    if (channel == 2) { return extra.xy; }
    if (channel == 3) { return extra.zw; }
    return first;
}
