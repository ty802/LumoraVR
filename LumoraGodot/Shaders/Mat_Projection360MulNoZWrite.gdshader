shader_type spatial;
// Projection360 material. Multiply. Alpha is ignored. No depth write.
//
// Blend and depth write are compile-time render_modes, so this family ships one file per pair and
// the material hook loads the right one. Culling is a uniform in the core. -xlinka
render_mode unshaded, fog_disabled, blend_mul, cull_disabled, depth_draw_never;

#define P360_ALPHA_ONE
#include "res://Shaders/Projection360_Core.gdshaderinc"
