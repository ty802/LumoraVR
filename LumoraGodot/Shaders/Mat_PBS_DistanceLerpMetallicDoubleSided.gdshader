shader_type spatial;
// Distance lerp PBS, metallic workflow, both sides drawn. Opaque: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_disabled;

#include "res://Shaders/PBS_DistanceLerp.gdshaderinc"
