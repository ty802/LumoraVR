shader_type spatial;
// Slice PBS, metallic workflow, front faces culled. Alpha blended.
render_mode blend_mix, depth_prepass_alpha, cull_front;

#define TRANSPARENT_PASS
#include "res://Shaders/PBS_Slice.gdshaderinc"
