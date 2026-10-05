shader_type spatial;
// Displace PBS, specular workflow, front faces culled. Alpha blended.
render_mode blend_mix, depth_prepass_alpha, cull_front;

#define TRANSPARENT_PASS
#define PBS_SPECULAR
#include "res://Shaders/PBS_Displace.gdshaderinc"
