shader_type spatial;
// OVERLAY variant of UI_Unlit: depth test DISABLED so it always draws above world geometry - the
// dash surface and other userspace overlays that must never be occluded by session content. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled;

#include "res://Shaders/UI_Unlit_Core.gdshaderinc"
