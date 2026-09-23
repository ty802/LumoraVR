shader_type spatial;
// Fresnel material, front faces culled, loaded when Culling is Front.
//
// Multiply. Same reasoning as additive, in the other direction - this is the one a tinted visor wants.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode unshaded, blend_mul, depth_draw_never, cull_front;

#define TRANSPARENT_PASS
#include "res://Shaders/Fresnel_Core.gdshaderinc"
