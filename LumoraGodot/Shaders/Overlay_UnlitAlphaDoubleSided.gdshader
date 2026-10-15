shader_type spatial;
// Overlay unlit, blended, both sides drawn, what Culling None loads.
//
// Blending and culling are compile-time render_modes, so the blended overlay ships one file per cull mode
// and the material hook loads the right one. The additive overlay is Overlay_Unlit. -xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled;

#include "res://Shaders/Overlay_UnlitAlpha_Core.gdshaderinc"
