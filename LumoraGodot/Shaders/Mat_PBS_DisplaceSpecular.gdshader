shader_type spatial;
// Displace PBS, specular workflow, back faces culled. Opaque and alpha clip: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_back;

#define PBS_SPECULAR
#include "res://Shaders/PBS_Displace.gdshaderinc"
