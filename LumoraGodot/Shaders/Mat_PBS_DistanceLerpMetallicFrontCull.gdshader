shader_type spatial;
// Distance lerp PBS, metallic workflow, front faces culled. Opaque: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_front;

#include "res://Shaders/PBS_DistanceLerp.gdshaderinc"
