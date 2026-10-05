shader_type spatial;
// Slice PBS, specular workflow, both sides drawn. Opaque and alpha clip: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_disabled;

#define PBS_SPECULAR
#include "res://Shaders/PBS_Slice.gdshaderinc"
