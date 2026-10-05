shader_type spatial;
// Displace PBS, metallic workflow, both sides drawn. Opaque and alpha clip: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_disabled;

#include "res://Shaders/PBS_Displace.gdshaderinc"
