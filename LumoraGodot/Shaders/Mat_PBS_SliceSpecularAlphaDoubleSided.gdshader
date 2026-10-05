shader_type spatial;
// Slice PBS, specular workflow, both sides drawn. Alpha blended.
render_mode blend_mix, depth_prepass_alpha, cull_disabled;

#define TRANSPARENT_PASS
#define PBS_SPECULAR
#include "res://Shaders/PBS_Slice.gdshaderinc"
