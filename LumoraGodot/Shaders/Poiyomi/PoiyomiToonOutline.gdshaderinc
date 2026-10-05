// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Poiyomi's outline pass, for the material's next_pass: the same program with POI_PASS_OUTLINE, which turns
// on the hull extrusion in vertex() and the outline color in fragment(). The material shader defines the
// same features as the base pass; the ones the outline pass never compiles are dropped here.

#define POI_PASS_OUTLINE

#undef POI_DETAIL
#undef POI_PARALLAX
#undef POI_VIDEO_EFFECTS
#undef POI_BACKFACE
#undef POI_TEXT
#undef POI_PATHING
#undef POI_INTERNALPARALLAX
#undef POI_TRUCHET
#undef POI_CUBEMAP
#undef POI_EMISSION_0
#undef POI_EMISSION_1
#undef POI_EMISSION_2
#undef POI_EMISSION_3
#undef POI_RIM_0
#undef POI_RIM_0_POIYOMI
#undef POI_RIM_0_UTS2
#undef POI_RIM_0_LILTOON
#undef POI_RIM_1
#undef POI_RIM_1_POIYOMI
#undef POI_RIM_1_UTS2
#undef POI_RIM_1_LILTOON
#undef POI_DEPTH_RIM
#undef POI_GLITTER
#undef POI_ANISOTROPICS
#undef POI_SUBSURFACESCATTERING
#undef POI_MOCHIE_BRDF
#undef POI_MOCHIE_GGX_ANISO
#undef POI_CLEARCOAT
#undef POI_ENVIRORIM
#undef POI_STYLIZED_SPECULAR
#undef POI_STYLIZED_SPECULAR_UTS
#undef POI_STYLIZED_SPECULAR_LILTOON

#include "res://Shaders/Poiyomi/PoiyomiToon.gdshaderinc"
