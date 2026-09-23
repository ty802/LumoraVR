shader_type spatial;
// Reflection material, front faces culled, loaded when Culling is Front.
//
// Alpha and transparent: a tinted, see-through mirror, half glass and half reflection.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode unshaded, fog_disabled, blend_mix, depth_prepass_alpha, cull_front;

#define TRANSPARENT_PASS
#include "res://Shaders/Reflection_Core.gdshaderinc"
