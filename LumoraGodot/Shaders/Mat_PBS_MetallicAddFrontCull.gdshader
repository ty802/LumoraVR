shader_type spatial;
// Metallic-workflow PBS, front faces culled, loaded when Culling is Front.
//
// Additive. Never writes depth: an additive surface is light being ADDED to what is behind it, so occluding anything with it is wrong.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode blend_add, depth_draw_never, cull_front;

#define TRANSPARENT_PASS
#include "res://Shaders/PBS_Metallic_Core.gdshaderinc"
