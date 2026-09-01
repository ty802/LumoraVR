shader_type spatial;
// Front faces dropped, for geometry meant to be seen from inside.
render_mode blend_mix, depth_prepass_alpha, cull_front;

#include "res://Shaders/Toon_Core.gdshaderinc"
