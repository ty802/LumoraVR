shader_type spatial;
// Distance lerp PBS, specular workflow, both sides drawn. Opaque: writes no ALPHA, stays in the opaque pass.
render_mode blend_mix, cull_disabled;

#define PBS_SPECULAR
#include "res://Shaders/PBS_DistanceLerp.gdshaderinc"
