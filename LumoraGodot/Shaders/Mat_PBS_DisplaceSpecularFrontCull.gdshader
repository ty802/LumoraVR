shader_type spatial;
// Displace PBS, specular workflow, front faces culled. Opaque and alpha clip: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_front;

#define PBS_SPECULAR
#include "res://Shaders/PBS_Displace.gdshaderinc"
