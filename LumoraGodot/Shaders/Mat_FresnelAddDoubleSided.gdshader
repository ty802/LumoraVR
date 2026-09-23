shader_type spatial;
// Fresnel material, both sides drawn, loaded when Culling is None.
//
// Additive. Never writes depth: an additive surface is light being ADDED to what is behind it, so occluding anything with it is wrong.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

#define TRANSPARENT_PASS
#include "res://Shaders/Fresnel_Core.gdshaderinc"
