shader_type spatial;
// Back-face culling, the default. The material hook swaps this shader for Mat_ToonFrontCull or
// Mat_ToonDoubleSided when Culling changes; uniform values live on the material and survive the swap.
render_mode blend_mix, depth_prepass_alpha, cull_back;

#include "res://Shaders/Toon_Core.gdshaderinc"
