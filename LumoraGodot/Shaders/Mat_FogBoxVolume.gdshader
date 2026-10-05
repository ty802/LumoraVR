shader_type spatial;
// Fog box volume. Opaque, cutout and alpha: mixed by its alpha, which opaque and cutout hold at one.
//
// Blending is a compile-time render_mode, so this family ships one file per blend and the material
// hook loads the right one. Back faces only, depth test off: see the core. -xlinka
render_mode unshaded, fog_disabled, blend_mix, cull_front, depth_draw_never, depth_test_disabled;

#include "res://Shaders/FogBoxVolume_Core.gdshaderinc"
