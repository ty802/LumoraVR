shader_type spatial;
// Fog box volume. Additive, the default. Alpha is ignored, as additive fog adds its colour whole.
//
// Blending is a compile-time render_mode, so this family ships one file per blend and the material
// hook loads the right one. Back faces only, depth test off: see the core. -xlinka
render_mode unshaded, fog_disabled, blend_add, cull_front, depth_draw_never, depth_test_disabled;

#define FOG_ALPHA_ONE
#include "res://Shaders/FogBoxVolume_Core.gdshaderinc"
