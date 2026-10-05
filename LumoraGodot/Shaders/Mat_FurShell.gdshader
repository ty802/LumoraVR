shader_type spatial;
// Fur material, one shell: the mesh pushed out by its fur_layer and cut to strands. Twenty materials wear
// this shader, chained one after the other behind the skin (Mat_Fur).
render_mode blend_mix, depth_draw_always, cull_back;

#define FUR_SHELL
#include "res://Shaders/Fur_Core.gdshaderinc"
