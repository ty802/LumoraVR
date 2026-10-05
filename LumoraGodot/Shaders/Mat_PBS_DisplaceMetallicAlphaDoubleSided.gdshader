shader_type spatial;
// Displace PBS, metallic workflow, both sides drawn. Alpha blended.
render_mode blend_mix, depth_prepass_alpha, cull_disabled;

#define TRANSPARENT_PASS
#include "res://Shaders/PBS_Displace.gdshaderinc"
