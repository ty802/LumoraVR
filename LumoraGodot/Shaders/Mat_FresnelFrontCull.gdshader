shader_type spatial;
// Fresnel material, front faces culled, loaded when Culling is Front.
//
// Opaque and cutout. Writes no ALPHA, so Godot keeps this in the OPAQUE pass where it writes depth and is not sorted against other transparents. Cutout still works: discard needs no alpha write.
//
// Blending and culling are compile-time render_modes in gdshader, and whether ALPHA is written at all
// decides which PASS the material lands in, so this family ships one file per combination and the
// material hook loads the right one. Uniform values live on the material and survive the swap. -xlinka
render_mode unshaded, blend_mix, cull_front;

#include "res://Shaders/Fresnel_Core.gdshaderinc"
