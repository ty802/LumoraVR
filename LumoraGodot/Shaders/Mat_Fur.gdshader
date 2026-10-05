shader_type spatial;
// Fur material, the skin under the shells: the mesh itself, opaque, lit Blinn-Phong. The material hook
// chains the twenty shell passes (Mat_FurShell) after it.
render_mode blend_mix, cull_back;

#include "res://Shaders/Fur_Core.gdshaderinc"
