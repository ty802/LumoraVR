shader_type spatial;
// Metallic-workflow PBS, both sides drawn, loaded when Culling is None.
//
// Multiply. Same reasoning as additive, in the other direction - this is the one a tinted visor wants.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode blend_mul, depth_draw_never, cull_disabled;

#define TRANSPARENT_PASS
#include "res://Shaders/PBS_Metallic_Core.gdshaderinc"
