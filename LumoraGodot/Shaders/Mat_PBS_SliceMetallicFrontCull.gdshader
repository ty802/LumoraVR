shader_type spatial;
// Slice PBS, metallic workflow, front faces culled. Opaque and alpha clip: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_front;

#include "res://Shaders/PBS_Slice.gdshaderinc"
