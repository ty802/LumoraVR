shader_type spatial;
// Four-way colour pick from a mask texture's channels, over a metallic PBS surface.
//
// Opaque and cutout. Writes no ALPHA, so this stays in Godot's opaque pass with proper depth.
render_mode blend_mix, cull_back, depth_draw_opaque;

#include "res://Shaders/PBS_ColorMask_Core.gdshaderinc"
