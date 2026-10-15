shader_type spatial;
// World-space text writes glyph depth as well as testing it, so later UI draws cannot cover nearer
// letters. Transparent glyph pixels are discarded in the shared body. Writing in the colour pass
// also works on Mobile, where an alpha depth prepass alone does not protect text from later draws.
render_mode unshaded, blend_mix, depth_draw_always, cull_disabled;

#include "res://Shaders/Text_Unlit_Core.gdshaderinc"
