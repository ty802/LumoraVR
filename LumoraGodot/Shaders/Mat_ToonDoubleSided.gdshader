shader_type spatial;
// Both sides drawn, which is what hair cards and skirts on imported avatars need. Godot flips NORMAL
// for back faces under cull_disabled, so the ramp lights the back side correctly on its own.
render_mode blend_mix, depth_prepass_alpha, cull_disabled;

#include "res://Shaders/Toon_Core.gdshaderinc"
