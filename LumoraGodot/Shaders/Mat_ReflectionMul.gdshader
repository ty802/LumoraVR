shader_type spatial;
// Reflection material, back-face culling (the default).
//
// Multiply. Same reasoning as additive, in the other direction.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode unshaded, fog_disabled, blend_mul, depth_draw_never, cull_back;

#define TRANSPARENT_PASS
#include "res://Shaders/Reflection_Core.gdshaderinc"
