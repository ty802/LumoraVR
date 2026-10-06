// Additive variant of Unlit.gdshader. Blend mode is a compile-time render_mode in Godot, not a
// uniform, so additive unlit materials swap to this shader (MaterialAssetHook.SetBlendMode). -xlinka
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

#define UNLIT_ADDITIVE
#include "res://Shaders/Unlit_Core.gdshaderinc"
