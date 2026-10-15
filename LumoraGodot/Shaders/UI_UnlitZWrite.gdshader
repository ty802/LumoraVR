// Depth-WRITING variant of UI_Unlit for panel BACKINGS: the first graphic of a panel writes depth
// (pushed slightly away from the camera so coplanar UI on the panel still passes LEqual), which stops
// UI from rendering through other panels or being visible through walls from behind. Everything else
// matches UI_Unlit so the material hook can set the same uniforms. -xlinka
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_always, cull_disabled;

#define BACKING_PUSH
#include "res://Shaders/UI_Unlit_Core.gdshaderinc"
