shader_type spatial;
// Distance lerp PBS, metallic workflow, back faces culled. Opaque: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_back;

#include "res://Shaders/PBS_DistanceLerp.gdshaderinc"
