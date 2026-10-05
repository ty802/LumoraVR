shader_type spatial;
// Projection360 material. Opaque and cutout. Writes no ALPHA, so it stays in the OPAQUE pass. Depth written.
//
// Blend and depth write are compile-time render_modes, so this family ships one file per pair and
// the material hook loads the right one. Culling is a uniform in the core. -xlinka
render_mode unshaded, fog_disabled, blend_mix, cull_disabled, depth_draw_opaque;

#include "res://Shaders/Projection360_Core.gdshaderinc"
