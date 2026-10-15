shader_type spatial;
// Depth-TESTED against the world (walls occlude panels) but never depth-written; panel backings
// (UI_UnlitZWrite) lay down the depth wall. UI-internal ordering stays render_priority. Read from its
// face only. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, cull_front;

#include "res://Shaders/UI_Unlit_Core.gdshaderinc"
