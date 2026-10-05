// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Poiyomi Toon. A material's shader defines its features (POI_*), the textures it assigns
// (PROP_*), its alpha handling (POI_ALPHA_*) and its render modes, then includes this file (or
// PoiyomiToonOutline.gdshaderinc for the outline pass).

#if defined(POI_DEPTH_RIM) || defined(POI_DEPTHBULGE) || defined(POI_DEPTH_FX)
#define POI_NEEDS_DEPTH_TEXTURE
#endif

#include "res://Shaders/Poiyomi/Core/PoiUniforms.gdshaderinc"
#include "res://Shaders/Poiyomi/Core/PoiCore.gdshaderinc"
#include "res://Shaders/Poiyomi/Core/PoiHelpers.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiVertexMods.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiDissolve.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiVertex.gdshaderinc"
#include "res://Shaders/Poiyomi/Core/PoiStochastic.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiLightData.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiMain.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiShading.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiDecals.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiAniso.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiMatcap.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiCubemap.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiFlipbook.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiEmission.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiRimLighting.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiGlitter.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiSSS.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiReflections.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiStylizedSpecular.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiEffects.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiNormalCorrect.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiBacklight.gdshaderinc"
#include "res://Shaders/Poiyomi/Modules/PoiVoronoi.gdshaderinc"
#include "res://Shaders/Poiyomi/PoiFragment.gdshaderinc"
