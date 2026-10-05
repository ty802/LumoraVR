// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Material properties, under Poiyomi's names and defaults. Generated from the base pass's declaration block;
// regenerate rather than hand edit.

#if defined(PROP_LIGHTINGAOMAPS)
uniform sampler2D _LightingAOMaps : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_LIGHTINGDETAILSHADOWMAPS)
uniform sampler2D _LightingDetailShadowMaps : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_LIGHTINGSHADOWMASKS)
uniform sampler2D _LightingShadowMasks : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_LIGHTDATASDFMAP)
uniform sampler2D _LightDataSDFMap : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif

uniform float _IgnoreFog = 0.0;
uniform float _RenderingReduceClipDistance = 0.0;
uniform int _FlipBackfaceNormals = 1;
uniform float _AddBlendOp = 4.0;
uniform float _Cull = 2.0;

uniform float _RenderingAOBlockerEnabled = 0.0;
uniform float _RenderingAOBlockerUVChannel = 0.0;
uniform float _RenderingAOBlockerFlipNormal = 0.0;

uniform sampler2D _MainTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#if defined(PROP_BUMPMAP)
uniform sampler2D _BumpMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(POI_BUMP2NDMAP)
#if defined(PROP_BUMP2NDMAP)
uniform sampler2D _Bump2ndMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_BUMP2NDSCALEMASK)
uniform sampler2D _Bump2ndScaleMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(PROP_ALPHAMASK)
uniform sampler2D _AlphaMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(POI_COLOR_ADJUST)
#if defined(PROP_MAINCOLORADJUSTTEXTURE)
uniform sampler2D _MainColorAdjustTexture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_MAINTINTTEXTURE)
uniform sampler2D _MainTintTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_MAINGRADATIONTEX)
uniform sampler2D _MainGradationTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_BENTNORMALMAP)
#if defined(PROP_BENTNORMALMAP)
uniform sampler2D _BentNormalMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_PASS_OUTLINE)
#if defined(PROP_OUTLINETEXTURE)
uniform sampler2D _OutlineTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_OUTLINEMASK)
uniform sampler2D _OutlineMask : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#endif

#if defined(POI_GLOBALMASK_TEXTURES)
#if defined(PROP_GLOBALMASKTEXTURE0)
uniform sampler2D _GlobalMaskTexture0 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_GLOBALMASKTEXTURE1)
uniform sampler2D _GlobalMaskTexture1 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_GLOBALMASKTEXTURE2)
uniform sampler2D _GlobalMaskTexture2 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_GLOBALMASKTEXTURE3)
uniform sampler2D _GlobalMaskTexture3 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_DISTORTION)
#if defined(PROP_DISTORTIONFLOWTEXTURE)
uniform sampler2D _DistortionFlowTexture : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DISTORTIONFLOWTEXTURE1)
uniform sampler2D _DistortionFlowTexture1 : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DISTORTIONMASK)
uniform sampler2D _DistortionMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_PARALLAX) && defined(PROP_HEIGHTMAP)
uniform sampler2D _HeightMap : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#if defined(PROP_HEIGHTMASK)
uniform sampler2D _Heightmask : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#endif

#if defined(POI_DETAIL)
#if defined(PROP_DETAILMASK)
uniform sampler2D _DetailMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DETAILNORMALMAP)
uniform sampler2D _DetailNormalMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DETAILTEX)
uniform sampler2D _DetailTex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_VERTEX_MANIPULATION)
#if defined(PROP_VERTEXBASICSMASK)
uniform sampler2D _VertexBasicsMask : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_VERTEXMANIPULATIONHEIGHTMASK)
uniform sampler2D _VertexManipulationHeightMask : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#endif

#if defined(POI_VERTEX_LOOKAT)
#if defined(PROP_LOOKATMASK)
uniform sampler2D _LookAtMask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#endif

#if defined(POI_VERTEX_GLITCHING)
uniform sampler2D _VertexGlitchMap : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D _VertexGlitchingMask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif

#if defined(POI_SKIN)
#if defined(PROP_POISKINLUT)
uniform sampler2D _PoiSkinLUT : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_POISKINDETAILMAP)
uniform sampler2D _PoiSkinDetailMap : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_DEPTHBULGE)
#if defined(PROP_DEPTHBULGEMASK)
uniform sampler2D _DepthBulgeMask : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#endif

#if defined(POI_BACKFACE)
#if defined(PROP_BACKFACETEXTURE)
uniform sampler2D _BackFaceTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_BACKFACEMASK)
uniform sampler2D _BackFaceMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_RGBMASK)
#if defined(PROP_RGBMASK)
uniform sampler2D _RGBMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RGBAMETALLICMAPS)
uniform sampler2D _RGBAMetallicMaps : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RGBASMOOTHNESSMAPS)
uniform sampler2D _RGBASmoothnessMaps : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_REDTEXTURE)
uniform sampler2D _RedTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RGBNORMALR)
uniform sampler2D _RgbNormalR : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_GREENTEXTURE)
uniform sampler2D _GreenTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RGBNORMALG)
uniform sampler2D _RgbNormalG : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_BLUETEXTURE)
uniform sampler2D _BlueTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RGBNORMALB)
uniform sampler2D _RgbNormalB : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_ALPHATEXTURE)
uniform sampler2D _AlphaTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RGBNORMALA)
uniform sampler2D _RgbNormalA : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_LIGHTING_TEXTURERAMP)
uniform sampler2D _ToonRamp : hint_default_white, filter_linear_mipmap, repeat_disable;
#endif
#if defined(POI_LIGHTING_SHADEMAP)
#if defined(PROP_1ST_SHADEMAP)
uniform sampler2D _1st_ShadeMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_2ND_SHADEMAP)
uniform sampler2D _2nd_ShadeMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_LIGHTING_SKIN)
#if defined(PROP_SKINLUT)
uniform sampler2D _SkinLUT : hint_default_white, filter_linear_mipmap, repeat_disable;
#endif
#if defined(PROP_SKINTHICKNESSMAP)
uniform sampler2D _SkinThicknessMap : source_color, hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_LIGHTING_MULTILAYER_MATH)
#if defined(PROP_SHADOWBORDERMASK)
uniform sampler2D _ShadowBorderMask : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_SHADOWCOLORTEX)
uniform sampler2D _ShadowColorTex : source_color, hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_MULTILAYERMATHBLURMAP)
uniform sampler2D _MultilayerMathBlurMap : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_SHADOW2NDCOLORTEX)
uniform sampler2D _Shadow2ndColorTex : source_color, hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_SHADOW3RDCOLORTEX)
uniform sampler2D _Shadow3rdColorTex : source_color, hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_SHADOWSTRENGTHMASK)
uniform sampler2D _ShadowStrengthMask : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_LIGHTING_CLOTH)
uniform sampler2D _ClothDFG : hint_default_black, filter_linear_mipmap, repeat_disable;
#if defined(PROP_CLOTHMETALLICSMOOTHNESSMAP)
uniform sampler2D _ClothMetallicSmoothnessMap : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_LIGHTING_SDF)
#if defined(PROP_SDFSHADINGTEXTURE)
uniform sampler2D _SDFShadingTexture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(PROP_DECALMASK)
uniform sampler2D _DecalMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif

#if defined(POI_DECAL_0)
uniform sampler2D _DecalTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif

#if defined(POI_DECAL_1)
uniform sampler2D _DecalTexture1 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif

#if defined(POI_DECAL_2)
uniform sampler2D _DecalTexture2 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif

#if defined(POI_DECAL_3)
uniform sampler2D _DecalTexture3 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif

#if defined(POI_DISSOLVE)
#if defined(PROP_DISSOLVETOTEXTURE)
uniform sampler2D _DissolveToTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DISSOLVENOISETEXTURE)
uniform sampler2D _DissolveNoiseTexture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DISSOLVEDETAILNOISE)
uniform sampler2D _DissolveDetailNoise : hint_default_black, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_DISSOLVEMASK)
uniform sampler2D _DissolveMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_ANISOTROPICS)
#if defined(PROP_ANISOCOLORMAP)
uniform sampler2D _AnisoColorMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_MATCAP_0)
#if defined(PROP_MATCAP)
uniform sampler2D _Matcap : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_MATCAPMASK)
uniform sampler2D _MatcapMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(POI_MATCAP_0_CUSTOM_NORMAL)
#if defined(PROP_MATCAP0NORMALMAP)
uniform sampler2D _Matcap0NormalMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif
#if defined(POI_MATCAP_1)
#if defined(PROP_MATCAP2)
uniform sampler2D _Matcap2 : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_MATCAP2MASK)
uniform sampler2D _Matcap2Mask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(POI_MATCAP_1_CUSTOM_NORMAL)
#if defined(PROP_MATCAP1NORMALMAP)
uniform sampler2D _Matcap1NormalMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif
#if defined(POI_MATCAP_2)
#if defined(PROP_MATCAP3)
uniform sampler2D _Matcap3 : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_MATCAP3MASK)
uniform sampler2D _Matcap3Mask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(POI_MATCAP_2_CUSTOM_NORMAL)
#if defined(PROP_MATCAP2NORMALMAP)
uniform sampler2D _Matcap2NormalMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif
#if defined(POI_MATCAP_3)
#if defined(PROP_MATCAP4)
uniform sampler2D _Matcap4 : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_MATCAP4MASK)
uniform sampler2D _Matcap4Mask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(POI_MATCAP_3_CUSTOM_NORMAL)
#if defined(PROP_MATCAP3NORMALMAP)
uniform sampler2D _Matcap3NormalMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif

#if defined(POI_CUBEMAP)
#if defined(PROP_CUBEMAP)
uniform samplerCube _CubeMap : filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_CUBEMAPMASK)
uniform sampler2D _CubeMapMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_FLIPBOOK_0)
uniform sampler2DArray _FlipbookTexArray : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
#if defined(PROP_FLIPBOOKMASK)
uniform sampler2D _FlipbookMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_FLIPBOOK_1)
uniform sampler2DArray _FlipbookTexArray1 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
#if defined(PROP_FLIPBOOKMASK1)
uniform sampler2D _FlipbookMask1 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_EMISSION_0)
#if defined(PROP_EMISSIONMAP)
uniform sampler2D _EmissionMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONMASK)
uniform sampler2D _EmissionMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONSCROLLINGCURVE)
uniform sampler2D _EmissionScrollingCurve : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_EMISSION_1)
#if defined(PROP_EMISSIONMAP1)
uniform sampler2D _EmissionMap1 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONMASK1)
uniform sampler2D _EmissionMask1 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONSCROLLINGCURVE1)
uniform sampler2D _EmissionScrollingCurve1 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_EMISSION_2)
#if defined(PROP_EMISSIONMAP2)
uniform sampler2D _EmissionMap2 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONMASK2)
uniform sampler2D _EmissionMask2 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONSCROLLINGCURVE2)
uniform sampler2D _EmissionScrollingCurve2 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_EMISSION_3)
#if defined(PROP_EMISSIONMAP3)
uniform sampler2D _EmissionMap3 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONMASK3)
uniform sampler2D _EmissionMask3 : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_EMISSIONSCROLLINGCURVE3)
uniform sampler2D _EmissionScrollingCurve3 : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_RIM_0)
#if defined(POI_RIM_0_POIYOMI)
#if defined(PROP_RIMTEX)
uniform sampler2D _RimTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RIMMASK)
uniform sampler2D _RimMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_RIM_0_UTS2)
#if defined(PROP_SET_RIMLIGHTMASK)
uniform sampler2D _Set_RimLightMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_RIM_0_LILTOON)
#if defined(PROP_RIMCOLORTEX)
uniform sampler2D _RimColorTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif
#if defined(POI_RIM_1)
#if defined(POI_RIM_1_POIYOMI)
#if defined(PROP_RIM2TEX)
uniform sampler2D _Rim2Tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RIM2MASK)
uniform sampler2D _Rim2Mask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_RIM2WIDTHNOISETEXTURE)
uniform sampler2D _Rim2WidthNoiseTexture : filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_RIM_1_UTS2)
#if defined(PROP_SET_RIM2LIGHTMASK)
uniform sampler2D _Set_Rim2LightMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_RIM_1_LILTOON)
#if defined(PROP_RIM2COLORTEX)
uniform sampler2D _Rim2ColorTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif

#if defined(POI_DEPTH_RIM)
#if defined(PROP_DEPTHRIMMASK)
uniform sampler2D _DepthRimMask : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_GLITTER)
#if defined(PROP_GLITTERMASK)
uniform sampler2D _GlitterMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_GLITTERCOLORMAP)
uniform sampler2D _GlitterColorMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_GLITTERTEXTURE)
uniform sampler2D _GlitterTexture : source_color, hint_default_white, filter_linear_mipmap, repeat_disable;
#endif

#endif

#if defined(POI_SUBSURFACESCATTERING)
#if defined(PROP_SSSTHICKNESSMAP)
uniform sampler2D _SSSThicknessMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_MOCHIE_BRDF)
#if defined(PROP_ANISOTROPYMAP)
uniform sampler2D _AnisotropyMap : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_MOCHIEMETALLICMAPS)
uniform sampler2D _MochieMetallicMaps : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_MOCHIEREFLCUBE)
uniform samplerCube _MochieReflCube : filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_CLEARCOAT)
#if defined(PROP_CLEARCOATMAPS)
uniform sampler2D _ClearCoatMaps : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_CLEARCOATFALLBACK)
uniform samplerCube _ClearCoatFallback : filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_ENVIRORIM)
#if defined(PROP_RIMENVIROMASK)
uniform sampler2D _RimEnviroMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_STYLIZED_SPECULAR)
#if defined(POI_STYLIZED_SPECULAR_UTS)
#if defined(PROP_HIGHCOLOR_TEX)
uniform sampler2D _HighColor_Tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_SET_HIGHCOLORMASK)
uniform sampler2D _Set_HighColorMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#if defined(POI_STYLIZED_SPECULAR_LILTOON)
#if defined(PROP_SMOOTHNESSTEX)
uniform sampler2D _SmoothnessTex : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_METALLICGLOSSMAP)
uniform sampler2D _MetallicGlossMap : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_REFLECTIONCOLORTEX)
uniform sampler2D _ReflectionColorTex : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_REFLECTIONCUBETEX)
uniform samplerCube _ReflectionCubeTex : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif

#if defined(POI_PATHING)
#if defined(PROP_PATHINGMAP)
uniform sampler2D _PathingMap : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif
#if defined(PROP_PATHINGCOLORMAP)
uniform sampler2D _PathingColorMap : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_MIRROR)
#if defined(POI_MIRROR_TEXTURE)
#if defined(PROP_MIRRORTEXTURE)
uniform sampler2D _MirrorTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif
#endif

#if defined(POI_DEPTH_FX)
#if defined(PROP_DEPTHMASK)
uniform sampler2D _DepthMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_DEPTHTEXTURE)
uniform sampler2D _DepthTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_TEXT)
uniform sampler2D _TextGlyphs : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;

#endif

#if defined(POI_POSTPROCESS)
#if defined(PROP_PPMASK)
uniform sampler2D _PPMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif

#endif

#if defined(POI_INTERNALPARALLAX)
#if defined(PROP_PARALLAXINTERNALMAP)
uniform sampler2D _ParallaxInternalMap : source_color, hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_PARALLAXINTERNALMAPMASK)
uniform sampler2D _ParallaxInternalMapMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_VIDEO_EFFECTS)
#if defined(PROP_VIDEOPIXELTEXTURE)
uniform sampler2D _VideoPixelTexture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_VIDEOMASKTEXTURE)
uniform sampler2D _VideoMaskTexture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_VIDEOGAMEBOYRAMP)
uniform sampler2D _VideoGameboyRamp : source_color, hint_default_white, filter_linear_mipmap, repeat_disable;
#endif
#endif

#if defined(POI_BACKLIGHT)
#if defined(PROP_BACKLIGHTCOLORTEX)
uniform sampler2D _BacklightColorTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_VORONOI)
#if defined(PROP_VORONOIMASK)
uniform sampler2D _VoronoiMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_VORONOINOISE)
uniform sampler2D _VoronoiNoise : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(POI_TRUCHET)
#if defined(PROP_TRUCHETTEX)
uniform sampler2D _TruchetTex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#if defined(PROP_TRUCHETMASK)
uniform sampler2D _TruchetMask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
#endif
#endif

#if defined(PROP_UZUMOREMASK)
uniform sampler2D _UzumoreMask : hint_default_white, filter_linear_mipmap, repeat_enable;
#endif

uniform float _Mode = 0.0;

#if !defined(POI_STOCHASTIC_NONE)
#if defined(POI_STOCHASTIC_DELIOT_HEITZ)
uniform float _StochasticDeliotHeitzDensity = 1.0;
#endif
#if defined(POI_STOCHASTIC_HEXTILE)
uniform float _StochasticHexGridDensity = 1.0;
uniform float _StochasticHexRotationStrength = 0.0;
uniform float _StochasticHexFallOffContrast = 0.6;
uniform float _StochasticHexFallOffPower = 7.0;
#endif
#endif

uniform vec4 _LightingAOMaps_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _LightingAOMapsPan = vec2(0.0, 0.0);
uniform float _LightingAOMapsUV = 0.0;
uniform float _LightDataAOStrengthR = 1.0;
uniform float _LightDataAOStrengthG = 0.0;
uniform float _LightDataAOStrengthB = 0.0;
uniform float _LightDataAOStrengthA = 0.0;
uniform float _LightDataAOGlobalMaskR = 0.0;
uniform float _LightDataAOGlobalMaskBlendTypeR = 2.0;
uniform vec4 _LightingDetailShadowMaps_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _LightingDetailShadowMapsPan = vec2(0.0, 0.0);
uniform float _LightingDetailShadowMapsUV = 0.0;
uniform float _LightingDetailShadowStrengthR = 1.0;
uniform float _LightingDetailShadowStrengthG = 0.0;
uniform float _LightingDetailShadowStrengthB = 0.0;
uniform float _LightingDetailShadowStrengthA = 0.0;
uniform float _LightingAddDetailShadowStrengthR = 1.0;
uniform float _LightingAddDetailShadowStrengthG = 0.0;
uniform float _LightingAddDetailShadowStrengthB = 0.0;
uniform float _LightingAddDetailShadowStrengthA = 0.0;
uniform float _LightDataDetailShadowGlobalMaskR = 0.0;
uniform float _LightDataDetailShadowGlobalMaskBlendTypeR = 2.0;
uniform vec4 _LightingShadowMasks_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _LightingShadowMasksPan = vec2(0.0, 0.0);
uniform float _LightingShadowMasksUV = 0.0;
uniform float _LightingShadowMaskStrengthR = 1.0;
uniform float _LightingShadowMaskStrengthG = 0.0;
uniform float _LightingShadowMaskStrengthB = 0.0;
uniform float _LightingShadowMaskStrengthA = 0.0;
uniform float _LightDataShadowMaskGlobalMaskR = 0.0;
uniform float _LightDataShadowMaskGlobalMaskBlendTypeR = 2.0;

uniform float _Unlit_Intensity = 1.0;
uniform float _LightingColorMode = 0.0;
uniform float _LightingMapMode = 0.0;

uniform vec4 _LightDataSDFMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _LightDataSDFMapPan = vec2(0.0, 0.0);
uniform float _LightDataSDFMapUV = 0.0;
uniform float _LightDataSDFMapLOD = 0.0;
uniform float _LightDataSDFBlendY = 1.0;
uniform float _LightingDirectionMode = 0.0;
uniform vec3 _LightngForcedDirection = vec3(0.0, 0.0, 0.0);
uniform float _LightingViewDirOffsetPitch = 0.0;
uniform float _LightingViewDirOffsetYaw = 0.0;
uniform float _LightingIndirectUsesNormals = 0.0;
uniform float _LightingCapEnabled = 1.0;
uniform float _LightingCap = 1.0;
uniform float _LightingForceColorEnabled = 0.0;
uniform vec3 _LightingForcedColor : source_color = vec3(1.0, 1.0, 1.0);
uniform float _LightingForcedColorThemeIndex = 0.0;
uniform float _LightingCastedShadows = 0.0;
uniform float _LightingShadowSoftness = 0.0;
uniform float _LightingMonochromatic = 0.0;
uniform float _LightingMinLightBrightness = 0.0;
uniform float _LightingAdditiveEnable = 1.0;
uniform float _LightingAdditiveLimited = 1.0;
uniform float _LightingAdditiveLimit = 1.0;
uniform float _LightingAdditiveCastedShadows = 1.0;
uniform float _LightingAdditiveMonochromatic = 0.0;
uniform float _LightingAdditivePassthrough = 0.0;
uniform vec4 _LightingAdditiveAttenCurve = vec4(0.0, 0.3333333, 0.6666667, 1.0);
uniform float _DisableDirectionalInAdd = 0.0;
uniform float _LightingVertexLightingEnabled = 1.0;
uniform float _LightingMirrorVertexLightingEnabled = 1.0;
uniform float _LightingEnableLightVolumes = 1.0;
uniform float _LightingLightVolumesIntensity = 1.0;
uniform float _LightingLightVolumesNormalBias = 0.0;
uniform float _LightingLightVolumesSpecular = 1.0;
uniform float _LightingLightVolumesShadows = 0.0;
uniform float _LightDataDebugEnabled = 0.0;
uniform float _LightingDebugVisualize = 0.0;

uniform vec4 _Color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _ColorThemeIndex = 0.0;
uniform float _MainPixelMode = 0.0;
uniform vec4 _MainTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MainTexPan = vec2(0.0, 0.0);
uniform float _MainTexUV = 0.0;
uniform float _MainTexStochastic = 0.0;
uniform float _MainIgnoreTexAlpha = 0.0;
uniform vec4 _BumpMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _BumpMapPan = vec2(0.0, 0.0);
uniform float _BumpMapUV = 0.0;
uniform float _BumpScale = 1.0;
uniform float _BumpMapStochastic = 0.0;
#if defined(POI_BUMP2NDMAP)
uniform vec4 _Bump2ndMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Bump2ndMapPan = vec2(0.0, 0.0);
uniform float _Bump2ndMapUV = 0.0;
uniform float _Bump2ndScale = 1.0;
uniform float _Bump2ndMapStochastic = 0.0;
uniform vec4 _Bump2ndScaleMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Bump2ndScaleMaskPan = vec2(0.0, 0.0);
uniform float _Bump2ndScaleMaskUV = 0.0;
uniform float _Bump2ndScaleMaskGlobalMask = 0.0;
uniform float _Bump2ndScaleMaskGlobalMaskBlendType = 2.0;
#endif
uniform vec4 _AlphaMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _AlphaMaskPan = vec2(0.0, 0.0);
uniform float _AlphaMaskUV = 0.0;
uniform float _AlphaMaskInvert = 0.0;
uniform float _MainAlphaMaskMode = 2.0;
uniform float _AlphaMaskR = 1.0;
uniform float _AlphaMaskG = 0.0;
uniform float _AlphaMaskB = 0.0;
uniform float _AlphaMaskA = 0.0;
uniform float _AlphaMaskChannelBlendMode = 2.0;
uniform vec2 _AlphaMaskMinMax = vec2(0.0, 1.0);
uniform float _AlphaMaskGamma = 1.0;
uniform float _AlphaMaskBlendStrength = 1.0;
uniform float _AlphaMaskValue = 0.0;
uniform float _Cutoff = 0.5;
#if defined(POI_COLOR_ADJUST)
uniform float _MainColorAdjustToggle = 0.0;
uniform vec4 _MainColorAdjustTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MainColorAdjustTexturePan = vec2(0.0, 0.0);
uniform float _MainColorAdjustTextureUV = 0.0;
uniform float _MainHueShiftColorSpace = 0.0;
uniform float _MainHueShiftSelectOrShift = 1.0;
uniform float _MainHueShiftToggle = 0.0;
uniform float _MainHueShiftReplace = 1.0;
uniform float _MainHueShift = 0.0;
uniform float _MainHueShiftSpeed = 0.0;
uniform float _Saturation = 0.0;
uniform float _MainChromatize = 0.0;
uniform vec4 _MainTintColor : source_color = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 _MainTintTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MainTintTexturePan = vec2(0.0, 0.0);
uniform float _MainTintTextureUV = 0.0;
uniform float _MainBrightness = 0.0;
uniform float _MainColorAdjustClamp = 1.0;
uniform float _MainGamma = 1.0;

uniform float _MainHueALCTEnabled = 0.0;
uniform float _MainALHueShiftBand = 0.0;
uniform float _MainALHueShiftCTIndex = 0.0;
uniform float _MainHueALMotionSpeed = 1.0;

uniform float _MainHueGlobalMask = 0.0;
uniform float _MainHueGlobalMaskBlendType = 2.0;
uniform float _MainSaturationGlobalMask = 0.0;
uniform float _MainSaturationGlobalMaskBlendType = 2.0;
uniform float _MainBrightnessGlobalMask = 0.0;
uniform float _MainBrightnessGlobalMaskBlendType = 2.0;
uniform float _MainGammaGlobalMask = 0.0;
uniform float _MainGammaGlobalMaskBlendType = 2.0;
uniform float _ColorGradingToggle = 0.0;
uniform float _MainGradationStrength = 0.0;
uniform float _MainGradationMaskChannel = 0.0;
#endif

uniform float _AlphaForceOpaque = 1.0;
uniform float _AlphaMod = 0.0;
uniform float _AlphaPremultiply = 0.0;
uniform float _AlphaBoostFA = 10.0;
uniform float _AlphaToCoverage = 0.0;
uniform float _AlphaSharpenedA2C = 0.0;
uniform float _AlphaMipScale = 0.25;

uniform float _AlphaDithering = 0.0;
uniform float _AlphaDitherGradient = 0.1;
uniform float _AlphaDitherBias = 0.0;

uniform float _AlphaDistanceFade = 0.0;
uniform float _AlphaDistanceFadeType = 1.0;
uniform float _AlphaDistanceFadeMinAlpha = 0.0;
uniform float _AlphaDistanceFadeMaxAlpha = 1.0;
uniform float _AlphaDistanceFadeMin = 0.0;
uniform float _AlphaDistanceFadeMax = 0.0;
uniform float _AlphaDistanceFadeGlobalMask = 0.0;
uniform float _AlphaDistanceFadeGlobalMaskBlendType;

uniform float _AlphaFresnel = 0.0;
uniform float _AlphaFresnelAlpha = 0.0;
uniform float _AlphaFresnelSharpness = 0.5;
uniform float _AlphaFresnelWidth = 0.5;
uniform float _AlphaFresnelInvert = 0.0;
uniform float _AlphaFresnelGlobalMask = 0.0;
uniform float _AlphaFresnelGlobalMaskBlendType;

uniform float _AlphaAngular = 0.0;
uniform float _AngleType = 0.0;
uniform float _AngleCompareTo = 0.0;
uniform vec3 _AngleForwardDirection = vec3(0.0, 0.0, 1.0);
uniform float _CameraAngleMin = 45.0;
uniform float _CameraAngleMax = 90.0;
uniform float _ModelAngleMin = 45.0;
uniform float _ModelAngleMax = 90.0;
uniform float _AngleMinAlpha = 0.0;
uniform float _AlphaAngularGlobalMask = 0.0;
uniform float _AlphaAngularGlobalMaskBlendType;

uniform float _AlphaGlobalMask = 0.0;
uniform float _AlphaGlobalMaskBlendType = 2.0;

#if defined(POI_BENTNORMALMAP)
uniform vec4 _BentNormalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _BentNormalMapPan = vec2(0.0, 0.0);
uniform float _BentNormalMapUV = 0.0;
uniform float _BentNormalMapStochastic = 0.0;
uniform float _BentNormalSpecOccStrength = 1.0;
uniform float _BentNormalIndirectDiffuse = 1.0;
#endif

#if defined(POI_PASS_OUTLINE)
uniform float _OutlineExpansionMode = 1.0;
uniform vec4 _OutlinePersonaDirection = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 _OutlineDropShadowOffset = vec4(1.0, 0.0, 0.0, 0.0);
uniform float _OutlineUseVertexColorNormals = 0.0;

uniform float _OutlineVertexColorMask = 0.0;
uniform float _OutlineVertexColorMaskStrength = 1.0;

uniform float _OutlineZOffsetVertexColor = 0.0;
uniform float _OutlineZOffsetVertexColorStrength = 1.0;
uniform float _OutlineZOffsetChannel = 0.0;
uniform float _OutlineZOffsetMaskStrength = 1.0;
uniform float _OutlineZOffsetInvertMaskChannel = 0.0;

uniform float _OutlineFixedSize = 1.0;
uniform float _OutlineFixWidth = 0.5;
uniform float _EnableOutlines = 0.0;
uniform float _EnableOutlinesRenderFeature = 0.0;
uniform float _OutlinesMaxDistance = 1.0;
uniform float _LineWidth = 1.0;
uniform float _OutlineEmission = 0.0;
uniform vec4 _LineColor : source_color = vec4(0.79, 0.77, 0.87, 1.0);
uniform float _OutlineOverrideAlpha = 0.0;
uniform float _OutlineSpace = 0.0;
uniform vec4 _OutlineTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _OutlineTexturePan = vec2(0.0, 0.0);
uniform float _OutlineTextureUV = 0.0;
uniform vec4 _OutlineFadeDistance;
uniform vec4 _OutlineGlobalPan;
uniform vec4 _OutlineMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _OutlineMaskPan = vec2(0.0, 0.0);
uniform float _OutlineMaskUV = 0.0;
uniform float _OutlineMaskChannel = 0.0;
uniform float _OutlineRimLightBlend = 0.0;
uniform float _OutlineLit = 1.0;
uniform float _OutlineTintMix = 0.0;

uniform float _OutlineHighlightEnabled = 0.0;
uniform vec4 _OutlineHighlightColor : source_color = vec4(1.0, 0.48, 0.0, 1.0);
uniform float _OutlineHighlightColorThemeIndex = 0.0;
uniform float _OutlineHighlightApplyTex = 0.0;
uniform float _OutlineHighlightEmission = 0.0;
uniform float _OutlineHighlightBorder = 0.85;
uniform float _OutlineHighlightBlur = 0.1;
uniform float _OutlineHighlightReceiveShadow = 0.0;

uniform float _OutlineHue = 0.0;
uniform float _OutlineSaturation = 0.0;
uniform float _OutlineValue = 1.0;
uniform float _OutlineBrightness = 0.0;
uniform float _OutlineGamma = 1.0;
uniform float _OutlineHueShift = 0.0;
uniform float _OutlineHueShiftColorSpace = 0.0;
uniform float _OutlineSaturationMethod = 1.0;
uniform float _OutlineHueOffset;
uniform float _OutlineHueOffsetSpeed = 0.0;
uniform float _PoiUTSStyleOutlineBlend = 0.0;
uniform float _OutlineAlphaDistanceFade = 0.0;
uniform float _OutlineAlphaDistanceFadeType = 1.0;
uniform float _OutlineAlphaDistanceFadeMinAlpha = 0.0;
uniform float _OutlineAlphaDistanceFadeMaxAlpha = 1.0;
uniform float _OutlineAlphaDistanceFadeMin = 0.0;
uniform float _OutlineAlphaDistanceFadeMax = 0.0;
uniform float _OutlineShadowStrength = 0.0;
uniform float _LineColorThemeIndex = 0.0;
uniform float _Offset_Z = 0.0;
uniform float _OutlineClipAtZeroWidth = 1.0;
#endif

uniform vec4 _GlobalThemeColor0 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _GlobalThemeColor1 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _GlobalThemeColor2 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _GlobalThemeColor3 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _GlobalThemeHueShiftColorSpace0 = 0.0;
uniform float _GlobalThemeHueShiftColorSpace1 = 0.0;
uniform float _GlobalThemeHueShiftColorSpace2 = 0.0;
uniform float _GlobalThemeHueShiftColorSpace3 = 0.0;
uniform float _GlobalThemeHue0 = 0.0;
uniform float _GlobalThemeHue1 = 0.0;
uniform float _GlobalThemeHue2 = 0.0;
uniform float _GlobalThemeHue3 = 0.0;
uniform float _GlobalThemeHueSpeed0 = 0.0;
uniform float _GlobalThemeHueSpeed1 = 0.0;
uniform float _GlobalThemeHueSpeed2 = 0.0;
uniform float _GlobalThemeHueSpeed3 = 0.0;
uniform float _GlobalThemeSaturationMethod0 = 1.0;
uniform float _GlobalThemeSaturationMethod1 = 1.0;
uniform float _GlobalThemeSaturationMethod2 = 1.0;
uniform float _GlobalThemeSaturationMethod3 = 1.0;
uniform float _GlobalThemeSaturation0 = 0.0;
uniform float _GlobalThemeSaturation1 = 0.0;
uniform float _GlobalThemeSaturation2 = 0.0;
uniform float _GlobalThemeSaturation3 = 0.0;
uniform float _GlobalThemeValue0 = 0.0;
uniform float _GlobalThemeValue1 = 0.0;
uniform float _GlobalThemeValue2 = 0.0;
uniform float _GlobalThemeValue3 = 0.0;
uniform float _GlobalThemeBrightness0 = 0.0;
uniform float _GlobalThemeBrightness1 = 0.0;
uniform float _GlobalThemeBrightness2 = 0.0;
uniform float _GlobalThemeBrightness3 = 0.0;

#if defined(POI_GLOBALMASK_TEXTURES)
uniform vec4 _GlobalMaskTexture0_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GlobalMaskTexture0Pan = vec2(0.0, 0.0);
uniform float _GlobalMaskTexture0UV = 0.0;
uniform int _GlobalMaskTexture0Split = 0;
uniform vec4 _GlobalMaskTexture0SplitTilingOffset_G = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture0SplitPan_G = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture0SplitTilingOffset_B = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture0SplitPan_B = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture0SplitTilingOffset_A = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture0SplitPan_A = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GlobalMaskTexture1Pan = vec2(0.0, 0.0);
uniform float _GlobalMaskTexture1UV = 0.0;
uniform int _GlobalMaskTexture1Split = 0;
uniform vec4 _GlobalMaskTexture1SplitTilingOffset_G = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture1SplitPan_G = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture1SplitTilingOffset_B = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture1SplitPan_B = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture1SplitTilingOffset_A = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture1SplitPan_A = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture2_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GlobalMaskTexture2Pan = vec2(0.0, 0.0);
uniform float _GlobalMaskTexture2UV = 0.0;
uniform int _GlobalMaskTexture2Split = 0;
uniform vec4 _GlobalMaskTexture2SplitTilingOffset_G = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture2SplitPan_G = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture2SplitTilingOffset_B = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture2SplitPan_B = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture2SplitTilingOffset_A = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture2SplitPan_A = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture3_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GlobalMaskTexture3Pan = vec2(0.0, 0.0);
uniform float _GlobalMaskTexture3UV = 0.0;
uniform int _GlobalMaskTexture3Split = 0;
uniform vec4 _GlobalMaskTexture3SplitTilingOffset_G = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture3SplitPan_G = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture3SplitTilingOffset_B = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture3SplitPan_B = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture3SplitTilingOffset_A = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _GlobalMaskTexture3SplitPan_A = vec4(0.0, 0.0, 0.0, 0.0);
#endif
uniform float _GlobalMaskOptionsEnable = 0.0;
uniform int _GlobalMaskOptionsType = 0;

uniform float _GlobalMaskSlider_0 = 0.0;
uniform float _GlobalMaskSlider_1 = 0.0;
uniform float _GlobalMaskSlider_2 = 0.0;
uniform float _GlobalMaskSlider_3 = 0.0;
uniform float _GlobalMaskSlider_4 = 0.0;
uniform float _GlobalMaskSlider_5 = 0.0;
uniform float _GlobalMaskSlider_6 = 0.0;
uniform float _GlobalMaskSlider_7 = 0.0;
uniform float _GlobalMaskSlider_8 = 0.0;
uniform float _GlobalMaskSlider_9 = 0.0;
uniform float _GlobalMaskSlider_10 = 0.0;
uniform float _GlobalMaskSlider_11 = 0.0;
uniform float _GlobalMaskSlider_12 = 0.0;
uniform float _GlobalMaskSlider_13 = 0.0;
uniform float _GlobalMaskSlider_14 = 0.0;
uniform float _GlobalMaskSlider_15 = 0.0;

uniform vec2 _GlobalMaskMinMaxSlider_0 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_1 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_2 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_3 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_4 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_5 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_6 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_7 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_8 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_9 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_10 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_11 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_12 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_13 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_14 = vec2(0.0, 1.0);
uniform vec2 _GlobalMaskMinMaxSlider_15 = vec2(0.0, 1.0);

uniform int _GlobalMaskToggleOn_0 = 0;
uniform int _GlobalMaskToggleOff_0 = 0;
uniform int _GlobalMaskToggleOn_1 = 0;
uniform int _GlobalMaskToggleOff_1 = 0;
uniform int _GlobalMaskToggleOn_2 = 0;
uniform int _GlobalMaskToggleOff_2 = 0;
uniform int _GlobalMaskToggleOn_3 = 0;
uniform int _GlobalMaskToggleOff_3 = 0;
uniform int _GlobalMaskToggleOn_4 = 0;
uniform int _GlobalMaskToggleOff_4 = 0;
uniform int _GlobalMaskToggleOn_5 = 0;
uniform int _GlobalMaskToggleOff_5 = 0;
uniform int _GlobalMaskToggleOn_6 = 0;
uniform int _GlobalMaskToggleOff_6 = 0;
uniform int _GlobalMaskToggleOn_7 = 0;
uniform int _GlobalMaskToggleOff_7 = 0;
uniform int _GlobalMaskToggleOn_8 = 0;
uniform int _GlobalMaskToggleOff_8 = 0;
uniform int _GlobalMaskToggleOn_9 = 0;
uniform int _GlobalMaskToggleOff_9 = 0;
uniform int _GlobalMaskToggleOn_10 = 0;
uniform int _GlobalMaskToggleOff_10 = 0;
uniform int _GlobalMaskToggleOn_11 = 0;
uniform int _GlobalMaskToggleOff_11 = 0;
uniform int _GlobalMaskToggleOn_12 = 0;
uniform int _GlobalMaskToggleOff_12 = 0;
uniform int _GlobalMaskToggleOn_13 = 0;
uniform int _GlobalMaskToggleOff_13 = 0;
uniform int _GlobalMaskToggleOn_14 = 0;
uniform int _GlobalMaskToggleOff_14 = 0;
uniform int _GlobalMaskToggleOn_15 = 0;
uniform int _GlobalMaskToggleOff_15 = 0;
uniform float _GlobalMaskModifiersBackfaceEnable = 0.0;
uniform float _GlobalMaskBackface_0 = 0.0;
uniform float _GlobalMaskBackface_1 = 0.0;
uniform float _GlobalMaskBackface_2 = 0.0;
uniform float _GlobalMaskBackface_3 = 0.0;
uniform float _GlobalMaskBackface_4 = 0.0;
uniform float _GlobalMaskBackface_5 = 0.0;
uniform float _GlobalMaskBackface_6 = 0.0;
uniform float _GlobalMaskBackface_7 = 0.0;
uniform float _GlobalMaskBackface_8 = 0.0;
uniform float _GlobalMaskBackface_9 = 0.0;
uniform float _GlobalMaskBackface_10 = 0.0;
uniform float _GlobalMaskBackface_11 = 0.0;
uniform float _GlobalMaskBackface_12 = 0.0;
uniform float _GlobalMaskBackface_13 = 0.0;
uniform float _GlobalMaskBackface_14 = 0.0;
uniform float _GlobalMaskBackface_15 = 0.0;

uniform float _GlobalMaskModifiersMirrorEnable = 0.0;
uniform float _GlobalMaskMirrorVisibilityMode = 1.0;
uniform float _GlobalMaskMirror_0 = 0.0;
uniform float _GlobalMaskMirror_1 = 0.0;
uniform float _GlobalMaskMirror_2 = 0.0;
uniform float _GlobalMaskMirror_3 = 0.0;
uniform float _GlobalMaskMirror_4 = 0.0;
uniform float _GlobalMaskMirror_5 = 0.0;
uniform float _GlobalMaskMirror_6 = 0.0;
uniform float _GlobalMaskMirror_7 = 0.0;
uniform float _GlobalMaskMirror_8 = 0.0;
uniform float _GlobalMaskMirror_9 = 0.0;
uniform float _GlobalMaskMirror_10 = 0.0;
uniform float _GlobalMaskMirror_11 = 0.0;
uniform float _GlobalMaskMirror_12 = 0.0;
uniform float _GlobalMaskMirror_13 = 0.0;
uniform float _GlobalMaskMirror_14 = 0.0;
uniform float _GlobalMaskMirror_15 = 0.0;

uniform float _GlobalMaskModifiersCameraEnable = 0.0;
uniform float _GlobalMaskCamera_0 = 0.0;
uniform float _GlobalMaskCamera_1 = 0.0;
uniform float _GlobalMaskCamera_2 = 0.0;
uniform float _GlobalMaskCamera_3 = 0.0;
uniform float _GlobalMaskCamera_4 = 0.0;
uniform float _GlobalMaskCamera_5 = 0.0;
uniform float _GlobalMaskCamera_6 = 0.0;
uniform float _GlobalMaskCamera_7 = 0.0;
uniform float _GlobalMaskCamera_8 = 0.0;
uniform float _GlobalMaskCamera_9 = 0.0;
uniform float _GlobalMaskCamera_10 = 0.0;
uniform float _GlobalMaskCamera_11 = 0.0;
uniform float _GlobalMaskCamera_12 = 0.0;
uniform float _GlobalMaskCamera_13 = 0.0;
uniform float _GlobalMaskCamera_14 = 0.0;
uniform float _GlobalMaskCamera_15 = 0.0;

uniform int _GlobalMaskModifiersDistanceEnable = 0;

uniform int _GlobalMaskDistanceEnable_0 = 0;
uniform int _GlobalMaskDistanceType_0 = 1;
uniform float _GlobalMaskDistanceMin_0 = 1.0;
uniform float _GlobalMaskDistanceMax_0 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_0 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_0 = 1.0;
uniform int _GlobalMaskDistanceBlendType_0 = 0;

uniform int _GlobalMaskDistanceEnable_1 = 0;
uniform int _GlobalMaskDistanceType_1 = 1;
uniform float _GlobalMaskDistanceMin_1 = 1.0;
uniform float _GlobalMaskDistanceMax_1 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_1 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_1 = 1.0;
uniform int _GlobalMaskDistanceBlendType_1 = 0;

uniform int _GlobalMaskDistanceEnable_2 = 0;
uniform int _GlobalMaskDistanceType_2 = 1;
uniform float _GlobalMaskDistanceMin_2 = 1.0;
uniform float _GlobalMaskDistanceMax_2 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_2 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_2 = 1.0;
uniform int _GlobalMaskDistanceBlendType_2 = 0;

uniform int _GlobalMaskDistanceEnable_3 = 0;
uniform int _GlobalMaskDistanceType_3 = 1;
uniform float _GlobalMaskDistanceMin_3 = 1.0;
uniform float _GlobalMaskDistanceMax_3 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_3 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_3 = 1.0;
uniform int _GlobalMaskDistanceBlendType_3 = 0;

uniform int _GlobalMaskDistanceEnable_4 = 0;
uniform int _GlobalMaskDistanceType_4 = 1;
uniform float _GlobalMaskDistanceMin_4 = 1.0;
uniform float _GlobalMaskDistanceMax_4 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_4 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_4 = 1.0;
uniform int _GlobalMaskDistanceBlendType_4 = 0;

uniform int _GlobalMaskDistanceEnable_5 = 0;
uniform int _GlobalMaskDistanceType_5 = 1;
uniform float _GlobalMaskDistanceMin_5 = 1.0;
uniform float _GlobalMaskDistanceMax_5 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_5 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_5 = 1.0;
uniform int _GlobalMaskDistanceBlendType_5 = 0;

uniform int _GlobalMaskDistanceEnable_6 = 0;
uniform int _GlobalMaskDistanceType_6 = 1;
uniform float _GlobalMaskDistanceMin_6 = 1.0;
uniform float _GlobalMaskDistanceMax_6 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_6 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_6 = 1.0;
uniform int _GlobalMaskDistanceBlendType_6 = 0;

uniform int _GlobalMaskDistanceEnable_7 = 0;
uniform int _GlobalMaskDistanceType_7 = 1;
uniform float _GlobalMaskDistanceMin_7 = 1.0;
uniform float _GlobalMaskDistanceMax_7 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_7 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_7 = 1.0;
uniform int _GlobalMaskDistanceBlendType_7 = 0;

uniform int _GlobalMaskDistanceEnable_8 = 0;
uniform int _GlobalMaskDistanceType_8 = 1;
uniform float _GlobalMaskDistanceMin_8 = 1.0;
uniform float _GlobalMaskDistanceMax_8 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_8 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_8 = 1.0;
uniform int _GlobalMaskDistanceBlendType_8 = 0;

uniform int _GlobalMaskDistanceEnable_9 = 0;
uniform int _GlobalMaskDistanceType_9 = 1;
uniform float _GlobalMaskDistanceMin_9 = 1.0;
uniform float _GlobalMaskDistanceMax_9 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_9 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_9 = 1.0;
uniform int _GlobalMaskDistanceBlendType_9 = 0;

uniform int _GlobalMaskDistanceEnable_10 = 0;
uniform int _GlobalMaskDistanceType_10 = 1;
uniform float _GlobalMaskDistanceMin_10 = 1.0;
uniform float _GlobalMaskDistanceMax_10 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_10 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_10 = 1.0;
uniform int _GlobalMaskDistanceBlendType_10 = 0;

uniform int _GlobalMaskDistanceEnable_11 = 0;
uniform int _GlobalMaskDistanceType_11 = 1;
uniform float _GlobalMaskDistanceMin_11 = 1.0;
uniform float _GlobalMaskDistanceMax_11 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_11 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_11 = 1.0;
uniform int _GlobalMaskDistanceBlendType_11 = 0;

uniform int _GlobalMaskDistanceEnable_12 = 0;
uniform int _GlobalMaskDistanceType_12 = 1;
uniform float _GlobalMaskDistanceMin_12 = 1.0;
uniform float _GlobalMaskDistanceMax_12 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_12 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_12 = 1.0;
uniform int _GlobalMaskDistanceBlendType_12 = 0;

uniform int _GlobalMaskDistanceEnable_13 = 0;
uniform int _GlobalMaskDistanceType_13 = 1;
uniform float _GlobalMaskDistanceMin_13 = 1.0;
uniform float _GlobalMaskDistanceMax_13 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_13 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_13 = 1.0;
uniform int _GlobalMaskDistanceBlendType_13 = 0;

uniform int _GlobalMaskDistanceEnable_14 = 0;
uniform int _GlobalMaskDistanceType_14 = 1;
uniform float _GlobalMaskDistanceMin_14 = 1.0;
uniform float _GlobalMaskDistanceMax_14 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_14 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_14 = 1.0;
uniform int _GlobalMaskDistanceBlendType_14 = 0;

uniform int _GlobalMaskDistanceEnable_15 = 0;
uniform int _GlobalMaskDistanceType_15 = 1;
uniform float _GlobalMaskDistanceMin_15 = 1.0;
uniform float _GlobalMaskDistanceMax_15 = 2.0;
uniform float _GlobalMaskDistanceMinAlpha_15 = 0.0;
uniform float _GlobalMaskDistanceMaxAlpha_15 = 1.0;
uniform int _GlobalMaskDistanceBlendType_15 = 0;

uniform int _GlobalMaskVertexColorLinearSpace = 1;
uniform int _GlobalMaskVertexColorRed = 0;
uniform int _GlobalMaskVertexColorRedBlendType = 2;
uniform int _GlobalMaskVertexColorGreen = 0;
uniform int _GlobalMaskVertexColorGreenBlendType = 2;
uniform int _GlobalMaskVertexColorBlue = 0;
uniform int _GlobalMaskVertexColorBlueBlendType = 2;
uniform int _GlobalMaskVertexColorAlpha = 0;
uniform int _GlobalMaskVertexColorAlphaBlendType = 2;

#if defined(POI_DISTORTION)
uniform vec4 _DistortionFlowTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DistortionFlowTexturePan = vec2(0.0, 2.0);
uniform float _DistortionFlowTextureUV = 0.0;

uniform vec4 _DistortionFlowTexture1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DistortionFlowTexture1Pan = vec2(2.0, 0.0);
uniform float _DistortionFlowTexture1UV = 0.0;

uniform vec4 _DistortionMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DistortionMaskPan = vec2(0.0, 0.0);
uniform float _DistortionMaskUV = 0.0;
uniform float _DistortionMaskChannel = 0.0;

uniform float _DistortionUvToDistort = 0.0;
uniform float _DistortionMode = 0.0;
uniform float _DistortionStrength = 0.05;
uniform float _DistortionStrength1 = 0.02;
uniform vec2 _DistortionTiling = vec2(1.0, 1.0);
uniform vec2 _DistortionPanSpeed = vec2(0.0, 0.0);
uniform float _DistortionGerstnerSpeed = 1.0;
uniform float _DistortionGerstnerScale = 1.0;
uniform float _DistortionGerstnerStrength = 0.15;
uniform float _DistortionGerstnerSteepness = 0.5;
uniform vec4 _DistortionGerstnerWave1 = vec4(0.0, 0.2, 10.0, 1.0);
uniform vec4 _DistortionGerstnerWave2 = vec4(45.0, 0.15, 6.0, 1.0);
uniform vec4 _DistortionGerstnerWave3 = vec4(120.0, 0.1, 4.0, 1.0);
uniform vec4 _DistortionGerstnerWave4 = vec4(200.0, 0.08, 2.5, 1.0);
uniform float _DistortionSimplexScale = 1.0;
uniform float _DistortionSimplexStrength = 0.01;
uniform float _DistortionSimplexSpeed = 1.0;
uniform float _DistortionVoronoiScale = 1.0;
uniform float _DistortionVoronoiStrength = 0.1;
uniform float _DistortionVoronoiSpeed = 2.0;
uniform float _DistortionVoronoiEdgeBlend = 1.0;
uniform float _DistortionCurlScale = 0.3;
uniform float _DistortionCurlStrength = 0.01;
uniform float _DistortionCurlSpeed = 0.2;
uniform float _DistortionFBMScale = 0.2;
uniform float _DistortionFBMStrength = 0.025;
uniform float _DistortionFBMSpeed = 1.0;
uniform float _DistortionFBMOctaves = 4.0;
uniform float _DistortionFBMLacunarity = 2.0;
uniform float _DistortionFBMGain = 0.5;
#endif
uniform vec2 _UVSettingsTiling0 = vec2(1.0, 1.0);
uniform vec2 _UVSettingsOffset0 = vec2(0.0, 0.0);
uniform float _UVSettingsAngle0 = 0.0;
uniform vec2 _UVSettingsPan0 = vec2(0.0, 0.0);
uniform float _UVSettingsRotate0 = 0.0;
uniform vec2 _UVSettingsTiling1 = vec2(1.0, 1.0);
uniform vec2 _UVSettingsOffset1 = vec2(0.0, 0.0);
uniform float _UVSettingsAngle1 = 0.0;
uniform vec2 _UVSettingsPan1 = vec2(0.0, 0.0);
uniform float _UVSettingsRotate1 = 0.0;
uniform vec2 _UVSettingsTiling2 = vec2(1.0, 1.0);
uniform vec2 _UVSettingsOffset2 = vec2(0.0, 0.0);
uniform float _UVSettingsAngle2 = 0.0;
uniform vec2 _UVSettingsPan2 = vec2(0.0, 0.0);
uniform float _UVSettingsRotate2 = 0.0;
uniform vec2 _UVSettingsTiling3 = vec2(1.0, 1.0);
uniform vec2 _UVSettingsOffset3 = vec2(0.0, 0.0);
uniform float _UVSettingsAngle3 = 0.0;
uniform vec2 _UVSettingsPan3 = vec2(0.0, 0.0);
uniform float _UVSettingsRotate3 = 0.0;
uniform float _UVSettingsShiftBackfaceUV = 0.0;

uniform int _StereoEnabled = 0;
uniform int _PanoUseBothEyes = 1;
uniform float _PolarUV = 0.0;
uniform float _PolarMode = 0.0;
uniform vec2 _PolarCenter = vec2(0.5, 0.5);
uniform float _PolarRadialScale = 1.0;
uniform float _PolarLengthScale = 1.0;
uniform float _PolarSpiralPower = 0.0;
uniform float _UVModWorldPos0 = 0.0;
uniform float _UVModWorldPos1 = 2.0;
uniform float _UVModLocalPos0 = 0.0;
uniform float _UVModLocalPos1 = 1.0;

#if defined(POI_UDIMDISCARD)
uniform float _UDIMDiscardMode = 0.0;
uniform float _UDIMDiscardUV = 0.0;
uniform float _UDIMDiscardRow3_0 = 0.0;
uniform float _UDIMDiscardRow3_1 = 0.0;
uniform float _UDIMDiscardRow3_2 = 0.0;
uniform float _UDIMDiscardRow3_3 = 0.0;
uniform float _UDIMDiscardRow2_0 = 0.0;
uniform float _UDIMDiscardRow2_1 = 0.0;
uniform float _UDIMDiscardRow2_2 = 0.0;
uniform float _UDIMDiscardRow2_3 = 0.0;
uniform float _UDIMDiscardRow1_0 = 0.0;
uniform float _UDIMDiscardRow1_1 = 0.0;
uniform float _UDIMDiscardRow1_2 = 0.0;
uniform float _UDIMDiscardRow1_3 = 0.0;
uniform float _UDIMDiscardRow0_0 = 0.0;
uniform float _UDIMDiscardRow0_1 = 0.0;
uniform float _UDIMDiscardRow0_2 = 0.0;
uniform float _UDIMDiscardRow0_3 = 0.0;

uniform float _EnableUDIMFaceDiscardOptions = 0.0;
uniform float _UDIMFaceDiscardFace = 0.0;
uniform float _UDIMFaceDiscardUV = 0.0;
uniform float _UDIMFaceDiscardRow3_0 = 0.0;
uniform float _UDIMFaceDiscardRow3_1 = 0.0;
uniform float _UDIMFaceDiscardRow3_2 = 0.0;
uniform float _UDIMFaceDiscardRow3_3 = 0.0;
uniform float _UDIMFaceDiscardRow2_0 = 0.0;
uniform float _UDIMFaceDiscardRow2_1 = 0.0;
uniform float _UDIMFaceDiscardRow2_2 = 0.0;
uniform float _UDIMFaceDiscardRow2_3 = 0.0;
uniform float _UDIMFaceDiscardRow1_0 = 0.0;
uniform float _UDIMFaceDiscardRow1_1 = 0.0;
uniform float _UDIMFaceDiscardRow1_2 = 0.0;
uniform float _UDIMFaceDiscardRow1_3 = 0.0;
uniform float _UDIMFaceDiscardRow0_0 = 0.0;
uniform float _UDIMFaceDiscardRow0_1 = 0.0;
uniform float _UDIMFaceDiscardRow0_2 = 0.0;
uniform float _UDIMFaceDiscardRow0_3 = 0.0;
#endif

#if defined(POI_PARALLAX) && defined(PROP_HEIGHTMAP)
uniform vec4 _HeightMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _HeightMapPan = vec2(0.0, 0.0);
uniform float _HeightMapUV = 0.0;

uniform vec4 _Heightmask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _HeightmaskPan = vec2(0.0, 0.0);
uniform float _HeightmaskUV = 0.0;
uniform float _HeightmaskChannel = 0.0;
uniform float _HeightmaskInvert = 0.0;
uniform float _HeightmaskGlobalMask = 0.0;
uniform float _HeightmaskGlobalMaskBlendType = 2.0;

uniform float _ParallaxUV = 0.0;
uniform float _HeightStrength = 0.005;
uniform float _HeightOffset = 0.0;
uniform float _HeightStepsMin = 10.0;
uniform float _HeightStepsMax = 128.0;

uniform float _CurvatureU = 0.0;
uniform float _CurvatureV = 0.0;
uniform float _CurvFix = 1.0;

uniform float _ParallaxConeRatio = 1.0;
uniform float _ParallaxRelaxedCone = 1.0;
uniform float _ParallaxBinarySteps = 8.0;
uniform float _ParallaxMipBias = 0.0;
uniform float _ParallaxAnisotropic = 1.0;
uniform float _ParallaxAnisotropyScale = 1.0;
uniform vec4 _ParallaxLOD = vec4(5.0, 15.0, 0.0, 0.0);
#endif

#if defined(POI_BLACKLIGHTMASKING)
uniform float _BlackLightMasking0Key = 1.0;
uniform vec2 _BlackLightMasking0Range = vec2(0.1, 0.5);
uniform float _BlackLightMasking0GlobalMaskIndex = 0.0;
uniform float _BlackLightMasking0GlobalMaskBlendType = 0.0;

uniform float _BlackLightMasking1Key = 2.0;
uniform vec2 _BlackLightMasking1Range = vec2(0.1, 0.5);
uniform float _BlackLightMasking1GlobalMaskIndex = 0.0;
uniform float _BlackLightMasking1GlobalMaskBlendType = 0.0;

uniform float _BlackLightMasking2Key = 3.0;
uniform vec2 _BlackLightMasking2Range = vec2(0.1, 0.5);
uniform float _BlackLightMasking2GlobalMaskIndex = 0.0;
uniform float _BlackLightMasking2GlobalMaskBlendType = 0.0;

uniform float _BlackLightMasking3Key = 4.0;
uniform vec2 _BlackLightMasking3Range = vec2(0.1, 0.5);
uniform float _BlackLightMasking3GlobalMaskIndex = 0.0;
uniform float _BlackLightMasking3GlobalMaskBlendType = 0.0;
#endif

#if defined(POI_DETAIL)
uniform vec4 _DetailMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DetailMaskPan = vec2(0.0, 0.0);
uniform float _DetailMaskUV = 0.0;
uniform float _DetailMaskStochastic = 0.0;
uniform vec4 _DetailNormalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DetailNormalMapPan = vec2(0.0, 0.0);
uniform float _DetailNormalMapUV = 0.0;
uniform float _DetailNormalMapScale = 1.0;
uniform float _DetailNormalMapStochastic = 0.0;
uniform float _DetailNormalGlobalMask = 0.0;
uniform float _DetailNormalGlobalMaskBlendType = 2.0;
uniform vec4 _DetailTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DetailTexPan = vec2(0.0, 0.0);
uniform float _DetailTexUV = 0.0;
uniform float _DetailTexStochastic = 0.0;
uniform vec3 _DetailTint : source_color = vec3(1.0, 1.0, 1.0);
uniform float _DetailTintThemeIndex = 0.0;
uniform float _DetailTexIntensity = 1.0;
uniform float _DetailBrightness = 1.0;
uniform float _DetailTexGlobalMask = 0.0;
uniform float _DetailTexGlobalMaskBlendType = 2.0;
#endif

#if defined(POI_VERTEX_MANIPULATION)

uniform vec4 _VertexBasicsMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _VertexBasicsMaskUVPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform float VertexBasicsMaskUV = 0.0;
uniform float _VertexBasicsMaskChannel = 0.0;
uniform vec4 _VertexManipulationLocalTranslation = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 _VertexManipulationWorldTranslation = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 _VertexManipulationLocalRotation = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec3 _VertexManipulationLocalRotationSpeed = vec3(0.0, 0.0, 0.0);
uniform vec4 _VertexManipulationLocalScale = vec4(1.0, 1.0, 1.0, 1.0);

uniform vec4 _VertexManipulationHeightMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _VertexManipulationHeightMaskPan = vec2(0.0, 0.0);
uniform float _VertexManipulationHeightMaskUV = 0.0;
uniform float _VertexManipulationHeightMapChannel = 0.0;
uniform float _VertexManipulationHeightMaskChannel = 0.0;
uniform float _VertexManipulationHeight = 0.0;
uniform float _VertexManipulationHeightBias = 0.0;

uniform float _VertexRoundingEnabled = 0.0;
uniform int _VertexRoundingSpace = 0;
uniform float _VertexRoundingDivision = 0.02;
uniform float _VertexRoundingMaskChannel = 0.0;

uniform float _VertexBarrelMode = 0.0;
uniform float _VertexBarrelWidth = 0.2;
uniform float _VertexBarrelAlpha = 0.0;
uniform float _VertexBarrelHeight = 0.0;
uniform float _VertexBarrelMaskChannel = 0.0;
uniform float _VertexSphereMode = 0.0;
uniform float _VertexSphereRadius = 1.0;
uniform float _VertexSphereHeight = 1.0;
uniform float _VertexSphereAlpha = 0.0;
uniform vec4 _VertexSphereCenter = vec4(0.0, 0.0, 0.0, 1.0);
uniform float _VertexSphereMaskChannel = 0.0;
uniform float _VertexTornadoMode = 0.0;
uniform float _VertexTornadoRadius = 1.0;
uniform float _VertexTornadoVerticalPull = 0.0;
uniform float _VertexTornadoFunnelShape = 1.0;
uniform float _VertexTornadoSpeed = 5.0;
uniform float _VertexTornadoIntensity = 100.0;
uniform float _VertexTornadoBaseHeight = 0.0;
uniform float _VertexTornadoTopHeight = 1.0;
uniform float _VertexTornadoMaskChannel = 0.0;

uniform float _VertexWindEnabled = 0.0;
uniform float _VertexWindMaskChannel = 0.0;
uniform vec4 _VertexWindPrimaryDirection = vec4(1.0, 0.0, 0.0, 0.0);
uniform float _VertexWindPrimaryAmplitude = 0.1;
uniform float _VertexWindPrimarySpeed = 1.0;
uniform float _VertexWindPrimaryFrequency = 1.0;
uniform vec4 _VertexWindDetailDirection = vec4(0.0, 1.0, 0.0, 0.0);
uniform float _VertexWindDetailAmplitude = 0.05;
uniform float _VertexWindDetailSpeed = 2.5;
uniform float _VertexWindDetailFrequency = 5.0;
uniform float _VertexWindNoiseChannel = 0.0;
uniform float _VertexWindNoiseStrength = 0.5;
uniform float _VertexWindNoiseScale = 10.0;
uniform float _VertexWindNoiseSpeed = 0.5;

uniform vec3 _VertexLocalTranslationALMin = vec3(0.0, 0.0, 0.0);
uniform vec3 _VertexLocalTranslationALMax = vec3(0.0, 0.0, 0.0);
uniform float _VertexLocalTranslationALBand = 0.0;
uniform vec3 _VertexLocalRotationAL = vec3(0.0, 0.0, 0.0);
uniform float _VertexLocalRotationALBand;
uniform vec3 _VertexLocalRotationCTALSpeed = vec3(0.0, 0.0, 0.0);
uniform float _VertexLocalRotationCTALBandX = 0.0;
uniform float _VertexLocalRotationCTALTypeX = 0.0;
uniform float _VertexLocalRotationCTALBandY = 0.0;
uniform float _VertexLocalRotationCTALTypeY = 0.0;
uniform float _VertexLocalRotationCTALBandZ = 0.0;
uniform float _VertexLocalRotationCTALTypeZ = 0.0;
uniform vec4 _VertexLocalScaleALMin = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _VertexLocalScaleALMax = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _VertexLocalScaleALBand = 0.0;
uniform vec3 _VertexWorldTranslationALMin = vec3(0.0, 0.0, 0.0);
uniform vec3 _VertexWorldTranslationALMax = vec3(0.0, 0.0, 0.0);
uniform float _VertexWorldTranslationALBand = 0.0;
uniform vec2 _VertexManipulationHeightAL = vec2(0.0, 0.0);
uniform float _VertexManipulationHeightBand = 0.0;
uniform vec2 _VertexRoundingRangeAL = vec2(0.0, 0.0);
uniform float _VertexRoundingRangeBand = 0.0;
uniform float _VertexSpectrumMotion = 0.0;
uniform vec3 _VertexSpectrumOffsetMin = vec3(0.0, 0.0, 0.0);
uniform vec3 _VertexSpectrumOffsetMax = vec3(0.0, 0.1, 0.0);
uniform float _VertexSpectrumUV = 0.0;
uniform float _VertexSpectrumUVDirection = 0.0;
#endif

#if defined(POI_VERTEX_LOOKAT)

uniform vec4 _LookAtMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _LookAtMaskPan = vec2(0.0, 0.0);
uniform float _LookAtMaskUV = 0.0;

uniform vec4 _VertexLookAtAlphaAudiolink = vec4(0.0, 0.0, 0.0, 0.0);

uniform float _LookAtVisibilityEnabled = 0.0;
uniform float _LookAtVisibilityMode = 1.0;
uniform float _LookAtVisibilityMirror = 0.0;
uniform float _LookAtVisibilityVRCRegular = 1.0;
uniform float _LookAtVisibilityVRCMirrorVR = 1.0;
uniform float _LookAtVisibilityVRCMirrorDesktop = 1.0;
uniform float _LookAtVisibilityVRCCameraVR = 1.0;
uniform float _LookAtVisibilityVRCCameraDesktop = 1.0;
uniform float _LookAtVisibilityVRCCameraScreenshot = 1.0;

uniform float _LookAtRedMaskEnabled = 1.0;
uniform float _LookAtRedAlpha = 1.0;
uniform float _LookAtRedTargetUp = 0.0;
uniform vec4 _LookAtRedUpDirection = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 _LookAtRedForwardDirection = vec4(0.0, 0.0, 1.0, 0.0);
uniform vec4 _LookAtRedPivotOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtRedOriginOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtRedFoV = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtRedDistanceFalloff = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtRedMaxPitchAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtRedMaxYawAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtRedMaxRollAngle = vec4(-180.0, 180.0, -180.0, 180.0);

uniform float _LookAtGreenMaskEnabled = 0.0;
uniform float _LookAtGreenAlpha = 1.0;
uniform float _LookAtGreenTargetUp = 0.0;
uniform vec4 _LookAtGreenForwardDirection = vec4(0.0, 0.0, 1.0, 0.0);
uniform vec4 _LookAtGreenUpDirection = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 _LookAtGreenPivotOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtGreenOriginOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtGreenFoV = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtGreenDistanceFalloff = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtGreenMaxPitchAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtGreenMaxYawAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtGreenMaxRollAngle = vec4(-180.0, 180.0, -180.0, 180.0);

uniform float _LookAtBlueMaskEnabled = 0.0;
uniform float _LookAtBlueAlpha = 1.0;
uniform float _LookAtBlueTargetUp = 0.0;
uniform vec4 _LookAtBlueUpDirection = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 _LookAtBlueForwardDirection = vec4(0.0, 0.0, 1.0, 0.0);
uniform vec4 _LookAtBluePivotOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtBlueOriginOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtBlueFoV = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtBlueDistanceFalloff = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtBlueMaxPitchAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtBlueMaxYawAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtBlueMaxRollAngle = vec4(-180.0, 180.0, -180.0, 180.0);

uniform float _LookAtAlphaMaskEnabled = 0.0;
uniform float _LookAtAlphaAlpha = 1.0;
uniform float _LookAtAlphaTargetUp = 0.0;
uniform vec4 _LookAtAlphaUpDirection = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 _LookAtAlphaForwardDirection = vec4(0.0, 0.0, 1.0, 0.0);
uniform vec4 _LookAtAlphaPivotOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtAlphaOriginOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtAlphaFoV = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtAlphaDistanceFalloff = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _LookAtAlphaMaxPitchAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtAlphaMaxYawAngle = vec4(-180.0, 180.0, -180.0, 180.0);
uniform vec4 _LookAtAlphaMaxRollAngle = vec4(-180.0, 180.0, -180.0, 180.0);
#endif

#if defined(POI_VERTEX_GLITCHING)
uniform vec4 _VertexGlitchMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _VertexGlitchingMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _VertexGlitchingMaskUV = 0.0;
uniform float _VertexGlitchUseMap = 0.0;
uniform float _VertexGlitchThreshold = 1.0;
uniform float _VertexGlitchFrequency = 1.0;
uniform float _VertexGlitchStrength = 1.0;
uniform float _VertexGlitchDensity = 10.0;

uniform float _VertexGlitchMirrorEnable = 0.0;
uniform float _VertexGlitchMirror = 0.0;

uniform float _VertexGlitchMapPanSpeed = 10.0;
uniform float _VertexGlitchingAudiolinkOverride = 1.0;
#endif

#if defined(POI_SKIN)
uniform float _PoiSkinMelanin = 0.2;
uniform float _PoiSkinMelaninRedness = 0.5;
uniform float _PoiSkinHemoglobin = 0.5;
uniform float _PoiSkinBrightness = 1.0;
uniform vec4 _PoiSkinColorTint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _PoiSkinColorTintThemeIndex = 0.0;
uniform float _PoiSkinBlendMode = 0.0;
uniform float _PoiSkinBlend = 1.0;
uniform float _PoiSkinFrecklesEnabled = 0.0;
uniform float _PoiSkinFreckleMapUV = 0.0;
uniform vec4 _PoiSkinFreckleMapTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _PoiSkinFreckleStrength = 0.5;
uniform float _PoiSkinFreckleSharpness = 3.0;
uniform float _PoiSkinFreckleRedness = 0.1;
uniform float _PoiSkinTanEnabled = 0.0;
uniform float _PoiSkinTanMapUV = 0.0;
uniform vec4 _PoiSkinTanMapTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _PoiSkinTanAmount = 0.15;
uniform float _PoiSkinTanRednessShift = -0.1;
#endif

#if defined(POI_DEPTHBULGE)
uniform float _DepthBulgeFadeLength = 0.02;
uniform float _DepthBulgeHeight = 0.02;
uniform float _DepthBulgeMaskUV = 0.0;
uniform vec4 _DepthBulgeMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _DepthBulgeMaskChannel = 0.0;
#endif

uniform float _MainVertexColoringEnabled = 0.0;
uniform float _MainVertexColoringLinearSpace = 1.0;
uniform float _MainVertexColoring = 0.0;
uniform float _MainUseVertexColorAlpha = 0.0;

#if defined(POI_BACKFACE)
uniform float _BackFaceEnabled = 0.0;
uniform float _BackFaceDetailIntensity = 1.0;
uniform float _BackFaceEmissionStrength = 0.0;
uniform vec2 _BackFacePanning;
uniform vec4 _BackFaceColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _BackFaceColorThemeIndex = 0.0;
uniform float _BackFaceReplaceAlpha = 0.0;
uniform vec4 _BackFaceTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _BackFaceTexturePan = vec2(0.0, 0.0);
uniform float _BackFaceTextureUV = 0.0;
uniform vec4 _BackFaceMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _BackFaceMaskPan = vec2(0.0, 0.0);
uniform float _BackFaceMaskUV = 0.0;
uniform float _BackFaceMaskChannel = 0.0;
uniform float _BackFaceHueShiftEnabled = 0.0;
uniform float _BackFaceHueShift = 0.0;
uniform float _BackFaceShiftColorSpace = 0.0;
uniform float _BackFaceHueShiftSpeed = 0.0;
uniform float _BackFaceEmissionLimiter = 1.0;
uniform float _BackFaceHueSelectOrShift = 1.0;
#endif

#if defined(POI_RGBMASK)
uniform vec4 _RGBMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RGBMaskPan = vec2(0.0, 0.0);
uniform float _RGBMaskUV = 0.0;

uniform vec4 _RGBAMetallicMaps_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RGBAMetallicMapsPan = vec2(0.0, 0.0);
uniform float _RGBAMetallicMapsUV = 0.0;
uniform float _RGBAMetallicMapsStochastic = 0.0;
uniform vec4 _RGBARedScaleOffset = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _RGBAGreenScaleOffset = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _RGBABlueScaleOffset = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _RGBAAlphaScaleOffset = vec4(1.0, 1.0, 0.0, 0.0);

uniform float _RGBARedMetallicInvert = 0.0;
uniform float _RGBAGreenMetallicInvert = 0.0;
uniform float _RGBABlueMetallicInvert = 0.0;
uniform float _RGBAAlphaMetallicInvert = 0.0;
uniform float _RGBAMetallicRedEnabled;
uniform float _RGBAMetallicGreenEnabled;
uniform float _RGBAMetallicBlueEnabled;
uniform float _RGBAMetallicAlphaEnabled;

uniform float _RGBARedPBRSplitMaskSample = 0.0;
uniform vec4 _RGBARedPBRMaskScaleTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RGBARedPBRMasksPan = vec2(0.0, 0.0);
uniform float _RGBARedPBRUV = 0.0;
uniform float _RGBARedPBRSplitMaskStochastic = 0.0;

uniform float _RGBAGreenPBRSplitMaskSample = 0.0;
uniform vec4 _RGBAGreenPBRMaskScaleTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RGBAGreenPBRMasksPan = vec2(0.0, 0.0);
uniform float _RGBAGreenPBRUV = 0.0;
uniform float _RGBAGreenPBRSplitMaskStochastic = 0.0;

uniform float _RGBABluePBRSplitMaskSample = 0.0;
uniform vec4 _RGBABluePBRMaskScaleTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RGBABluePBRMasksPan = vec2(0.0, 0.0);
uniform float _RGBABluePBRUV = 0.0;
uniform float _RGBABluePBRSplitMaskStochastic = 0.0;

uniform float _RGBAAlphaPBRSplitMaskSample = 0.0;
uniform vec4 _RGBAAlphaPBRMaskScaleTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RGBAAlphaPBRMasksPan = vec2(0.0, 0.0);
uniform float _RGBAAlphaPBRUV = 0.0;
uniform float _RGBAAlphaPBRSplitMaskStochastic = 0.0;

uniform float _RGBAPBRRedEnabled = 0.0;
uniform float _RGBAPBRGreenEnabled = 0.0;
uniform float _RGBAPBRBlueEnabled = 0.0;
uniform float _RGBAPBRAlphaEnabled = 0.0;

uniform vec4 _RGBASmoothnessMaps_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _RGBASmoothnessMapsPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _RGBASmoothnessMapsUV = 0.0;
uniform float _RGBASmoothnessMapsStochastic = 0.0;
uniform float _RGBARedSmoothnessInvert = 0.0;
uniform float _RGBAGreenSmoothnessInvert = 0.0;
uniform float _RGBABlueSmoothnessInvert = 0.0;
uniform float _RGBAAlphaSmoothnessInvert = 0.0;

uniform float _RGBARedEnable = 0.0;
uniform vec4 _RedTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RedTexturePan = vec2(0.0, 0.0);
uniform float _RedTextureUV = 0.0;
uniform float _RedAlphaAdd = 0.0;
uniform float _RedTextureStochastic = 0.0;
uniform float _RgbRedMaskChannel = 0.0;
uniform float _RgbRedGlobalMaskChannel = 0.0;
uniform float _RgbRedGlobalMaskBlendType = 2.0;
uniform float _RGBARedBlendType = 0.0;
uniform vec4 _RedColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _RedColorThemeIndex = 0.0;
uniform float _RGBARedEmissionStrength = 0.0;
uniform vec4 _RgbNormalR_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RgbNormalRPan = vec2(0.0, 0.0);
uniform float _RgbNormalRUV = 0.0;
uniform float _RgbNormalRScale = 0.0;
uniform float _RgbNormalRStochastic = 0.0;
uniform float _RgbNormalRMaskChannel = 0.0;
uniform float _RgbNormalRGlobalMaskChannel = 0.0;
uniform float _RgbNormalRGlobalMaskBlendType = 2.0;
uniform float _RgbNormalRedBlendMode = 0.0;
uniform vec4 _RgbNormalRBlendSources = vec4(0.0, 0.0, 0.0, 0.0);

uniform float _RGBAGreenEnable = 0.0;
uniform vec4 _GreenTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GreenTexturePan = vec2(0.0, 0.0);
uniform float _GreenTextureUV = 0.0;
uniform float _GreenAlphaAdd = 0.0;
uniform float _GreenTextureStochastic = 0.0;
uniform float _RgbGreenMaskChannel = 1.0;
uniform float _RgbGreenGlobalMaskChannel = 0.0;
uniform float _RgbGreenGlobalMaskBlendType = 2.0;
uniform float _RGBAGreenBlendType = 0.0;
uniform vec4 _GreenColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _GreenColorThemeIndex = 0.0;
uniform float _RGBAGreenEmissionStrength = 0.0;
uniform vec4 _RgbNormalG_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RgbNormalGPan = vec2(0.0, 0.0);
uniform float _RgbNormalGUV = 0.0;
uniform float _RgbNormalGScale = 0.0;
uniform float _RgbNormalGStochastic = 0.0;
uniform float _RgbNormalGMaskChannel = 1.0;
uniform float _RgbNormalGGlobalMaskChannel = 0.0;
uniform float _RgbNormalGGlobalMaskBlendType = 2.0;
uniform float _RgbNormalGreenBlendMode = 0.0;
uniform vec4 _RgbNormalGBlendSources = vec4(0.0, 0.0, 0.0, 0.0);

uniform float _RGBABlueEnable = 0.0;
uniform vec4 _BlueTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _BlueTexturePan = vec2(0.0, 0.0);
uniform float _BlueTextureUV = 0.0;
uniform float _BlueAlphaAdd = 0.0;
uniform float _BlueTextureStochastic = 0.0;
uniform float _RgbBlueMaskChannel = 2.0;
uniform float _RgbBlueGlobalMaskChannel = 0.0;
uniform float _RgbBlueGlobalMaskBlendType = 2.0;
uniform float _RGBABlueBlendType = 0.0;
uniform vec4 _BlueColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _BlueColorThemeIndex = 0.0;
uniform float _RGBABlueEmissionStrength = 0.0;
uniform vec4 _RgbNormalB_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RgbNormalBPan = vec2(0.0, 0.0);
uniform float _RgbNormalBUV = 0.0;
uniform float _RgbNormalBScale = 0.0;
uniform float _RgbNormalBStochastic = 0.0;
uniform float _RgbNormalBMaskChannel = 2.0;
uniform float _RgbNormalBGlobalMaskChannel = 0.0;
uniform float _RgbNormalBGlobalMaskBlendType = 2.0;
uniform float _RgbNormalBlueBlendMode = 0.0;
uniform vec4 _RgbNormalBBlendSources = vec4(0.0, 0.0, 0.0, 0.0);

uniform float _RGBAAlphaEnable = 0.0;
uniform vec4 _AlphaTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _AlphaTexturePan = vec2(0.0, 0.0);
uniform float _AlphaTextureUV = 0.0;
uniform float _AlphaAlphaAdd = 0.0;
uniform float _AlphaTextureStochastic = 0.0;
uniform float _RgbAlphaMaskChannel = 3.0;
uniform float _RgbAlphaGlobalMaskChannel = 0.0;
uniform float _RgbAlphaGlobalMaskBlendType = 2.0;
uniform float _RGBAAlphaBlendType = 0.0;
uniform vec4 _AlphaColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _AlphaColorThemeIndex = 0.0;
uniform float _RGBAAlphaEmissionStrength = 0.0;
uniform vec4 _RgbNormalA_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RgbNormalAPan = vec2(0.0, 0.0);
uniform float _RgbNormalAUV = 0.0;
uniform float _RgbNormalAScale = 0.0;
uniform float _RgbNormalAStochastic = 0.0;
uniform float _RgbNormalAMaskChannel = 3.0;
uniform float _RgbNormalAGlobalMaskChannel = 0.0;
uniform float _RgbNormalAGlobalMaskBlendType = 2.0;
uniform float _RgbNormalAlphaBlendMode = 0.0;
uniform vec4 _RgbNormalABlendSources = vec4(0.0, 0.0, 0.0, 0.0);

uniform float _RGBMaskType = 0.0;
#endif

uniform float _ShadingRampedLightMapApplyGlobalMaskIndex = 0.0;
uniform float _ShadingRampedLightMapApplyGlobalMaskStrength = 1.0;
uniform float _ShadingRampedLightMapApplyGlobalMaskBlendType = 2.0;

uniform float _ShadingRampedLightMapInverseApplyGlobalMaskIndex = 0.0;
uniform float _ShadingRampedLightMapInverseApplyGlobalMaskStrength = 1.0;
uniform float _ShadingRampedLightMapInverseApplyGlobalMaskBlendType = 2.0;

uniform float _ShadowStrength = 1.0;
uniform float _LightingIgnoreAmbientColor = 1.0;
uniform vec3 _LightingShadowColor : source_color = vec3(1.0, 1.0, 1.0);

#if defined(POI_LIGHTING_TEXTURERAMP)
uniform float _ShadowOffset = 0.0;
uniform int _ToonRampCount = 1;
uniform int _ToonRampUVSelector = 1;
#endif

#if defined(POI_LIGHTING_WRAPPED)
uniform vec4 _LightingWrappedColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _LightingWrappedWrap = 0.0;
uniform float _LightingWrappedNormalization = 0.0;
uniform float _LightingGradientStart = 0.0;
uniform float _LightingGradientEnd = 0.5;
#endif

#if defined(POI_LIGHTING_SHADEMAP)
uniform vec3 _1st_ShadeColor : source_color = vec3(1.0, 1.0, 1.0);
uniform vec4 _1st_ShadeMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _1st_ShadeMapPan = vec2(0.0, 0.0);
uniform float _1st_ShadeMapUV = 0.0;
uniform float _Use_1stShadeMapAlpha_As_ShadowMask = 0.0;
uniform float _1stShadeMapMask_Inverse = 0.0;
uniform float _Use_BaseAs1st = 0.0;
uniform vec3 _2nd_ShadeColor : source_color = vec3(1.0, 1.0, 1.0);
uniform vec4 _2nd_ShadeMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _2nd_ShadeMapPan = vec2(0.0, 0.0);
uniform float _2nd_ShadeMapUV = 0.0;
uniform float _Use_2ndShadeMapAlpha_As_ShadowMask = 0.0;
uniform float _2ndShadeMapMask_Inverse = 0.0;
uniform float _Use_1stAs2nd = 0.0;
uniform float _BaseColor_Step = 0.5;
uniform float _BaseShade_Feather = 0.0001;
uniform float _ShadeColor_Step = 0.0;
uniform float _1st2nd_Shades_Feather = 0.0001;
uniform float _ShadingShadeMapBlendType = 0.0;
#endif

#if defined(POI_LIGHTING_SKIN)
uniform float _SssScale = 1.0;
uniform vec4 _SkinThicknessMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _SkinThicknessMapPan = vec2(0.0, 0.0);
uniform float _SkinThicknessMapUV = 0.0;
uniform float _SkinThicknessMapInvert = 0.0;
uniform float _SkinThicknessPower = 1.0;
uniform float _SssBumpBlur = 0.7;
uniform vec3 _SssTransmissionAbsorption = vec3(-8.0, -40.0, -64.0);
uniform vec3 _SssColorBleedAoWeights = vec3(0.4, 0.15, 0.13);
#endif

#if defined(POI_LIGHTING_MULTILAYER_MATH)
uniform vec4 _ShadowBorderMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _ShadowBorderMaskPan = vec2(0.0, 0.0);
uniform float _ShadowBorderMaskUV = 0.0;
uniform float _ShadowPostAO = 0.0;
uniform float _ShadowBorderMaskLOD = 0.0;
uniform vec4 _ShadowAOShift = vec4(0.0, 1.0, 0.0, 1.0);
uniform vec4 _ShadowAOShift2 = vec4(0.0, 1.0, 0.0, 1.0);
uniform float _ShadowBorderMapToggle = 0.0;
uniform vec4 _ShadowColor : source_color = vec4(0.82, 0.76, 0.85, 1.0);
uniform float _LightingMulitlayerNonLinear = 1.0;
uniform vec4 _ShadowColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _ShadowColorTexPan = vec2(0.0, 0.0);
uniform float _ShadowColorTexUV = 0.0;
uniform float _ShadowColorTexAddition = 0.0;
uniform vec4 _MultilayerMathBlurMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MultilayerMathBlurMapPan = vec2(0.0, 0.0);
uniform float _MultilayerMathBlurMapUV = 0.0;
uniform float _ShadowBorder = 0.5;
uniform float _ShadowBlur = 0.1;
uniform float _ShadowReceive = 0.0;
uniform float _ShadowNormalStrength = 1.0;
uniform vec4 _Shadow2ndColor : source_color = vec4(0.68, 0.66, 0.79, 1.0);
uniform vec4 _Shadow2ndColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Shadow2ndColorTexPan = vec2(0.0, 0.0);
uniform float _Shadow2ndColorTexUV = 0.0;
uniform float _Shadow2ndColorTexAddition = 0.0;
uniform float _Shadow2ndBorder = 0.15;
uniform float _Shadow2ndBlur = 0.1;
uniform float _Shadow2ndReceive = 0.0;
uniform float _Shadow2ndNormalStrength = 1.0;
uniform vec4 _Shadow3rdColor : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _Shadow3rdColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Shadow3rdColorTexPan = vec2(0.0, 0.0);
uniform float _Shadow3rdColorTexUV = 0.0;
uniform float _Shadow3rdColorTexAddition = 0.0;
uniform float _Shadow3rdBorder = 0.25;
uniform float _Shadow3rdBlur = 0.1;
uniform float _Shadow3rdReceive = 0.0;
uniform float _Shadow3rdNormalStrength = 1.0;
uniform vec4 _ShadowBorderColor : source_color = vec4(1.0, 0.09999994, 0.0, 1.0);
uniform float _ShadowBorderRange = 0.08;
uniform float _ShadowEnvStrength = 0.0;
uniform float _ShadowMainStrength = 0.0;
uniform float _ShadowMaskType = 0.0;
uniform vec4 _ShadowStrengthMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _ShadowStrengthMaskPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _ShadowStrengthMaskUV = 0.0;
uniform float _ShadowFlatBorder = 1.0;
uniform float _ShadowFlatBlur = 1.0;
uniform float _MultilayerShadowStrength;
#endif

#if defined(POI_LIGHTING_FLAT)
uniform float _ForceFlatRampedLightmap = 1.0;
#endif

#if defined(POI_LIGHTING_CLOTH)
uniform vec4 _ClothMetallicSmoothnessMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _ClothMetallicSmoothnessMapPan = vec2(0.0, 0.0);
uniform float _ClothMetallicSmoothnessMapUV = 0.0;
uniform float _ClothMetallicSmoothnessMapInvert = 0.0;

uniform float _ClothLerp = 0.0;
uniform float _ClothMetallic = 0.0;
uniform float _ClothReflectance = 0.5;
uniform float _ClothSmoothness = 0.5;
#endif

#if defined(POI_LIGHTING_SDF)
uniform float _SDFShadingTextureUV = 0.0;
uniform vec2 _SDFShadingTexturePan = vec2(0.0, 0.0);
uniform vec4 _SDFShadingTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _SDFBlur = 0.1;
uniform vec4 _SDFForward = vec4(0.0, 0.0, 1.0, 0.0);
uniform vec4 _SDFLeft = vec4(-1.0, 0.0, 0.0, 0.0);
#endif

uniform float _LightingAdditiveType = 3.0;
uniform float _LightingAdditiveGradientStart = 0.0;
uniform float _LightingAdditiveGradientEnd = 0.5;
uniform float _LightingAdditiveDetailStrength;

uniform vec4 _DecalMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DecalMaskPan = vec2(0.0, 0.0);
uniform float _DecalMaskUV = 0.0;

#if defined(POI_DECAL_0)
uniform float _Decal0VideoFitToScale = 1.0;
uniform float _Decal0VideoAspectFix = 0.0;
uniform float _Decal0VideoEmissionStrength = 0.0;
uniform float _Decal0VideoEnabled = 0.0;
uniform float _Decal0UseDecalAlpha = 0.0;
uniform float _Decal0OnlyVideo = 0.0;
uniform float _Decal0FaceMask = 0.0;
uniform float _Decal0MaskChannel = 0.0;
uniform float _Decal0GlobalMask = 0.0;
uniform float _Decal0GlobalMaskBlendType = 2.0;
uniform float _Decal0ApplyGlobalMaskIndex = 0.0;
uniform float _Decal0ApplyGlobalMaskBlendType = 0.0;
uniform vec4 _DecalTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DecalTexturePan = vec2(0.0, 0.0);
uniform float _DecalTextureUV = 0.0;
uniform vec4 _DecalColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DecalColorThemeIndex = 0.0;
uniform float _DecalTiled = 0.0;
uniform float _DecalMirroredUVMode = 0.0;
uniform float _DecalSymmetryMode = 0.0;
uniform float _Decal0AspectRatioMode = 0.0;
uniform float _DecalBlendType = 0.0;
uniform float _DecalRotation = 0.0;
uniform vec3 _DecalScale = vec3(1.0, 1.0, 1.0);
uniform vec4 _DecalSideOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _DecalPosition = vec2(0.5, 0.5);
uniform float _DecalRotationSpeed = 0.0;
uniform float _DecalEmissionStrength = 0.0;
uniform float _DecalBlendAlpha = 1.0;
uniform float _DecalAlphaIntensity = 1.0;
uniform float _DecalOverrideAlpha = 0.0;
uniform float _DecalHueShiftColorSpace = 0.0;
uniform float _DecalHueShiftSelectOrShift = 1.0;
uniform float _DecalHueShiftEnabled = 0.0;
uniform float _DecalHueShift = 0.0;
uniform float _DecalHueShiftSpeed = 0.0;
uniform float _DecalColorAdjustEnabled = 0.0;
uniform float _DecalSaturation = 0.0;
uniform float _DecalChromatize = 0.0;
uniform float _DecalBrightness = 0.0;
uniform float _DecalGamma = 1.0;
uniform float _Decal0Depth = 0.0;
uniform float _Decal0HueAngleStrength = 0.0;
uniform float _Decal0ChannelSeparationEnable = 0.0;
uniform float _Decal0ChannelSeparation = 0.0;
uniform float _Decal0ChannelSeparationPremultiply = 0.0;
uniform float _Decal0ChannelSeparationHue = 0.0;
uniform float _Decal0ChannelSeparationVertical = 0.0;
uniform float _Decal0ChannelSeparationAngleStrength = 0.0;
uniform float _Decal0OverrideAlphaMode = 0.0;
uniform float _Decal0UseVideoEffects = 1.0;
uniform int _Decal0OnOutlines = 0;
#endif

#if defined(POI_DECAL_1)
uniform float _Decal1VideoFitToScale = 1.0;
uniform float _Decal1VideoAspectFix = 0.0;
uniform float _Decal1VideoEmissionStrength = 0.0;
uniform float _Decal1VideoEnabled = 0.0;
uniform float _Decal1UseDecalAlpha = 0.0;
uniform float _Decal1OnlyVideo = 0.0;
uniform float _Decal1FaceMask = 0.0;
uniform float _Decal1MaskChannel = 1.0;
uniform float _Decal1GlobalMask = 0.0;
uniform float _Decal1GlobalMaskBlendType = 2.0;
uniform float _Decal1ApplyGlobalMaskIndex = 0.0;
uniform float _Decal1ApplyGlobalMaskBlendType = 0.0;
uniform vec4 _DecalTexture1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DecalTexture1Pan = vec2(0.0, 0.0);
uniform float _DecalTexture1UV = 0.0;
uniform vec4 _DecalColor1 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DecalColor1ThemeIndex = 0.0;
uniform float _DecalTiled1 = 0.0;
uniform float _DecalMirroredUVMode1 = 0.0;
uniform float _DecalSymmetryMode1 = 0.0;
uniform float _Decal1AspectRatioMode = 0.0;
uniform float _DecalBlendType1 = 0.0;
uniform float _DecalRotation1 = 0.0;
uniform vec3 _DecalScale1 = vec3(1.0, 1.0, 1.0);
uniform vec4 _DecalSideOffset1 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _DecalPosition1 = vec2(0.5, 0.5);
uniform float _DecalRotationSpeed1 = 0.0;
uniform float _DecalEmissionStrength1 = 0.0;
uniform float _DecalBlendAlpha1 = 1.0;
uniform float _DecalAlphaIntensity1 = 1.0;
uniform float _DecalOverrideAlpha1 = 0.0;
uniform float _DecalHueShiftColorSpace1 = 0.0;
uniform float _DecalHueShiftSelectOrShift1 = 1.0;
uniform float _DecalHueShiftEnabled1 = 0.0;
uniform float _DecalHueShift1 = 0.0;
uniform float _DecalHueShiftSpeed1 = 0.0;
uniform float _DecalColorAdjustEnabled1 = 0.0;
uniform float _DecalSaturation1 = 0.0;
uniform float _DecalChromatize1 = 0.0;
uniform float _DecalBrightness1 = 0.0;
uniform float _DecalGamma1 = 1.0;
uniform float _Decal1Depth = 0.0;
uniform float _Decal1HueAngleStrength = 0.0;
uniform float _Decal1ChannelSeparationEnable = 0.0;
uniform float _Decal1ChannelSeparation = 0.0;
uniform float _Decal1ChannelSeparationPremultiply = 0.0;
uniform float _Decal1ChannelSeparationHue = 0.0;
uniform float _Decal1ChannelSeparationVertical = 0.0;
uniform float _Decal1ChannelSeparationAngleStrength = 0.0;
uniform float _Decal1OverrideAlphaMode = 0.0;
uniform float _Decal1UseVideoEffects = 1.0;
uniform int _Decal1OnOutlines = 0;
#endif

#if defined(POI_DECAL_2)
uniform float _Decal2VideoFitToScale = 1.0;
uniform float _Decal2VideoAspectFix = 0.0;
uniform float _Decal2VideoEmissionStrength = 0.0;
uniform float _Decal2VideoEnabled = 0.0;
uniform float _Decal2UseDecalAlpha = 0.0;
uniform float _Decal2OnlyVideo = 0.0;
uniform float _Decal2FaceMask = 0.0;
uniform float _Decal2MaskChannel = 2.0;
uniform float _Decal2GlobalMask = 0.0;
uniform float _Decal2GlobalMaskBlendType = 2.0;
uniform float _Decal2ApplyGlobalMaskIndex = 0.0;
uniform float _Decal2ApplyGlobalMaskBlendType = 0.0;
uniform vec4 _DecalTexture2_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DecalTexture2Pan = vec2(0.0, 0.0);
uniform float _DecalTexture2UV = 0.0;
uniform vec4 _DecalColor2 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DecalColor2ThemeIndex = 0.0;
uniform float _DecalTiled2 = 0.0;
uniform float _DecalMirroredUVMode2 = 0.0;
uniform float _DecalSymmetryMode2 = 0.0;
uniform float _Decal2AspectRatioMode = 0.0;
uniform float _DecalBlendType2 = 0.0;
uniform float _DecalRotation2 = 0.0;
uniform vec3 _DecalScale2 = vec3(1.0, 1.0, 1.0);
uniform vec4 _DecalSideOffset2 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _DecalPosition2 = vec2(0.5, 0.5);
uniform float _DecalRotationSpeed2 = 0.0;
uniform float _DecalEmissionStrength2 = 0.0;
uniform float _DecalBlendAlpha2 = 1.0;
uniform float _DecalAlphaIntensity2 = 1.0;
uniform float _DecalOverrideAlpha2 = 0.0;
uniform float _DecalHueShiftColorSpace2 = 0.0;
uniform float _DecalHueShiftSelectOrShift2 = 1.0;
uniform float _DecalHueShiftEnabled2 = 0.0;
uniform float _DecalHueShift2 = 0.0;
uniform float _DecalHueShiftSpeed2 = 0.0;
uniform float _DecalColorAdjustEnabled2 = 0.0;
uniform float _DecalSaturation2 = 0.0;
uniform float _DecalChromatize2 = 0.0;
uniform float _DecalBrightness2 = 0.0;
uniform float _DecalGamma2 = 1.0;
uniform float _Decal2Depth = 0.0;
uniform float _Decal2HueAngleStrength = 0.0;
uniform float _Decal2ChannelSeparationEnable = 0.0;
uniform float _Decal2ChannelSeparation = 0.0;
uniform float _Decal2ChannelSeparationPremultiply = 0.0;
uniform float _Decal2ChannelSeparationHue = 0.0;
uniform float _Decal2ChannelSeparationVertical = 0.0;
uniform float _Decal2ChannelSeparationAngleStrength = 0.0;
uniform float _Decal2OverrideAlphaMode = 0.0;
uniform float _Decal2UseVideoEffects = 1.0;
uniform int _Decal2OnOutlines = 0;
#endif

#if defined(POI_DECAL_3)
uniform float _Decal3VideoFitToScale = 1.0;
uniform float _Decal3VideoAspectFix = 0.0;
uniform float _Decal3VideoEmissionStrength = 0.0;
uniform float _Decal3VideoEnabled = 0.0;
uniform float _Decal3UseDecalAlpha = 0.0;
uniform float _Decal3OnlyVideo = 0.0;
uniform float _Decal3FaceMask = 0.0;
uniform float _Decal3MaskChannel = 3.0;
uniform float _Decal3GlobalMask = 0.0;
uniform float _Decal3GlobalMaskBlendType = 2.0;
uniform float _Decal3ApplyGlobalMaskIndex = 0.0;
uniform float _Decal3ApplyGlobalMaskBlendType = 0.0;
uniform vec4 _DecalTexture3_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DecalTexture3Pan = vec2(0.0, 0.0);
uniform float _DecalTexture3UV = 0.0;
uniform vec4 _DecalColor3 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DecalColor3ThemeIndex = 0.0;
uniform float _DecalTiled3 = 0.0;
uniform float _DecalMirroredUVMode3 = 0.0;
uniform float _DecalSymmetryMode3 = 0.0;
uniform float _Decal3AspectRatioMode = 0.0;
uniform float _DecalBlendType3 = 0.0;
uniform float _DecalRotation3 = 0.0;
uniform vec3 _DecalScale3 = vec3(1.0, 1.0, 1.0);
uniform vec4 _DecalSideOffset3 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _DecalPosition3 = vec2(0.5, 0.5);
uniform float _DecalRotationSpeed3 = 0.0;
uniform float _DecalEmissionStrength3 = 0.0;
uniform float _DecalBlendAlpha3 = 1.0;
uniform float _DecalAlphaIntensity3 = 1.0;
uniform float _DecalOverrideAlpha3 = 0.0;
uniform float _DecalHueShiftColorSpace3 = 0.0;
uniform float _DecalHueShiftSelectOrShift3 = 1.0;
uniform float _DecalHueShiftEnabled3 = 0.0;
uniform float _DecalHueShift3 = 0.0;
uniform float _DecalHueShiftSpeed3 = 0.0;
uniform float _DecalColorAdjustEnabled3 = 0.0;
uniform float _DecalSaturation3 = 0.0;
uniform float _DecalChromatize3 = 0.0;
uniform float _DecalBrightness3 = 0.0;
uniform float _DecalGamma3 = 1.0;
uniform float _Decal3Depth = 0.0;
uniform float _Decal3HueAngleStrength = 0.0;
uniform float _Decal3ChannelSeparationEnable = 0.0;
uniform float _Decal3ChannelSeparation = 0.0;
uniform float _Decal3ChannelSeparationPremultiply = 0.0;
uniform float _Decal3ChannelSeparationHue = 0.0;
uniform float _Decal3ChannelSeparationVertical = 0.0;
uniform float _Decal3ChannelSeparationAngleStrength = 0.0;
uniform float _Decal3OverrideAlphaMode = 0.0;
uniform float _Decal3UseVideoEffects = 1.0;
uniform int _Decal3OnOutlines = 0;
#endif

#if defined(POI_DISSOLVE)
uniform float _DissolveType = 1.0;
uniform float _DissolveAlpha = 0.0;
uniform float _DissolveSoftness = 0.0;
uniform float _DissolveWaveMode = 0.0;
uniform float _DissolveWaveWidth = 0.1;
uniform float _DissolveWaveBounds = 0.0;
uniform float _DissolveDebugGradient = 0.0;
uniform float _DissolveEdgeEnabled = 0.0;
uniform vec4 _DissolveEdgeColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DissolveEdgeColorThemeIndex = 0.0;
uniform float _DissolveEdgeGradient = 0.0;
uniform vec4 _DissolveEdgeColor2 : source_color = vec4(1.0, 0.5, 0.0, 1.0);
uniform float _DissolveEdgeColor2ThemeIndex = 0.0;
uniform vec4 _DissolveEdgeColor3 : source_color = vec4(1.0, 1.0, 0.0, 1.0);
uniform float _DissolveEdgeColor3ThemeIndex = 0.0;
uniform float _DissolveEdgeBorder1 = 0.5;
uniform float _DissolveEdgeBorder2 = 1.0;
uniform float _DissolveEdgeBlur1 = 0.1;
uniform float _DissolveEdgeBlur2 = 0.1;
uniform float _DissolveEdgeWidth = 0.1;
uniform float _DissolveEdgeEmission = 1.0;
uniform float _DissolveEdgeFlip = 0.0;
uniform float _DissolveEdgeHueShiftEnabled = 0.0;
uniform float _DissolveEdgeHueShiftColorSpace = 0.0;
uniform float _DissolveEdgeHueSelectOrShift = 1.0;
uniform float _DissolveEdgeHueShift = 0.0;
uniform float _DissolveEdgeHueShiftSpeed = 0.0;
uniform float _ContinuousDissolveEnabled = 0.0;
uniform float _ContinuousDissolveWave = 0.0;
uniform float _ContinuousDissolveSpeed = 1.0;
uniform vec2 _ContinuousDissolveRange = vec2(0.0, 1.0);
uniform float _ContinuousDissolvePhase = 0.0;
uniform float _ContinuousDissolveBlend = 0.0;
uniform float _DissolveVertexHeightEnabled = 0.0;
uniform float _DissolveVertexHeight = 0.04;
uniform float _DissolveVertexHeightOffset = 0.0;
uniform float _DissolveVertexHeightEasing = 1.0;
uniform float _DissolveVertexHeightDetailStrength = 0.0;
uniform vec3 _DissolveVertexHeightDirection = vec3(0.0, 1.0, 0.0);
uniform float _DissolveVertexHeightDirectionSpace = 0.0;
uniform float _DissolveVertexHeightDirectionBlend = 0.0;
uniform float _DissolveVertexHeightDotMask = 0.0;
uniform float _DissolveVertexHeightContinue = 0.0;
uniform float _DissolveAlphaOnly = 0.0;
uniform vec4 _DissolveAlphaOnlyRamp = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec4 _DissolveTextureColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DissolveTextureColorThemeIndex = 0.0;
uniform vec4 _DissolveToTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DissolveToTexturePan = vec2(0.0, 0.0);
uniform float _DissolveToTextureUV = 0.0;
uniform float _DissolveToEmissionStrength = 0.0;
uniform vec4 _DissolveNoiseTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DissolveNoiseTexturePan = vec2(0.0, 0.0);
uniform float _DissolveNoiseTextureUV = 0.0;
uniform float _DissolveInvertNoise = 0.0;
uniform float _DissolveDetailNoiseEnabled = 0.0;
uniform float _DissolveDetailNoiseMode = 0.0;
uniform vec4 _DissolveDetailNoise_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DissolveDetailNoisePan = vec2(0.0, 0.0);
uniform float _DissolveDetailNoiseUV = 0.0;
uniform float _DissolveInvertDetailNoise = 0.0;
uniform vec4 _DissolveDotNoiseScale = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 _DissolveDotNoiseOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _DissolveDotNoiseSpeed = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _DissolveInvertDotNoise = 0.0;
uniform float _DissolveDetailStrength = 0.1;
uniform float _DissolveDetailEdgeMask = 0.0;
uniform vec4 _DissolveMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DissolveMaskPan = vec2(0.0, 0.0);
uniform float _DissolveMaskUV = 0.0;
uniform float _DissolveMaskGlobalMask = 0.0;
uniform float _DissolveMaskGlobalMaskBlendType = 2.0;
uniform float _DissolveMaskInvert = 0.0;
uniform float _DissolveUseVertexColors = 0.0;
uniform float _DissolveApplyGlobalMaskIndex = 0.0;
uniform float _DissolveApplyGlobalMaskBlendType = 0.0;
uniform float _DissolveInverseApplyGlobalMaskIndex = 0.0;
uniform float _DissolveInverseApplyGlobalMaskBlendType = 0.0;
uniform float _DissolveP2PWorldLocal = 0.0;
uniform float _DissolveP2PUV = 0.0;
uniform vec4 _DissolveStartPoint = vec4(0.0, -1.0, 0.0, 0.0);
uniform vec4 _DissolveEndPoint = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec3 _SphericalDissolveCenter = vec3(0.0, 0.0, 0.0);
uniform float _SphericalDissolveRadius = 1.5;
uniform float _SphericalDissolveInvert = 0.0;
uniform float _CenterOutDissolveMode = 1.0;
uniform vec3 _CenterOutDissolveDirection = vec3(0.0, 0.0, 1.0);
uniform float _CenterOutDissolveInvert = 0.0;
uniform float _CenterOutDissolveNormals = 0.0;
uniform float _CenterOutDissolveLinear = 1.0;
uniform float _DissolveHueShiftEnabled = 0.0;
uniform float _DissolveHueShiftColorSpace = 0.0;
uniform float _DissolveHueSelectOrShift = 1.0;
uniform float _DissolveHueShiftSpeed = 0.0;
uniform float _DissolveHueShift = 0.0;
uniform float _UVTileDissolveEnabled = 0.0;
uniform float _UVTileDissolveDiscardAtMax = 1.0;
uniform float _UVTileDissolveUV = 0.0;
uniform float _UVTileDissolveAlpha_Row0_0 = 0.0;
uniform float _UVTileDissolveAlpha_Row0_1 = 0.0;
uniform float _UVTileDissolveAlpha_Row0_2 = 0.0;
uniform float _UVTileDissolveAlpha_Row0_3 = 0.0;
uniform float _UVTileDissolveAlpha_Row1_0 = 0.0;
uniform float _UVTileDissolveAlpha_Row1_1 = 0.0;
uniform float _UVTileDissolveAlpha_Row1_2 = 0.0;
uniform float _UVTileDissolveAlpha_Row1_3 = 0.0;
uniform float _UVTileDissolveAlpha_Row2_0 = 0.0;
uniform float _UVTileDissolveAlpha_Row2_1 = 0.0;
uniform float _UVTileDissolveAlpha_Row2_2 = 0.0;
uniform float _UVTileDissolveAlpha_Row2_3 = 0.0;
uniform float _UVTileDissolveAlpha_Row3_0 = 0.0;
uniform float _UVTileDissolveAlpha_Row3_1 = 0.0;
uniform float _UVTileDissolveAlpha_Row3_2 = 0.0;
uniform float _UVTileDissolveAlpha_Row3_3 = 0.0;
#endif

#if defined(POI_ANISOTROPICS)

uniform vec4 _AnisoColorMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _AnisoColorMapPan = vec2(0.0, 0.0);
uniform float _AnisoColorMapUV = 0.0;

uniform float _AnisoHideInShadow = 1.0;
uniform float _AnisoFadeByLightDirection = 0.0;
uniform float _AnisoReplace = 0.0;
uniform float _AnisoAdd = 1.0;
uniform float _AnisoUseBaseColor = 0.0;
uniform float _AnisoUseLightColor = 1.0;

uniform float _AnisoGlobalMask = 0.0;
uniform float _AnisoGlobalMaskBlendType = 2.0;
uniform float _AnisoNormalBlend = 1.0;

uniform float _Aniso0Strength = 1.0;
uniform float _Aniso0Power = 0.0;
uniform float _Aniso0Offset = 0.0;
uniform float _Aniso0SwitchDirection = 0.0;
uniform vec4 _Aniso0Tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Aniso0TintIndex = 0.0;
uniform float _Aniso0OffsetMapStrength = 0.0;
uniform float _Aniso0ToonMode = 0.0;
uniform float _Aniso0Edge = 0.5;
uniform float _Aniso0Blur = 0.0;

uniform float _Aniso1Strength = 1.0;
uniform float _Aniso1Power = 0.1;
uniform float _Aniso1Offset = 0.0;
uniform float _Aniso1SwitchDirection = 0.0;
uniform vec4 _Aniso1Tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Aniso1TintIndex = 0.0;
uniform float _Aniso1OffsetMapStrength = 0.0;
uniform float _Aniso1ToonMode = 0.0;
uniform float _Aniso1Edge = 0.5;
uniform float _Aniso1Blur = 0.0;
#endif

#if defined(POI_MATCAP_0)
uniform vec4 _Matcap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MatcapPan = vec2(0.0, 0.0);
uniform float _MatcapUV;
uniform vec4 _MatcapMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MatcapMaskPan = vec2(0.0, 0.0);
uniform float _MatcapMaskUV = 0.0;
uniform float _MatcapMaskChannel = 0.0;
#if defined(POI_MATCAP_0_CUSTOM_NORMAL)
uniform vec4 _Matcap0NormalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap0NormalMapPan = vec2(0.0, 0.0);
uniform float _Matcap0NormalMapUV = 0.0;
uniform float _Matcap0NormalMapScale = 1.0;
#endif
uniform float _Matcap0CircleMaskEnabled = 0.0;
uniform float _Matcap0CircleMaskBorder = 0.45;
uniform float _Matcap0CircleMaskBlur = 0.1;
uniform float _Matcap0LightVsReflection;
uniform float _MatcapUVToBlend = 1.0;
uniform vec4 _MatCapBlendUV1 = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _MatcapUVMode = 1.0;
uniform float _MatcapMaskInvert = 0.0;
uniform float _MatcapMaskGlobalMask = 0.0;
uniform float _MatcapMaskGlobalMaskBlendType = 2.0;
uniform float _MatcapBorder = 0.5;
uniform float _MatcapRotation = 0.0;
uniform float _MatCapVRParallaxStrength = 0.0;
uniform float _MatcapSmoothness = 1.0;
uniform float _MatcapMaskSmoothnessChannel = 3.0;
uniform float _MatcapMaskSmoothnessApply = 0.0;
uniform vec4 _MatcapColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _MatcapBaseColorMix = 0.0;
uniform float _MatcapLightColorMix = 0.0;
uniform float _MatcapColorThemeIndex = 0.0;
uniform float _MatcapIntensity = 1.0;
uniform float _MatcapReplace = 1.0;
uniform float _MatcapMultiply = 0.0;
uniform float _MatcapAdd = 0.0;
uniform float _MatcapAddToLight = 0.0;
uniform float _MatcapMixed = 0.0;
uniform float _MatcapScreen = 0.0;
uniform float _MatcapAlphaOverride = 0.0;
uniform float _MatcapEnable = 0.0;
uniform float _MatcapLightMask = 0.0;
uniform float _MatcapEmissionStrength = 0.0;
uniform float _MatcapNormal = 1.0;
uniform float _MatcapHideWithReflProbe = 0.0;
uniform float _MatcapShowInOutline = 0.0;
uniform float _MatcapHueShiftEnabled = 0.0;
uniform float _MatcapHueShiftColorSpace = 0.0;
uniform float _MatcapHueSelectOrShift = 1.0;
uniform float _MatcapHueShiftSpeed = 0.0;
uniform float _MatcapHueShift = 0.0;
uniform int _MatcapApplyToAlphaEnabled = 0;
uniform int _MatcapApplyToAlphaSourceBlend = 0;
uniform int _MatcapApplyToAlphaBlendType = 0;
uniform float _MatcapApplyToAlphaBlending = 1.0;
uniform float _MatcapTPSDepthEnabled = 0.0;
uniform float _MatcapTPSMaskStrength = 1.0;

uniform float _Matcap0ALEnabled = 0.0;
uniform float _Matcap0ALAlphaAddBand = 0.0;
uniform vec4 _Matcap0ALAlphaAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap0ALEmissionAddBand = 0.0;
uniform vec4 _Matcap0ALEmissionAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap0ALIntensityAddBand = 0.0;
uniform vec4 _Matcap0ALIntensityAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap0ALChronoPanType = 0.0;
uniform float _Matcap0ALChronoPanBand = 0.0;
uniform float _Matcap0ALChronoPanSpeed = 0.0;
uniform vec4 _Matcap0ALColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap0ALColorOverrideEnabled = 0.0;
uniform float _Matcap0ALColorOverrideThemeIndex = 0.0;
uniform float _Matcap0ALColorOverrideBand = 0.0;
uniform vec4 _Matcap0ALColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif
#if defined(POI_MATCAP_1)
uniform vec4 _Matcap2_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap2Pan = vec2(0.0, 0.0);
uniform float _Matcap2UV;
uniform vec4 _Matcap2Mask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap2MaskPan = vec2(0.0, 0.0);
uniform float _Matcap2MaskUV = 0.0;
uniform float _Matcap2MaskChannel = 0.0;
#if defined(POI_MATCAP_1_CUSTOM_NORMAL)
uniform vec4 _Matcap1NormalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap1NormalMapPan = vec2(0.0, 0.0);
uniform float _Matcap1NormalMapUV = 0.0;
uniform float _Matcap1NormalMapScale = 1.0;
#endif
uniform float _Matcap2CircleMaskEnabled = 0.0;
uniform float _Matcap2CircleMaskBorder = 0.45;
uniform float _Matcap2CircleMaskBlur = 0.1;
uniform float _Matcap2UVToBlend = 1.0;
uniform vec4 _MatCap2ndBlendUV1 = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap2UVMode = 1.0;
uniform float _Matcap2MaskInvert = 0.0;
uniform float _Matcap2MaskGlobalMask = 0.0;
uniform float _Matcap2MaskGlobalMaskBlendType = 2.0;
uniform float _Matcap2Border = 0.5;
uniform float _Matcap2Rotation = 0.0;
uniform float _MatCap2ndVRParallaxStrength = 0.0;
uniform float _Matcap2Smoothness = 1.0;
uniform float _Matcap2MaskSmoothnessChannel = 3.0;
uniform float _Matcap2MaskSmoothnessApply = 0.0;
uniform vec4 _Matcap2Color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap2BaseColorMix = 0.0;
uniform float _Matcap2LightColorMix = 0.0;
uniform float _Matcap2ColorThemeIndex = 0.0;
uniform float _Matcap2Intensity = 1.0;
uniform float _Matcap2Replace = 1.0;
uniform float _Matcap2Multiply = 0.0;
uniform float _Matcap2Add = 0.0;
uniform float _Matcap2AddToLight = 0.0;
uniform float _Matcap2Mixed = 0.0;
uniform float _Matcap2Screen = 0.0;
uniform float _Matcap2AlphaOverride = 0.0;
uniform float _Matcap2Enable = 0.0;
uniform float _Matcap2LightMask = 0.0;
uniform float _Matcap2EmissionStrength = 0.0;
uniform float _Matcap2Normal = 1.0;
uniform float _Matcap2HideWithReflProbe = 0.0;
uniform float _Matcap2ShowInOutline = 0.0;
uniform float _Matcap2HueShiftEnabled = 0.0;
uniform float _Matcap2HueShiftColorSpace = 0.0;
uniform float _Matcap2HueSelectOrShift = 1.0;
uniform float _Matcap2HueShiftSpeed = 0.0;
uniform float _Matcap2HueShift = 0.0;
uniform int _Matcap2ApplyToAlphaEnabled = 0;
uniform int _Matcap2ApplyToAlphaSourceBlend = 0;
uniform int _Matcap2ApplyToAlphaBlendType = 0;
uniform float _Matcap2ApplyToAlphaBlending = 1.0;
uniform float _Matcap2TPSDepthEnabled = 0.0;
uniform float _Matcap2TPSMaskStrength = 1.0;

uniform float _Matcap1ALEnabled = 0.0;
uniform float _Matcap1ALAlphaAddBand = 0.0;
uniform vec4 _Matcap1ALAlphaAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap1ALEmissionAddBand = 0.0;
uniform vec4 _Matcap1ALEmissionAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap1ALIntensityAddBand = 0.0;
uniform vec4 _Matcap1ALIntensityAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap1ALChronoPanType = 0.0;
uniform float _Matcap1ALChronoPanBand = 0.0;
uniform float _Matcap1ALChronoPanSpeed = 0.0;
uniform vec4 _Matcap1ALColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap1ALColorOverrideEnabled = 0.0;
uniform float _Matcap1ALColorOverrideThemeIndex = 0.0;
uniform float _Matcap1ALColorOverrideBand = 0.0;
uniform vec4 _Matcap1ALColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_MATCAP_2)
uniform vec4 _Matcap3_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap3Pan = vec2(0.0, 0.0);
uniform float _Matcap3UV;
uniform vec4 _Matcap3Mask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap3MaskPan = vec2(0.0, 0.0);
uniform float _Matcap3MaskUV = 0.0;
uniform float _Matcap3MaskChannel = 0.0;
#if defined(POI_MATCAP_2_CUSTOM_NORMAL)
uniform vec4 _Matcap2NormalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap2NormalMapPan = vec2(0.0, 0.0);
uniform float _Matcap2NormalMapUV = 0.0;
uniform float _Matcap2NormalMapScale = 1.0;
#endif
uniform float _Matcap3CircleMaskEnabled = 0.0;
uniform float _Matcap3CircleMaskBorder = 0.45;
uniform float _Matcap3CircleMaskBlur = 0.1;
uniform float _Matcap3UVToBlend = 1.0;
uniform vec4 _MatCap3rdBlendUV1 = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap3UVMode = 1.0;
uniform float _Matcap3MaskInvert = 0.0;
uniform float _Matcap3MaskGlobalMask = 0.0;
uniform float _Matcap3MaskGlobalMaskBlendType = 2.0;
uniform float _Matcap3Border = 0.5;
uniform float _Matcap3Rotation = 0.0;
uniform float _MatCap3rdVRParallaxStrength = 0.0;
uniform float _Matcap3Smoothness = 1.0;
uniform float _Matcap3MaskSmoothnessChannel = 3.0;
uniform float _Matcap3MaskSmoothnessApply = 0.0;
uniform vec4 _Matcap3Color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap3BaseColorMix = 0.0;
uniform float _Matcap3LightColorMix = 0.0;
uniform float _Matcap3ColorThemeIndex = 0.0;
uniform float _Matcap3Intensity = 1.0;
uniform float _Matcap3Replace = 1.0;
uniform float _Matcap3Multiply = 0.0;
uniform float _Matcap3Add = 0.0;
uniform float _Matcap3AddToLight = 0.0;
uniform float _Matcap3Mixed = 0.0;
uniform float _Matcap3Screen = 0.0;
uniform float _Matcap3AlphaOverride = 0.0;
uniform float _Matcap3Enable = 0.0;
uniform float _Matcap3LightMask = 0.0;
uniform float _Matcap3EmissionStrength = 0.0;
uniform float _Matcap3Normal = 1.0;
uniform float _Matcap3HideWithReflProbe = 0.0;
uniform float _Matcap3ShowInOutline = 0.0;
uniform float _Matcap3HueShiftEnabled = 0.0;
uniform float _Matcap3HueShiftColorSpace = 0.0;
uniform float _Matcap3HueSelectOrShift = 1.0;
uniform float _Matcap3HueShiftSpeed = 0.0;
uniform float _Matcap3HueShift = 0.0;
uniform int _Matcap3ApplyToAlphaEnabled = 0;
uniform int _Matcap3ApplyToAlphaSourceBlend = 0;
uniform int _Matcap3ApplyToAlphaBlendType = 0;
uniform float _Matcap3ApplyToAlphaBlending = 1.0;
uniform float _Matcap3TPSDepthEnabled = 0.0;
uniform float _Matcap3TPSMaskStrength = 1.0;

uniform float _Matcap2ALEnabled = 0.0;
uniform float _Matcap2ALAlphaAddBand = 0.0;
uniform vec4 _Matcap2ALAlphaAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap2ALEmissionAddBand = 0.0;
uniform vec4 _Matcap2ALEmissionAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap2ALIntensityAddBand = 0.0;
uniform vec4 _Matcap2ALIntensityAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap2ALChronoPanType = 0.0;
uniform float _Matcap2ALChronoPanBand = 0.0;
uniform float _Matcap2ALChronoPanSpeed = 0.0;
uniform vec4 _Matcap2ALColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap2ALColorOverrideEnabled = 0.0;
uniform float _Matcap2ALColorOverrideThemeIndex = 0.0;
uniform float _Matcap2ALColorOverrideBand = 0.0;
uniform vec4 _Matcap2ALColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_MATCAP_3)
uniform vec4 _Matcap4_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap4Pan = vec2(0.0, 0.0);
uniform float _Matcap4UV;
uniform vec4 _Matcap4Mask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap4MaskPan = vec2(0.0, 0.0);
uniform float _Matcap4MaskUV = 0.0;
uniform float _Matcap4MaskChannel = 0.0;
#if defined(POI_MATCAP_3_CUSTOM_NORMAL)
uniform vec4 _Matcap3NormalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Matcap3NormalMapPan = vec2(0.0, 0.0);
uniform float _Matcap3NormalMapUV = 0.0;
uniform float _Matcap3NormalMapScale = 1.0;
#endif
uniform float _Matcap4CircleMaskEnabled = 0.0;
uniform float _Matcap4CircleMaskBorder = 0.45;
uniform float _Matcap4CircleMaskBlur = 0.1;
uniform float _Matcap4UVToBlend = 1.0;
uniform vec4 _MatCap4thBlendUV1 = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap4UVMode = 1.0;
uniform float _Matcap4MaskInvert = 0.0;
uniform float _Matcap4MaskGlobalMask = 0.0;
uniform float _Matcap4MaskGlobalMaskBlendType = 2.0;
uniform float _Matcap4Border = 0.5;
uniform float _Matcap4Rotation = 0.0;
uniform float _MatCap4thVRParallaxStrength = 0.0;
uniform float _Matcap4Smoothness = 1.0;
uniform float _Matcap4MaskSmoothnessChannel = 3.0;
uniform float _Matcap4MaskSmoothnessApply = 0.0;
uniform vec4 _Matcap4Color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap4BaseColorMix = 0.0;
uniform float _Matcap4LightColorMix = 0.0;
uniform float _Matcap4ColorThemeIndex = 0.0;
uniform float _Matcap4Intensity = 1.0;
uniform float _Matcap4Replace = 1.0;
uniform float _Matcap4Multiply = 0.0;
uniform float _Matcap4Add = 0.0;
uniform float _Matcap4AddToLight = 0.0;
uniform float _Matcap4Mixed = 0.0;
uniform float _Matcap4Screen = 0.0;
uniform float _Matcap4AlphaOverride = 0.0;
uniform float _Matcap4Enable = 0.0;
uniform float _Matcap4LightMask = 0.0;
uniform float _Matcap4EmissionStrength = 0.0;
uniform float _Matcap4Normal = 1.0;
uniform float _Matcap4HideWithReflProbe = 0.0;
uniform float _Matcap4ShowInOutline = 0.0;
uniform float _Matcap4HueShiftEnabled = 0.0;
uniform float _Matcap4HueShiftColorSpace = 0.0;
uniform float _Matcap4HueSelectOrShift = 1.0;
uniform float _Matcap4HueShiftSpeed = 0.0;
uniform float _Matcap4HueShift = 0.0;
uniform int _Matcap4ApplyToAlphaEnabled = 0;
uniform int _Matcap4ApplyToAlphaSourceBlend = 0;
uniform int _Matcap4ApplyToAlphaBlendType = 0;
uniform float _Matcap4ApplyToAlphaBlending = 1.0;
uniform float _Matcap4TPSDepthEnabled = 0.0;
uniform float _Matcap4TPSMaskStrength = 1.0;

uniform float _Matcap3ALEnabled = 0.0;
uniform float _Matcap3ALAlphaAddBand = 0.0;
uniform vec4 _Matcap3ALAlphaAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap3ALEmissionAddBand = 0.0;
uniform vec4 _Matcap3ALEmissionAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap3ALIntensityAddBand = 0.0;
uniform vec4 _Matcap3ALIntensityAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _Matcap3ALChronoPanType = 0.0;
uniform float _Matcap3ALChronoPanBand = 0.0;
uniform float _Matcap3ALChronoPanSpeed = 0.0;
uniform vec4 _Matcap3ALColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Matcap3ALColorOverrideEnabled = 0.0;
uniform float _Matcap3ALColorOverrideThemeIndex = 0.0;
uniform float _Matcap3ALColorOverrideBand = 0.0;
uniform vec4 _Matcap3ALColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_CUBEMAP)
uniform vec3 _CubeMapRotation = vec3(0.0, 0.0, 0.0);
uniform vec3 _CubeMapRotationPan = vec3(0.0, 0.0, 0.0);
uniform vec4 _CubeMapMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _CubeMapMaskPan = vec2(0.0, 0.0);
uniform float _CubeMapMaskUV = 0.0;
uniform float _CubeMapMaskChannel = 0.0;
uniform float _CubeMapUVMode = 1.0;
uniform float _CubeMapWorldNormalsStrength = 1.0;
uniform float _CubeMapMaskInvert = 0.0;
uniform float _CubeMapMaskGlobalMask = 0.0;
uniform float _CubeMapMaskGlobalMaskBlendType = 2.0;
uniform vec4 _CubeMapColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _CubeMapColorThemeIndex = 0.0;
uniform float _CubeMapIntensity = 1.0;
uniform float _CubemapBlendType = 0.0;
uniform float _CubeMapBlendAmount = 1.0;
uniform float _CubeMapEnable;
uniform float _CubeMapLightMask = 0.0;
uniform float _CubeMapEmissionStrength = 0.0;
uniform float _CubeMapNormal = 1.0;
uniform float _CubeMapHueShiftEnabled = 0.0;
uniform float _CubeMapHueShiftSpeed = 0.0;
uniform float _CubeMapHueShift = 0.0;
uniform float _CubeMapHueShiftColorSpace = 0.0;
uniform float _CubeMapHueSelectOrShift = 1.0;
uniform float _CubeMapSaturation = 1.0;
uniform float _CubeMapBrightness = 0.0;
uniform float _CubeMapContrast = 1.0;
uniform float _CubeMapSmoothness = 1.0;
#endif

#if defined(POI_FLIPBOOK_0)
uniform vec4 _FlipbookTexArray_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _FlipbookColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _FlipbookColorThemeIndex = 0.0;
uniform float _FlipbookFPS = 30.0;
uniform vec2 _FlipbookPosition = vec2(0.5, 0.5);
uniform vec3 _FlipbookScale = vec3(1.0, 1.0, 1.0);
uniform vec4 _FlipbookSideOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _FlipbookAspectRatioMode = 0.0;
uniform float _FlipbookMirroredUVMode = 0.0;
uniform float _FlipbookSymmetryMode = 0.0;
uniform float _FlipbookTiled = 0.0;
uniform float _FlipbookManualFrameControl = 0.0;
uniform float _FlipbookCurrentFrame = 0.0;
uniform float _FlipbookStartAndEnd = 0.0;
uniform float _FlipbookStartFrame = 0.0;
uniform float _FlipbookEndFrame = 0.0;
uniform float _FlipbookEmissionStrength = 0.0;
uniform float _FlipbookRotation = 0.0;
uniform float _EnableFlipbook = 0.0;
uniform float _FlipbookTexArrayUV = 0.0;
uniform float _FlipbookAlphaControlsFinalAlpha = 0.0;
uniform float _FlipbookRotationSpeed = 0.0;
uniform float _FlipbookIntensityControlsAlpha = 0.0;
uniform float _FlipbookColorReplaces = 0.0;
uniform vec2 _FlipbookTexArrayPan = vec2(0.0, 0.0);
uniform float _FlipbookFrameOffset = 0.0;
uniform float _FlipbookAlpha = 1.0;
uniform float _FlipbookMultiply;
uniform float _FlipbookAdd;
uniform float _FlipbookBlendType = 0.0;
uniform vec4 _FlipbookMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _FlipbookMaskPan = vec2(0.0, 0.0);
uniform float _FlipbookMaskUV = 0.0;
uniform float _FlipbookMaskChannel = 0.0;
uniform float _FlipbookMaskGlobalMask = 0.0;
uniform float _FlipbookMaskGlobalMaskBlendType = 2.0;

uniform float _FlipbookMovementType;
uniform vec4 _FlipbookStartEndOffset;
uniform float _FlipbookMovementSpeed;

uniform float _FlipbookCrossfadeEnabled = 0.0;
uniform vec2 _FlipbookCrossfadeRange = vec2(0.75, 1.0);

uniform float _FlipbookHueShiftEnabled = 0.0;
uniform float _FlipbookHueShiftColorSpace = 0.0;
uniform float _FlipbookHueSelectOrShift = 1.0;
uniform float _FlipbookHueShiftSpeed = 0.0;
uniform float _FlipbookHueShift = 0.0;

#endif

#if defined(POI_FLIPBOOK_1)
uniform vec4 _FlipbookTexArray1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _FlipbookColor1 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _FlipbookColorThemeIndex1 = 0.0;
uniform float _FlipbookFPS1 = 30.0;
uniform vec2 _FlipbookPosition1 = vec2(0.5, 0.5);
uniform vec3 _FlipbookScale1 = vec3(1.0, 1.0, 1.0);
uniform vec4 _FlipbookSideOffset1 = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _FlipbookAspectRatioMode1 = 0.0;
uniform float _FlipbookMirroredUVMode1 = 0.0;
uniform float _FlipbookSymmetryMode1 = 0.0;
uniform float _FlipbookTiled1 = 0.0;
uniform float _FlipbookManualFrameControl1 = 0.0;
uniform float _FlipbookCurrentFrame1 = 0.0;
uniform float _FlipbookStartAndEnd1 = 0.0;
uniform float _FlipbookStartFrame1 = 0.0;
uniform float _FlipbookEndFrame1 = 0.0;
uniform float _FlipbookEmissionStrength1 = 0.0;
uniform float _FlipbookRotation1 = 0.0;
uniform float _EnableFlipbook1 = 0.0;
uniform float _FlipbookTexArrayUV1 = 0.0;
uniform float _FlipbookAlphaControlsFinalAlpha1 = 0.0;
uniform float _FlipbookRotationSpeed1 = 0.0;
uniform float _FlipbookIntensityControlsAlpha1 = 0.0;
uniform float _FlipbookColorReplaces1 = 0.0;
uniform vec2 _FlipbookTexArrayPan1 = vec2(0.0, 0.0);
uniform float _FlipbookFrameOffset1 = 0.0;
uniform float _FlipbookAlpha1 = 1.0;
uniform float _FlipbookMultiply1;
uniform float _FlipbookAdd1;
uniform float _FlipbookBlendType1 = 0.0;
uniform vec4 _FlipbookMask1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _FlipbookMaskPan1 = vec2(0.0, 0.0);
uniform float _FlipbookMaskUV1 = 0.0;
uniform float _FlipbookMaskChannel1 = 0.0;
uniform float _FlipbookMaskGlobalMask1 = 0.0;
uniform float _FlipbookMaskGlobalMaskBlendType1 = 2.0;

uniform float _FlipbookMovementType1;
uniform vec4 _FlipbookStartEndOffset1;
uniform float _FlipbookMovementSpeed1;

uniform float _FlipbookCrossfadeEnabled1 = 0.0;
uniform vec2 _FlipbookCrossfadeRange1 = vec2(0.75, 1.0);

uniform float _FlipbookHueShiftEnabled1 = 0.0;
uniform float _FlipbookHueShiftColorSpace1 = 0.0;
uniform float _FlipbookHueSelectOrShift1 = 1.0;
uniform float _FlipbookHueShiftSpeed1 = 0.0;
uniform float _FlipbookHueShift1 = 0.0;

#endif

#if defined(POI_EMISSION_0)
uniform vec4 _EmissionMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMapPan = vec2(0.0, 0.0);
uniform float _EmissionMapUV = 0.0;
uniform vec4 _EmissionMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMaskPan = vec2(0.0, 0.0);
uniform float _EmissionMaskUV = 0.0;
uniform float _EmissionMaskInvert = 0.0;
uniform float _EmissionMaskChannel = 0.0;
uniform float _EmissionMask0GlobalMask = 0.0;
uniform float _EmissionMask0GlobalMaskBlendType = 2.0;
uniform vec4 _EmissionScrollingCurve_ST = vec4(1.0, 1.0, 0.0, 0.0);

uniform float _EmissionFluorescence = 0.0;
uniform vec4 _EmissionColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionBaseColorAsMap = 0.0;
uniform float _EmissionStrength = 0.0;
uniform float _EmissionHueShiftEnabled = 0.0;
uniform float _EmissionHueShiftColorSpace = 0.0;
uniform float _EmissionHueSelectOrShift = 1.0;
uniform float _EmissionSaturation = 0.0;
uniform float _EmissionHueShift = 0.0;
uniform float _EmissionHueShiftSpeed = 0.0;
uniform float _EmissionCenterOutEnabled = 0.0;
uniform float _EmissionCenterOutSpeed = 5.0;
uniform float _EmissionCenterOutLinear = 0.0;
uniform float _EnableGITDEmission = 0.0;
uniform float _GITDEWorldOrMesh = 0.0;
uniform float _GITDEMinEmissionMultiplier = 1.0;
uniform float _GITDEMaxEmissionMultiplier = 0.0;
uniform float _GITDEMinLight = 0.0;
uniform float _GITDEMaxLight = 1.0;
uniform float _EmissionBlinkingEnabled = 0.0;
uniform float _EmissiveBlink_Min = 0.0;
uniform float _EmissiveBlink_Max = 1.0;
uniform float _EmissiveBlink_Velocity = 4.0;
uniform float _EmissionBlinkingOffset = 0.0;
uniform float _EmissiveBlink_Mode = 0.0;
uniform float _EmissiveBlink_Duty = 0.5;
uniform float _ScrollingEmission = 0.0;
uniform vec4 _EmissiveScroll_Direction = vec4(0.0, -10.0, 0.0, 0.0);
uniform float _EmissiveScroll_Width = 10.0;
uniform float _EmissiveScroll_Velocity = 10.0;
uniform float _EmissiveScroll_Interval = 20.0;
uniform float _EmissionScrollingOffset = 0.0;

uniform float _EmissionReplace0 = 0.0;
uniform float _EmissionScrollingVertexColor = 0.0;
uniform float _EmissionScrollingUseCurve = 0.0;
uniform float _EmissionColorThemeIndex = 0.0;

uniform float _EmissionAL0Enabled = 0.0;
uniform vec2 _EmissionAL0StrengthMod = vec2(0.0, 0.0);
uniform float _EmissionAL0StrengthBand = 0.0;
uniform vec2 _EmissionAL0Multipliers = vec2(1.0, 1.0);
uniform float _EmissionAL0MultipliersBand = 0.0;
uniform float _EmissionAL0ColorOverrideEnabled = 0.0;
uniform vec4 _EmissionAL0ColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionAL0ColorOverrideThemeIndex = 0.0;
uniform float _EmissionAL0ColorOverrideBand = 0.0;
uniform vec4 _EmissionAL0ColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_EMISSION_1)
uniform vec4 _EmissionMap1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMap1Pan = vec2(0.0, 0.0);
uniform float _EmissionMap1UV = 0.0;
uniform vec4 _EmissionMask1_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMask1Pan = vec2(0.0, 0.0);
uniform float _EmissionMask1UV = 0.0;
uniform float _EmissionMaskInvert1 = 0.0;
uniform float _EmissionMask1Channel = 0.0;
uniform float _EmissionMask1GlobalMask = 0.0;
uniform float _EmissionMask1GlobalMaskBlendType = 2.0;
uniform vec4 _EmissionScrollingCurve1_ST = vec4(1.0, 1.0, 0.0, 0.0);

uniform float _EmissionFluorescence1 = 0.0;
uniform vec4 _EmissionColor1 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionBaseColorAsMap1 = 0.0;
uniform float _EmissionStrength1 = 0.0;
uniform float _EmissionHueShiftEnabled1 = 0.0;
uniform float _EmissionHueShiftColorSpace1 = 0.0;
uniform float _EmissionHueSelectOrShift1 = 1.0;
uniform float _EmissionSaturation1 = 0.0;
uniform float _EmissionHueShift1 = 0.0;
uniform float _EmissionHueShiftSpeed1 = 0.0;
uniform float _EmissionCenterOutEnabled1 = 0.0;
uniform float _EmissionCenterOutSpeed1 = 5.0;
uniform float _EmissionCenterOutLinear1 = 0.0;
uniform float _EnableGITDEmission1 = 0.0;
uniform float _GITDEWorldOrMesh1 = 0.0;
uniform float _GITDEMinEmissionMultiplier1 = 1.0;
uniform float _GITDEMaxEmissionMultiplier1 = 0.0;
uniform float _GITDEMinLight1 = 0.0;
uniform float _GITDEMaxLight1 = 1.0;
uniform float _EmissionBlinkingEnabled1 = 0.0;
uniform float _EmissiveBlink_Min1 = 0.0;
uniform float _EmissiveBlink_Max1 = 1.0;
uniform float _EmissiveBlink_Velocity1 = 4.0;
uniform float _EmissionBlinkingOffset1 = 0.0;
uniform float _EmissiveBlink_Mode1 = 0.0;
uniform float _EmissiveBlink_Duty1 = 0.5;
uniform float _ScrollingEmission1 = 0.0;
uniform vec4 _EmissiveScroll_Direction1 = vec4(0.0, -10.0, 0.0, 0.0);
uniform float _EmissiveScroll_Width1 = 10.0;
uniform float _EmissiveScroll_Velocity1 = 10.0;
uniform float _EmissiveScroll_Interval1 = 20.0;
uniform float _EmissionScrollingOffset1 = 0.0;

uniform float _EmissionReplace1 = 0.0;
uniform float _EmissionScrollingVertexColor1 = 0.0;
uniform float _EmissionScrollingUseCurve1 = 0.0;
uniform float _EmissionColor1ThemeIndex = 0.0;

uniform float _EmissionAL1Enabled = 0.0;
uniform vec2 _EmissionAL1StrengthMod = vec2(0.0, 0.0);
uniform float _EmissionAL1StrengthBand = 0.0;
uniform vec2 _EmissionAL1Multipliers = vec2(1.0, 1.0);
uniform float _EmissionAL1MultipliersBand = 0.0;
uniform float _EmissionAL1ColorOverrideEnabled = 0.0;
uniform vec4 _EmissionAL1ColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionAL1ColorOverrideThemeIndex = 0.0;
uniform float _EmissionAL1ColorOverrideBand = 0.0;
uniform vec4 _EmissionAL1ColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_EMISSION_2)
uniform vec4 _EmissionMap2_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMap2Pan = vec2(0.0, 0.0);
uniform float _EmissionMap2UV = 0.0;
uniform vec4 _EmissionMask2_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMask2Pan = vec2(0.0, 0.0);
uniform float _EmissionMask2UV = 0.0;
uniform float _EmissionMaskInvert2 = 0.0;
uniform float _EmissionMask2Channel = 0.0;
uniform float _EmissionMask2GlobalMask = 0.0;
uniform float _EmissionMask2GlobalMaskBlendType = 2.0;
uniform vec4 _EmissionScrollingCurve2_ST = vec4(1.0, 1.0, 0.0, 0.0);

uniform float _EmissionFluorescence2 = 0.0;
uniform vec4 _EmissionColor2 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionBaseColorAsMap2 = 0.0;
uniform float _EmissionStrength2 = 0.0;
uniform float _EmissionHueShiftEnabled2 = 0.0;
uniform float _EmissionHueShiftColorSpace2 = 0.0;
uniform float _EmissionHueSelectOrShift2 = 1.0;
uniform float _EmissionSaturation2 = 0.0;
uniform float _EmissionHueShift2 = 0.0;
uniform float _EmissionHueShiftSpeed2 = 0.0;
uniform float _EmissionCenterOutEnabled2 = 0.0;
uniform float _EmissionCenterOutSpeed2 = 5.0;
uniform float _EmissionCenterOutLinear2 = 0.0;
uniform float _EnableGITDEmission2 = 0.0;
uniform float _GITDEWorldOrMesh2 = 0.0;
uniform float _GITDEMinEmissionMultiplier2 = 1.0;
uniform float _GITDEMaxEmissionMultiplier2 = 0.0;
uniform float _GITDEMinLight2 = 0.0;
uniform float _GITDEMaxLight2 = 1.0;
uniform float _EmissionBlinkingEnabled2 = 0.0;
uniform float _EmissiveBlink_Min2 = 0.0;
uniform float _EmissiveBlink_Max2 = 1.0;
uniform float _EmissiveBlink_Velocity2 = 4.0;
uniform float _EmissionBlinkingOffset2 = 0.0;
uniform float _EmissiveBlink_Mode2 = 0.0;
uniform float _EmissiveBlink_Duty2 = 0.5;
uniform float _ScrollingEmission2 = 0.0;
uniform vec4 _EmissiveScroll_Direction2 = vec4(0.0, -10.0, 0.0, 0.0);
uniform float _EmissiveScroll_Width2 = 10.0;
uniform float _EmissiveScroll_Velocity2 = 10.0;
uniform float _EmissiveScroll_Interval2 = 20.0;
uniform float _EmissionScrollingOffset2 = 0.0;

uniform float _EmissionReplace2 = 0.0;
uniform float _EmissionScrollingVertexColor2 = 0.0;
uniform float _EmissionScrollingUseCurve2 = 0.0;
uniform float _EmissionColor2ThemeIndex = 0.0;

uniform float _EmissionAL2Enabled = 0.0;
uniform vec2 _EmissionAL2StrengthMod = vec2(0.0, 0.0);
uniform float _EmissionAL2StrengthBand = 0.0;
uniform vec2 _EmissionAL2Multipliers = vec2(1.0, 1.0);
uniform float _EmissionAL2MultipliersBand = 0.0;
uniform float _EmissionAL2ColorOverrideEnabled = 0.0;
uniform vec4 _EmissionAL2ColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionAL2ColorOverrideThemeIndex = 0.0;
uniform float _EmissionAL2ColorOverrideBand = 0.0;
uniform vec4 _EmissionAL2ColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_EMISSION_3)
uniform vec4 _EmissionMap3_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMap3Pan = vec2(0.0, 0.0);
uniform float _EmissionMap3UV = 0.0;
uniform vec4 _EmissionMask3_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _EmissionMask3Pan = vec2(0.0, 0.0);
uniform float _EmissionMask3UV = 0.0;
uniform float _EmissionMaskInvert3 = 0.0;
uniform float _EmissionMask3Channel = 0.0;
uniform float _EmissionMask3GlobalMask = 0.0;
uniform float _EmissionMask3GlobalMaskBlendType = 2.0;
uniform vec4 _EmissionScrollingCurve3_ST = vec4(1.0, 1.0, 0.0, 0.0);

uniform float _EmissionFluorescence3 = 0.0;
uniform vec4 _EmissionColor3 : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionBaseColorAsMap3 = 0.0;
uniform float _EmissionStrength3 = 0.0;
uniform float _EmissionHueShiftEnabled3 = 0.0;
uniform float _EmissionHueShiftColorSpace3 = 0.0;
uniform float _EmissionHueSelectOrShift3 = 1.0;
uniform float _EmissionSaturation3 = 0.0;
uniform float _EmissionHueShift3 = 0.0;
uniform float _EmissionHueShiftSpeed3 = 0.0;
uniform float _EmissionCenterOutEnabled3 = 0.0;
uniform float _EmissionCenterOutSpeed3 = 5.0;
uniform float _EmissionCenterOutLinear3 = 0.0;
uniform float _EnableGITDEmission3 = 0.0;
uniform float _GITDEWorldOrMesh3 = 0.0;
uniform float _GITDEMinEmissionMultiplier3 = 1.0;
uniform float _GITDEMaxEmissionMultiplier3 = 0.0;
uniform float _GITDEMinLight3 = 0.0;
uniform float _GITDEMaxLight3 = 1.0;
uniform float _EmissionBlinkingEnabled3 = 0.0;
uniform float _EmissiveBlink_Min3 = 0.0;
uniform float _EmissiveBlink_Max3 = 1.0;
uniform float _EmissiveBlink_Velocity3 = 4.0;
uniform float _EmissionBlinkingOffset3 = 0.0;
uniform float _EmissiveBlink_Mode3 = 0.0;
uniform float _EmissiveBlink_Duty3 = 0.5;
uniform float _ScrollingEmission3 = 0.0;
uniform vec4 _EmissiveScroll_Direction3 = vec4(0.0, -10.0, 0.0, 0.0);
uniform float _EmissiveScroll_Width3 = 10.0;
uniform float _EmissiveScroll_Velocity3 = 10.0;
uniform float _EmissiveScroll_Interval3 = 20.0;
uniform float _EmissionScrollingOffset3 = 0.0;

uniform float _EmissionReplace3 = 0.0;
uniform float _EmissionScrollingVertexColor3 = 0.0;
uniform float _EmissionScrollingUseCurve3 = 0.0;
uniform float _EmissionColor3ThemeIndex = 0.0;

uniform float _EmissionAL3Enabled = 0.0;
uniform vec2 _EmissionAL3StrengthMod = vec2(0.0, 0.0);
uniform float _EmissionAL3StrengthBand = 0.0;
uniform vec2 _EmissionAL3Multipliers = vec2(1.0, 1.0);
uniform float _EmissionAL3MultipliersBand = 0.0;
uniform float _EmissionAL3ColorOverrideEnabled = 0.0;
uniform vec4 _EmissionAL3ColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _EmissionAL3ColorOverrideThemeIndex = 0.0;
uniform float _EmissionAL3ColorOverrideBand = 0.0;
uniform vec4 _EmissionAL3ColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);
#endif

#if defined(POI_RIM_0)
uniform float _Is_NormalMapToRimLight = 1.0;
uniform vec4 _RimLightColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _RimLightColorThemeIndex = 0.0;
#if defined(POI_RIM_0_POIYOMI)
uniform float _RimLightingInvert = 0.0;
uniform float _RimWidth = 0.8;
uniform float _RimStrength = 0.0;
uniform float _RimBlur = 0.0;
uniform float _RimBaseColorMix = 0.0;
uniform float _EnableRimLighting = 0.0;
uniform float _RimWidthNoiseStrength;
uniform vec4 _RimShadowAlpha = vec4(0.0, 0.0, 0.0, 1.0);
uniform float _RimShadowWidth = 0.0;
uniform float _RimBlendStrength = 1.0;
uniform float _RimPoiBlendMode = 0.0;
uniform float _RimShadowToggle = 0.0;
uniform float _RimPower = 1.0;
uniform float _RimLinear = 0.0;
uniform float _RimShadowMaskStrength = 1.0;
uniform float _RimShadowMaskRampType = 0.0;
uniform float _RimShadowMaskInvert = 0.0;
uniform float _RimBrightness = 1.0;
uniform vec4 _RimTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RimTexPan = vec2(0.0, 0.0);
uniform float _RimTexUV = 0.0;
uniform vec4 _RimMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RimMaskPan = vec2(0.0, 0.0);
uniform float _RimMaskUV = 0.0;
uniform float _RimMaskChannel = 0.0;
uniform float _RimMaskInvert = 0.0;
uniform float _RimBiasIntensity = 0.0;
uniform int _RimApplyAlpha = 0;
uniform float _RimApplyAlphaBlend = 1.0;
#endif

#if defined(POI_RIM_0_UTS2)
uniform float _RimLight;
uniform float _Is_LightColor_RimLight = 1.0;
uniform float _RimLight_Power = 0.1;
uniform float _RimLight_InsideMask = 0.0001;
uniform float _RimLight_FeatherOff = 0.0;
uniform float _LightDirection_MaskOn = 0.0;
uniform float _Tweak_LightDirection_MaskLevel = 0.0;
uniform float _Add_Antipodean_RimLight = 0.0;
uniform vec4 _Ap_RimLightColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _RimApColorThemeIndex = 0.0;
uniform float _Is_LightColor_Ap_RimLight = 1.0;
uniform float _Ap_RimLight_Power = 0.1;
uniform float _Ap_RimLight_FeatherOff = 0.0;
uniform vec4 _Set_RimLightMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Set_RimLightMaskPan = vec2(0.0, 0.0);
uniform float _Set_RimLightMaskUV = 0.0;
uniform float _Set_RimLightMaskChannel = 1.0;
uniform float _Tweak_RimLightMaskLevel = 0.0;
#endif

#if defined(POI_RIM_0_LILTOON)
uniform vec4 _RimColor : source_color = vec4(0.66, 0.5, 0.48, 1.0);
uniform vec4 _RimColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RimColorTexPan = vec2(0.0, 0.0);
uniform float _RimColorTexUV = 0.0;
uniform float _RimMainStrength = 0.0;
uniform float _RimNormalStrength = 1.0;
uniform float _RimBorder = 0.5;
uniform float _RimBlur = 0.0;
uniform float _RimFresnelPower = 3.5;
uniform float _RimEnableLighting = 1.0;
uniform float _RimShadowMask = 0.5;
uniform int _RimBackfaceMask = 1;
uniform float _RimVRParallaxStrength = 1.0;
uniform float _RimDirStrength = 0.0;
uniform float _RimDirRange = 0.0;
uniform float _RimIndirRange = 0.0;
uniform vec4 _RimIndirColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _RimIndirBorder = 0.5;
uniform float _RimIndirBlur = 0.1;
uniform float _RimMaskOnlyMask = 0.0;
uniform int _RimBlendMode = 1;
#endif

uniform float _RimGlobalMask = 0.0;
uniform float _RimGlobalMaskBlendType = 2.0;
uniform float _RimApplyGlobalMaskIndex = 0.0;
uniform float _RimApplyGlobalMaskBlendType = 2.0;

uniform float _RimHueShiftEnabled = 0.0;
uniform float _RimHueShiftColorSpace = 0.0;
uniform float _RimHueSelectOrShift = 1.0;
uniform float _RimHueShiftSpeed = 0.0;
uniform float _RimHueShift = 0.0;
#endif
#if defined(POI_RIM_1)
uniform float _Is_NormalMapToRim2Light = 1.0;
uniform vec4 _Rim2LightColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Rim2LightColorThemeIndex = 0.0;

#if defined(POI_RIM_1_POIYOMI)
uniform float _Rim2LightingInvert = 0.0;
uniform float _Rim2Width = 0.8;
uniform float _Rim2Strength = 0.0;
uniform float _Rim2Blur = 0.0;
uniform float _Rim2BaseColorMix = 0.0;
uniform float _EnableRim2Lighting = 0.0;
uniform float _Rim2WidthNoiseStrength;
uniform vec4 _Rim2ShadowAlpha = vec4(0.0, 0.0, 0.0, 1.0);
uniform float _Rim2ShadowWidth = 0.0;
uniform float _Rim2BlendStrength = 1.0;
uniform float _RimPoi2BlendMode = 0.0;
uniform float _Rim2ShadowToggle = 0.0;
uniform float _Rim2Power = 1.0;
uniform float _Rim2Linear = 0.0;
uniform float _Rim2ShadowMaskStrength = 1.0;
uniform float _Rim2ShadowMaskRampType = 0.0;
uniform float _Rim2ShadowMaskInvert = 0.0;
uniform float _Rim2Brightness = 1.0;
uniform vec4 _Rim2Tex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Rim2TexPan = vec2(0.0, 0.0);
uniform float _Rim2TexUV = 0.0;
uniform vec4 _Rim2Mask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Rim2MaskPan = vec2(0.0, 0.0);
uniform float _Rim2MaskUV = 0.0;
uniform float _Rim2MaskChannel = 0.0;
uniform float _Rim2MaskInvert = 0.0;
uniform float _Rim2BiasIntensity = 0.0;
uniform int _Rim2ApplyAlpha = 0;
uniform float _Rim2ApplyAlphaBlend = 1.0;
#endif

#if defined(POI_RIM_1_UTS2)
uniform float _Rim2Light;
uniform float _Is_LightColor_Rim2Light = 1.0;
uniform float _Rim2Light_Power = 0.1;
uniform float _Rim2Light_InsideMask = 0.0001;
uniform float _Rim2Light_FeatherOff = 0.0;
uniform float _LightDirection_MaskOn2 = 0.0;
uniform float _Tweak_LightDirection_MaskLevel2 = 0.0;
uniform float _Add_Antipodean_Rim2Light = 0.0;
uniform vec4 _Ap_Rim2LightColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Rim2ApColorThemeIndex = 0.0;
uniform float _Is_LightColor_Ap_Rim2Light = 1.0;
uniform float _Ap_Rim2Light_Power = 0.1;
uniform float _Ap_Rim2Light_FeatherOff = 0.0;
uniform vec4 _Set_Rim2LightMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Set_Rim2LightMaskPan = vec2(0.0, 0.0);
uniform float _Set_Rim2LightMaskUV = 0.0;
uniform float _Set_Rim2LightMaskChannel = 1.0;
uniform float _Tweak_Rim2LightMaskLevel = 0.0;
#endif

#if defined(POI_RIM_1_LILTOON)
uniform vec4 _Rim2Color : source_color = vec4(0.66, 0.5, 0.48, 1.0);
uniform vec4 _Rim2ColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Rim2ColorTexPan = vec2(0.0, 0.0);
uniform float _Rim2ColorTexUV = 0.0;
uniform float _Rim2MainStrength = 0.0;
uniform float _Rim2NormalStrength = 1.0;
uniform float _Rim2Border = 0.5;
uniform float _Rim2Blur = 0.0;
uniform float _Rim2FresnelPower = 3.5;
uniform float _Rim2EnableLighting = 1.0;
uniform float _Rim2ShadowMask = 0.5;
uniform int _Rim2BackfaceMask = 1;
uniform float _Rim2VRParallaxStrength = 1.0;
uniform float _Rim2DirStrength = 0.0;
uniform float _Rim2DirRange = 0.0;
uniform float _Rim2IndirRange = 0.0;
uniform vec4 _Rim2IndirColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _Rim2IndirBorder = 0.5;
uniform float _Rim2IndirBlur = 0.1;
uniform float _Rim2MaskOnlyMask = 0.0;
uniform int _Rim2BlendMode = 1;
#endif

uniform float _Rim2GlobalMask = 0.0;
uniform float _Rim2GlobalMaskBlendType = 2.0;
uniform float _Rim2ApplyGlobalMaskIndex = 0.0;
uniform float _Rim2ApplyGlobalMaskBlendType = 2.0;

uniform float _Rim2HueShiftEnabled = 0.0;
uniform float _Rim2HueShiftColorSpace = 0.0;
uniform float _Rim2HueSelectOrShift = 1.0;
uniform float _Rim2HueShiftSpeed = 0.0;
uniform float _Rim2HueShift = 0.0;
#endif

#if defined(POI_DEPTH_RIM)
uniform float _DepthRimNormalToUse = 1.0;
uniform float _DepthRimWidth = 0.08;
uniform float _DepthRimSharpness = 0.5;
uniform float _DepthRimDepthThreshold = 0.25;
uniform float _DepthRimBinary = 1.0;
uniform float _DepthRimHideInShadow = 0.0;
uniform float _DepthRimLightDirMethod = 0.0;
uniform float _DepthRimShadowMask = 0.0;
uniform float _DepthRimMixRampedLightMap = 0.0;
uniform float _DepthRimCameraClip = 0.5;
uniform float _DepthRimMinDistance = 0.0;
uniform float _DepthRimMaxDistance = 0.0;
uniform vec4 _DepthRimColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _DepthRimColorThemeIndex = 0.0;
uniform float _DepthRimMixBaseColor = 0.0;
uniform float _DepthRimEmission = 0.0;
uniform float _DepthRimReplace = 0.0;
uniform float _DepthRimAdd = 0.0;
uniform float _DepthRimScreen = 1.0;
uniform float _DepthRimMultiply = 0.0;
uniform float _DepthRimAdditiveLighting = 0.0;
uniform float _DepthRimMixLightColor = 0.0;
uniform float _DepthRimType = 2.0;
uniform float _DepthRimBrightness = 1.0;
uniform float _DepthRimIgnoreLighting = 0.0;

uniform vec4 _DepthRimMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DepthRimMaskPan = vec2(0.0, 0.0);
uniform float _DepthRimMaskUV = 0.0;
uniform float _DepthRimMaskChannel = 0.0;
#endif

#if defined(POI_GLITTER)
uniform vec4 _GlitterRandomRotationSpeed = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _GlitterLayers = 1.0;
uniform float _GlitterUseNormals = 0.0;
uniform float _GlitterUV = 0.0;
uniform vec4 _GlitterColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _GlitterColorThemeIndex = 0.0;
uniform float _GlitterSpeed = 10.0;
uniform float _GlitterBrightness = 3.0;
uniform float _GlitterFrequency = 300.0;
uniform float _GlitterRandomLocation = 1.0;
uniform float _GlitterSize = 0.3;
uniform float _GlitterContrast = 300.0;
uniform float _GlitterAngleRange = 90.0;
uniform float _GlitterMinBrightness = 0.0;
uniform float _GlitterBias = 0.8;
uniform float _GlitterUseSurfaceColor = 0.0;
uniform float _GlitterBlendType = 0.0;
uniform float _GlitterMode = 0.0;
uniform float _GlitterShape = 0.0;
uniform float _GlitterLinearMinBrightness = 0.0;
uniform float _GlitterLinearBrightness = 3.0;
uniform float _GlitterLinearSpeed = 0.25;
uniform float _GlitterLinearSensitivity = 0.25;
uniform float _GlitterLinearContrast = 50.0;
uniform float _GlitterLinearPostContrast = 1.0;
uniform float _GlitterLinearAngle = 0.0;
uniform float _GlitterLinearLightDirection = 0.0;
uniform float _GlitterLinearVRParallax = 0.0;
uniform float _GlitterLinearDistanceLOD = 1.0;
uniform float _GlitterJaggyFix = 0.0;
uniform float _GlitterDistanceLOD = 1.0;
uniform float _GlitterTextureRotation = 0.0;
uniform vec2 _GlitterUVPanning = vec2(0.0, 0.0);

uniform float _GlitterHueShiftEnabled = 0.0;
uniform float _GlitterHueShiftColorSpace = 0.0;
uniform float _GlitterHueShiftSpeed = 0.0;
uniform float _GlitterHueShift = 0.0;
uniform float _GlitterHueSelectOrShift = 1.0;
uniform float _GlitterHueShiftRandomOffset = 0.0;
uniform float _GlitterHideInShadow = 0.0;
uniform float _GlitterScaleWithLighting = 0.0;

uniform float _GlitterRandomColors = 0.0;
uniform vec2 _GlitterMinMaxSaturation = vec2(0.8, 1.0);
uniform vec2 _GlitterMinMaxBrightness = vec2(0.8, 1.0);
uniform float _GlitterRandomSize = 0.0;
uniform vec4 _GlitterMinMaxSize = vec4(0.1, 0.5, 0.0, 1.0);
uniform float _GlitterFullCoverage = 0.0;
uniform float _GlitterRandomRotation = 0.0;
uniform float _GlitterRandomRotationDirection = 0.0;
uniform vec4 _GlitterMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GlitterMaskPan = vec2(0.0, 0.0);
uniform float _GlitterMaskUV = 0.0;
uniform float _GlitterMaskChannel = 0.0;
uniform float _GlitterMaskInvert = 0.0;
uniform float _GlitterMaskGlobalMask = 0.0;
uniform float _GlitterMaskGlobalMaskBlendType = 2.0;
uniform vec4 _GlitterColorMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _GlitterColorMapPan = vec2(0.0, 0.0);
uniform float _GlitterColorMapUV = 0.0;
uniform vec4 _GlitterTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _GlitterTexturePixelMode = 0.0;

uniform float _GlitterALEnabled = 0.0;
uniform float _GlitterALAlphaAddBand = 0.0;
uniform vec4 _GlitterALAlphaAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _GlitterALMaxBrightnessBand = 0.0;
uniform vec4 _GlitterALMaxBrightnessAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _GlitterALSizeAddBand = 0.0;
uniform vec4 _GlitterALSizeAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _GlitterALChronoSparkleSpeedType = 0.0;
uniform float _GlitterALChronoSparkleSpeedBand = 0.0;
uniform float _GlitterALChronoSparkleSpeed = 0.0;
uniform float _GlitterALChronoRotationSpeedType = 0.0;
uniform float _GlitterALChronoRotationSpeedBand = 0.0;
uniform float _GlitterALChronoRotationSpeed = 0.0;
uniform float _GlitterALColorOverrideEnabled = 0.0;
uniform vec4 _GlitterALColorOverride : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _GlitterALColorOverrideThemeIndex = 0.0;
uniform float _GlitterALColorOverrideBand = 0.0;
uniform vec4 _GlitterALColorOverrideRamp = vec4(0.0, 1.0, 0.0, 1.0);

uniform float _GlitterSpriteSheetEnabled = 0.0;
uniform float _GlitterSpriteSheetColumns = 1.0;
uniform float _GlitterSpriteSheetRows = 1.0;
uniform float _GlitterSpriteSheetFrameSelection = 0.0;
uniform float _GlitterSpriteSheetFPS = 10.0;
uniform float _GlitterSpriteSheetRandomStart = 0.0;
uniform float _GlitterSpriteSheetTotalFrames = 0.0;
#endif

#if defined(POI_SUBSURFACESCATTERING)
uniform vec4 _SSSColor : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec4 _SSSThicknessMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _SSSThicknessMapPan = vec2(0.0, 0.0);
uniform float _SSSThicknessMapUV = 0.0;
uniform float _SSSThicknessMapChannel = 0.0;
uniform float _SSSThicknessMod = -1.0;
uniform float _SSSStrength = 0.5;
uniform float _SSSSpread = 2.0;
uniform float _SSSDistortion = 1.0;
uniform float _SSSLightColorMix = 0.0;
uniform float _SSSBaseColorMix = 0.0;
uniform float _SSSShadowStrength = 0.25;
uniform float _SSSGlobalMaskIndex = 0.0;
uniform float _SSSGlobalMaskBlendType = 2.0;
#endif

#if defined(POI_MOCHIE_BRDF)
uniform float _PBRMapsStochastic;
uniform vec4 _MochieMetallicMaps_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MochieMetallicMapsPan = vec2(0.0, 0.0);
uniform float _MochieMetallicMapsUV = 0.0;
uniform float _MochieMetallicMapsStochastic = 0.0;
uniform float _MochieMetallicMapInvert = 0.0;
uniform float _MochieRoughnessMapInvert = 0.0;
uniform float _MochieReflectionMaskInvert = 0.0;
uniform float _MochieSpecularMaskInvert = 0.0;
uniform float _MochieMetallicMapsMetallicChannel = 0.0;
uniform float _MochieMetallicMapsRoughnessChannel = 1.0;
uniform float _MochieMetallicMapsReflectionMaskChannel = 2.0;
uniform float _MochieMetallicMapsSpecularMaskChannel = 3.0;
uniform float _PBRNormalSelect = 1.0;

uniform float _MochieReflectionTintThemeIndex = 0.0;
uniform float _MochieSpecularTintThemeIndex = 0.0;

uniform float _MochieRoughnessMultiplier = 1.0;
uniform float _MochieMetallicMultiplier = 0.0;
uniform float _MochieReflectionStrength = 1.0;
uniform float _MochieSpecularStrength = 1.0;
uniform vec4 _MochieSpecularTint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _MochieReflectionTint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _MochieLitFallback = 1.0;
uniform float _IgnoreCastedShadows = 0.0;
uniform float _PBRSplitMaskSample = 0.0;
uniform float _PBRSplitMaskStochastic = 0.0;
uniform vec4 _PBRMaskScaleTiling = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _MochieMetallicMasksUV = 0.0;
uniform vec4 _MochieMetallicMasksPan = vec4(0.0, 0.0, 0.0, 0.0);

uniform float _Specular2ndLayer = 0.0;
uniform float _MochieSpecularStrength2 = 1.0;
uniform float _MochieRoughnessMultiplier2 = 1.0;
uniform float _RefSpecFresnelStrength = 0.5;
uniform float _SFExposureOcclusion = 0.0;
uniform float _MochieForceFallback = 0.0;
uniform float _MochieGSAAEnabled = 1.0;
uniform float _PoiGSAAVariance = 0.15;
uniform float _PoiGSAAThreshold = 0.1;
uniform float _BRDFTPSReflectionMaskStrength = 1.0;
uniform float _BRDFTPSSpecularMaskStrength = 1.0;
uniform float _BRDFTPSDepthEnabled = 0.0;

uniform float _MochieMetallicGlobalMask = 0.0;
uniform float _MochieMetallicGlobalMaskBlendType = 2.0;
uniform float _MochieSmoothnessGlobalMask = 0.0;
uniform float _MochieSmoothnessGlobalMaskBlendType = 2.0;
uniform float _MochieReflectionStrengthGlobalMask = 0.0;
uniform float _MochieReflectionStrengthGlobalMaskBlendType = 2.0;
uniform float _MochieSpecularStrengthGlobalMask = 0.0;
uniform float _MochieSpecularStrengthGlobalMaskBlendType = 2.0;

#if defined(POI_MOCHIE_GGX_ANISO)
uniform vec4 _AnisotropyMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _AnisotropyMapPan = vec2(0.0, 0.0);
uniform float _AnisotropyMapUV = 0.0;
uniform float _AnisotropyMapChannel = 0.0;
uniform float _Anisotropy = 0.0;
uniform float _ReflectionAnisotropicStretch = 1.0;
uniform float _RoughnessAnisotropy = 1.0;
#endif
#endif
#if defined(POI_CLEARCOAT)
uniform vec4 _ClearCoatMaps_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _ClearCoatMapsPan = vec2(0.0, 0.0);
uniform float _ClearCoatMapsUV = 0.0;
uniform float _ClearCoatMapsStochastic = 0.0;
uniform float _ClearCoatMapsClearCoatMaskChannel = 0.0;
uniform float _ClearCoatMapsRoughnessChannel = 1.0;
uniform float _ClearCoatMapsReflectionMaskChannel = 2.0;
uniform float _ClearCoatMapsSpecularMaskChannel = 3.0;
uniform float _ClearCoatBRDF = 0.0;
uniform float _ClearCoatReflectionStrength = 1.0;
uniform float _ClearCoatSpecularStrength = 1.0;
uniform float _ClearCoatStrength = 1.0;
uniform float _ClearCoatSmoothness = 1.0;
uniform vec4 _ClearCoatReflectionTint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _ClearCoatReflectionTintThemeIndex = 0.0;
uniform vec4 _ClearCoatSpecularTint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _ClearCoatSpecularTintThemeIndex = 0.0;
uniform float _ClearCoatSmoothnessMapInvert = 0.0;
uniform float _ClearCoatMaskInvert = 0.0;
uniform float _ClearCoatReflectionMaskInvert = 0.0;
uniform float _ClearCoatSpecularMaskInvert = 0.0;
uniform float _ClearCoatTPSMaskStrength = 1.0;
uniform float _ClearCoatTPSDepthMaskEnabled = 0.0;
uniform float _ClearCoatNormalSelect = 0.0;

uniform float _ClearCoatForceFallback = 0.0;
uniform float _ClearCoatLitFallback = 1.0;
uniform float _CCIgnoreCastedShadows = 0.0;
uniform float _ClearCoatGSAAEnabled = 1.0;
uniform float _ClearCoatGSAAVariance = 0.15;
uniform float _ClearCoatGSAAThreshold = 0.1;
uniform float _ClearcoatFresnelStrength = 0.5;
uniform float _ClearcoatExposureOcclusion = 0.0;

uniform float _ClearCoatGlobalMask = 0.0;
uniform float _ClearCoatGlobalMaskBlendType = 2.0;
uniform float _ClearCoatSmoothnessGlobalMask = 0.0;
uniform float _ClearCoatSmoothnessGlobalMaskBlendType = 2.0;
uniform float _ClearCoatReflectionStrengthGlobalMask = 0.0;
uniform float _ClearCoatReflectionStrengthGlobalMaskBlendType = 2.0;
uniform float _ClearCoatSpecularStrengthGlobalMask = 0.0;
uniform float _ClearCoatSpecularStrengthGlobalMaskBlendType = 2.0;
#endif

#if defined(POI_ENVIRORIM)
uniform vec4 _RimEnviroMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _RimEnviroMaskPan = vec2(0.0, 0.0);
uniform float _RimEnviroMaskUV = 0.0;
uniform float _RimEnviroChannel = 0.0;
uniform float _RimEnviroBlur = 0.7;
uniform float _RimEnviroMinBrightness = 0.0;
uniform float _RimEnviroWidth = 0.45;
uniform float _RimEnviroSharpness = 0.0;
uniform float _RimEnviroIntensity = 1.0;
#endif

#if defined(POI_STYLIZED_SPECULAR)
uniform float _StylizedReflectionMode = 0.0;

#if defined(POI_STYLIZED_SPECULAR_UTS)
uniform vec4 _HighColor_Tex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _HighColor_TexPan = vec2(0.0, 0.0);
uniform float _HighColor_TexUV = 0.0;

uniform vec4 _Set_HighColorMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _Set_HighColorMaskPan = vec2(0.0, 0.0);
uniform float _Set_HighColorMaskUV = 0.0;
uniform float _Set_HighColorMaskChannel = 1.0;
uniform float _Tweak_HighColorMaskLevel = 0.0;
uniform float _StylizedSpecularInvertMask = 0.0;
uniform float _StylizedSpecularNormalStrength = 1.0;

uniform vec4 _HighColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _UseLightColor = 1.0;
uniform float _HighColor_Power = 0.2;
uniform float _StylizedSpecularFeather = 0.0;
uniform float _Layer1Strength = 1.0;
uniform float _Layer2Size = 0.0;
uniform float _StylizedSpecular2Feather = 0.0;
uniform float _Layer2Strength = 0.0;
uniform float _StylizedSpecularStrength = 1.0;
uniform float _HighColorThemeIndex = 0.0;
uniform float _Is_BlendAddToHiColor = 0.0;
uniform float _Is_SpecularToHighColor = 0.0;
uniform float _StylizedSpecularIgnoreNormal = 0.0;
uniform float _StylizedSpecularIgnoreShadow = 0.0;
uniform float _SSIgnoreCastedShadows = 0.0;
uniform float _UseSpecularOptMap2;
#endif

#if defined(POI_STYLIZED_SPECULAR_LILTOON)
uniform float _UseReflection = 1.0;
uniform float _Smoothness = 1.0;
uniform vec4 _SmoothnessTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _SmoothnessTexPan = vec2(0.0, 0.0);
uniform float _SmoothnessTexUV = 0.0;
uniform float _Metallic = 0.0;
uniform vec4 _MetallicGlossMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MetallicGlossMapPan = vec2(0.0, 0.0);
uniform float _MetallicGlossMapUV = 0.0;
uniform float _Reflectance = 0.04;
uniform float _GSAAStrength = 0.0;
uniform float _ApplySpecular = 1.0;
uniform float _ApplySpecularFA = 1.0;
uniform float _SpecularToon = 1.0;
uniform float _SpecularNormalStrength = 1.0;
uniform float _SpecularBorder = 0.5;
uniform float _SpecularBlur = 0.0;
uniform float _ApplyReflection = 0.0;
uniform float _ReflectionNormalStrength = 1.0;
uniform vec4 _ReflectionColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _ReflectionColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _ReflectionColorTexPan = vec2(0.0, 0.0);
uniform float _ReflectionColorTexUV = 0.0;
uniform float _ReflectionApplyTransparency = 1.0;
uniform vec4 _ReflectionCubeColor : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float _ReflectionCubeOverride = 0.0;
uniform float _ReflectionCubeEnableLighting = 1.0;
uniform float _ReflectionBlendMode = 1.0;
#endif
#endif

#if defined(POI_PATHING)
uniform vec4 _PathingMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _PathingMapPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _PathingMapUV = 0.0;
uniform float _PathPointSampling = 1.0;

uniform vec4 _PathingColorMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _PathingColorMapPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _PathingColorMapUV = 0.0;
uniform float _PathingUVSelect = 0.0;

uniform float _PathingOverrideAlpha = 0.0;
uniform float _PathSource = 0.0;
uniform float _PathSourceDirR = 0.0;
uniform float _PathSourceDirG = 0.0;
uniform float _PathSourceDirB = 0.0;
uniform float _PathSourceDirA = 0.0;
uniform float _EnablePathRemapping = 0.0;
uniform vec4 _PathRemapR = vec4(0.0, 1.0, 0.0, 1.0);
uniform vec4 _PathRemapG = vec4(0.0, 1.0, 0.0, 1.0);
uniform vec4 _PathRemapB = vec4(0.0, 1.0, 0.0, 1.0);
uniform vec4 _PathRemapA = vec4(0.0, 1.0, 0.0, 1.0);
uniform vec4 _PathGapLengths = vec4(0.1, 0.1, 0.1, 0.1);

uniform float _PathTypeR = 2.0;
uniform float _PathTypeG = 2.0;
uniform float _PathTypeB = 2.0;
uniform float _PathTypeA = 2.0;
uniform float _PathGradientType = 0.0;
uniform vec4 _PathWidth = vec4(0.03, 0.03, 0.03, 0.03);
uniform vec4 _PathTime = vec4(-999.0, -999.0, -999.0, -999.0);
uniform vec4 _PathOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _PathSpeed = vec4(0.1, 0.1, 0.1, 0.1);
uniform vec4 _PathColorR : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _PathColorG : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _PathColorB : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _PathColorA : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _PathEmissionStrength = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _PathSoftness = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _PathSegments = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _PathSurfaceBlendMode = 0.0;

uniform float _PathColorRThemeIndex = 0.0;
uniform float _PathColorGThemeIndex = 0.0;
uniform float _PathColorBThemeIndex = 0.0;
uniform float _PathColorAThemeIndex = 0.0;

uniform float _PathGlobalMaskR = 0.0;
uniform float _PathGlobalMaskRBlendType = 2.0;
uniform float _PathGlobalMaskG = 0.0;
uniform float _PathGlobalMaskGBlendType = 2.0;
uniform float _PathGlobalMaskB = 0.0;
uniform float _PathGlobalMaskBBlendType = 2.0;
uniform float _PathGlobalMaskA = 0.0;
uniform float _PathGlobalMaskABlendType = 2.0;

#endif

#if defined(POI_MIRROR)
uniform float _VisibilityMode = 1.0;
uniform float _Mirror = 0.0;
uniform float _MirrorTextureEnabled = 0.0;
uniform float _MirrorTextureForceEnabled = 0.0;
uniform float _VisibilityVRCRegular = 1.0;
uniform float _VisibilityVRCMirrorVR = 1.0;
uniform float _VisibilityVRCMirrorDesktop = 1.0;
uniform float _VisibilityVRCCameraVR = 1.0;
uniform float _VisibilityVRCCameraDesktop = 1.0;
uniform float _VisibilityVRCCameraScreenshot = 1.0;
#if defined(POI_MIRROR_TEXTURE)
uniform vec4 _MirrorColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _MirrorColorThemeIndex = 0.0;
uniform float _MirrorTextureBlendType = 0.0;
uniform vec4 _MirrorTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _MirrorTexturePan = vec2(0.0, 0.0);
uniform float _MirrorTextureUV = 0.0;
#endif
#endif

#if defined(POI_DEPTH_FX)
uniform vec4 _DepthMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DepthMaskPan = vec2(0.0, 0.0);
uniform float _DepthMaskUV = 0.0;
uniform float _DepthMaskChannel = 0.0;
uniform float _DepthMaskGlobalMask = 0.0;
uniform float _DepthMaskGlobalMaskBlendType = 2.0;
uniform float _DepthColorToggle = 0.0;
uniform float _DepthColorBlendMode = 0.0;
uniform vec4 _DepthTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _DepthTexturePan = vec2(0.0, 0.0);
uniform float _DepthTextureUV = 0.0;
uniform vec3 _DepthColor : source_color = vec3(1.0, 1.0, 1.0);
uniform float _DepthColorThemeIndex = 0.0;
uniform float _DepthColorMinDepth = 0.0;
uniform float _DepthColorMaxDepth = 1.0;
uniform float _DepthColorMinValue = 1.0;
uniform float _DepthColorMaxValue = 0.0;
uniform float _DepthEmissionStrength = 0.0;

uniform float _DepthAlphaToggle = 0.0;
uniform float _DepthAlphaMinValue = 1.0;
uniform float _DepthAlphaMaxValue = 0.0;
uniform float _DepthAlphaMinDepth = 0.0;
uniform float _DepthAlphaMaxDepth = 1.0;
#endif

uniform float _PPLightingMultiplier = 1.0;
uniform float _PPLightingAddition = 0.0;
uniform float _PPEmissionMultiplier = 1.0;
uniform float _PPFinalColorMultiplier = 1.0;

#if defined(POI_TEXT)
uniform vec4 _TextGlyphs_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _TextPixelRange = 6.0;

uniform float _TextThiccness = 0.0;
uniform vec2 _TextScale = vec2(1.0, 1.0);

uniform float _TextShadowEnabled = 0.0;
uniform float _TextShadowSize = 0.3;
uniform float _TextShadowSoftness = 1.0;
uniform vec2 _TextShadowOffset = vec2(0.0, 0.0);
uniform vec4 _TextShadowColor : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float _TextShadowColorThemeIndex = 0.0;
uniform float _TextShadowEmissionStrength = 0.0;

uniform float _TextBevelEnabled = 0.0;
uniform float _TextBevelWidth = 0.25;
uniform float _TextBevelStrength = 1.0;
uniform float _TextBevelLightAngle = 135.0;

uniform float _TextHollowEnabled = 0.0;
uniform float _TextHollowWidth = 0.15;

uniform float _TextFPSEnabled = 0.0;
uniform float _TextFPSUV = 0.0;
uniform vec4 _TextFPSColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _TextFPSEmissionStrength = 0.0;
uniform vec4 _TextFPSPadding = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _TextFPSOffset = vec2(0.0, 0.0);
uniform vec2 _TextFPSScale = vec2(1.0, 1.0);
uniform float _TextFPSRotation = 0.0;
uniform float _TextFPSColorThemeIndex = 0.0;

uniform float _TextPositionEnabled = 0.0;
uniform float _TextPositionUV = 0.0;
uniform float _TextPositionVertical;
uniform vec4 _TextPositionColor : source_color = vec4(1.0, 0.0, 1.0, 1.0);
uniform float _TextPositionEmissionStrength = 0.0;
uniform vec4 _TextPositionPadding = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _TextPositionOffset = vec2(0.0, 0.0);
uniform vec2 _TextPositionScale = vec2(1.0, 1.0);
uniform float _TextPositionRotation = 0.0;
uniform float _TextPositionColorThemeIndex = 0.0;

uniform float _TextTimeEnabled = 0.0;
uniform float _TextTimeUV = 0.0;
uniform vec4 _TextTimeColor : source_color = vec4(1.0, 0.0, 1.0, 1.0);
uniform float _TextTimeEmissionStrength = 0.0;
uniform vec4 _TextTimePadding = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _TextTimeOffset = vec2(0.0, 0.0);
uniform vec2 _TextTimeScale = vec2(1.0, 1.0);
uniform float _TextTimeRotation = 0.0;
uniform float _TextTimeColorThemeIndex = 0.0;

uniform float _TextUTCTimeEnabled = 0.0;
uniform float _TextUTCTimeUV = 0.0;
uniform vec4 _TextUTCTimeColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _TextUTCTimeEmissionStrength = 0.0;
uniform vec4 _TextUTCTimePadding = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _TextUTCTimeOffset = vec2(0.0, 0.0);
uniform vec2 _TextUTCTimeScale = vec2(1.0, 1.0);
uniform float _TextUTCTimeRotation = 0.0;
uniform float _TextUTCTimeFormat = 0.0;
uniform float _TextUTCTimeZone = 0.0;
uniform float _TextUTCTimeZoneOffset = 0.0;
uniform float _TextUTCTimeColorThemeIndex = 0.0;

uniform float _TextNumericEnabled = 0.0;
uniform float _TextNumericUV = 0.0;
uniform vec4 _TextNumericColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _TextNumericEmissionStrength = 0.0;
uniform vec4 _TextNumericPadding = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec2 _TextNumericOffset = vec2(0.0, 0.0);
uniform vec2 _TextNumericScale = vec2(1.0, 1.0);
uniform float _TextNumericRotation = 0.0;
uniform float _TextNumericValue = 0.0;
uniform float _TextNumericWholeDigits = 4.0;
uniform float _TextNumericDecimalDigits = 0.0;
uniform float _TextNumericTrimZeroes = 0.0;
uniform float _TextNumericColorThemeIndex = 0.0;
#endif

uniform float _FXProximityColor = 0.0;
uniform float _FXProximityColorType = 1.0;
uniform float _FXProximityColorMinDistance = 0.0;
uniform float _FXProximityColorMaxDistance = 1.0;
uniform vec4 _FXProximityColorCurve = vec4(0.0, 0.3333333, 0.6666667, 1.0);
uniform int _FXProximityColorSolid = 0;
uniform vec4 _FXProximityColorEdgeColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _FXProximityColorEdgeWidth = 0.05;
uniform vec4 _FXProximityColorMinColor : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 _FXProximityColorMaxColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _FXProximityColorMinColorThemeIndex = 0.0;
uniform float _FXProximityColorMaxColorThemeIndex = 0.0;
uniform int _FXProximityColorBackFace = 0;
uniform int _FXProximityColorOverrideColor = 1;
uniform int _FXProximityColorOverrideAlpha = 0;
uniform int _FXProximityColorOverrideEmission = 0;

#if defined(POI_POSTPROCESS)
uniform vec4 _PPMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _PPMaskPan = vec2(0.0, 0.0);
uniform float _PPMaskUV = 0.0;
uniform float _PPMaskChannel = 0.0;
uniform float _PPMaskInvert = 0.0;

uniform vec3 _PPTint : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3 _PPRGB = vec3(1.0, 1.0, 1.0);
uniform float _PPHue = 0.0;
uniform float _PPHueSpeed = 0.0;
uniform float _PPHueOffset = 0.0;
uniform float _PPHueOffsetMask = 0.0;
uniform float _PPContrast = 1.0;
uniform float _PPSaturation = 1.0;
uniform float _PPBrightness = 1.0;
uniform float _PPLightness = 0.0;
uniform float _PPHDR = 0.0;
uniform float _PPHueShiftColorSpace = 0.0;
uniform float _ppHueSelectOrShift = 1.0;
uniform float _PPPosterization = 0.0;
uniform float _PPPosterizationAmount = 4.0;
#endif

#if defined(POI_INTERNALPARALLAX)
uniform vec4 _ParallaxInternalMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _ParallaxInternalMapUV = 0.0;
uniform vec2 _ParallaxInternalMapPan = vec2(0.0, 0.0);
uniform vec4 _ParallaxInternalPosition = vec4(0.5, 0.5, 0.0, 0.0);
uniform float _ParallaxInternalRotation = 0.0;
uniform vec4 _ParallaxInternalScale = vec4(1.0, 1.0, 1.0, 0.0);
uniform vec4 _ParallaxInternalSideOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _ParallaxInternalTiled = 1.0;
uniform vec4 _ParallaxInternalMapMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _ParallaxInternalMapMaskPan = vec2(0.0, 0.0);
uniform float _ParallaxInternalMapMaskUV = 0.0;
uniform float _ParallaxInternalMapMaskChannel = 0.0;
uniform float _ParallaxInternalMapMaskInvert = 0.0;
uniform float _ParallaxInternalMapMaskGlobalMask = 0.0;
uniform float _ParallaxInternalMapMaskGlobalMaskBlendType = 2.0;

uniform float _ParallaxInternalIterations = 4.0;
uniform float _ParallaxInternalIterationsMin = 1.0;
uniform vec4 _ParallaxInternalIterationsDistance = vec4(5.0, 15.0, 0.0, 0.0);
uniform float _ParallaxInternalMinDepth = 0.0;
uniform float _ParallaxInternalMaxDepth = 0.1;
uniform float _ParallaxInternalMinFade = 1.0;
uniform float _ParallaxInternalMaxFade = 0.1;
uniform vec4 _ParallaxInternalMinColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _ParallaxInternalMaxColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _ParallaxInternalMinColorThemeIndex = 0.0;
uniform float _ParallaxInternalMaxColorThemeIndex = 0.0;
uniform vec2 _ParallaxInternalPerLevelOffset = vec2(0.0, 0.0);
uniform float _ParallaxInternalHeightmapMode = 0.0;
uniform float _ParallaxInternalHeightFromAlpha = 0.0;

uniform float _ParallaxInternalHueShiftEnabled = 0.0;
uniform float _ParallaxInternalHueShift = 0.0;
uniform float _ParallaxInternalHueShiftSpeed = 0.0;
uniform float _ParallaxInternalHueShiftPerLevel = 0.0;
uniform float _ParallaxInternalHueShiftColorSpace = 0.0;
uniform float _ParallaxInternalHueSelectOrShift = 1.0;
uniform float _ParallaxInternalSurfaceBlendMode = 8.0;
uniform float _ParallaxInternalBlendMode = 0.0;

#endif

uniform float _NormalCorrect = 0.0;
uniform float _NormalCorrectAmount = 0.9;
uniform vec3 _NormalCorrectOrigin = vec3(0.0, 0.4, -0.025);

#if defined(POI_VIDEO_EFFECTS)
uniform float _VideoEffectsEnable = 0.0;
uniform vec4 _VideoPixelTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _VideoPixelTextureUV = 0.0;
uniform vec4 _VideoMaskTexture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _VideoMaskTexturePan = vec2(0.0, 0.0);
uniform float _VideoMaskTextureUV = 0.0;
uniform float _VideoMaskTextureChannel = 0.0;

uniform float _VideoType = 3.0;
uniform vec2 _VideoResolution = vec2(1280.0, 720.0);
uniform float _VideoBacklight = 1.0;
uniform float _VideoCRTRefreshRate = 24.0;
uniform float _VideoCRTPixelEnergizedTime = 1.9;
uniform float _VideoRepeatVideoTexture;
uniform float _VideoPixelateToResolution = 0.0;
uniform vec2 _VideoMaskPanning;
uniform float _VideoGlobalMask = 0.0;
uniform float _VideoGlobalMaskBlendType = 2.0;

uniform float _VideoSaturation = 0.0;
uniform float _VideoContrast = 0.0;
uniform float _VideoEmissionEnabled = 1.0;
#endif

#if defined(POI_BACKLIGHT)
uniform vec4 _BacklightColor : source_color = vec4(0.85, 0.8, 0.7, 1.0);
uniform vec4 _BacklightColorTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _BacklightColorTexPan = vec2(0.0, 0.0);
uniform float _BacklightColorTexUV = 0.0;
uniform float _BacklightMainStrength = 0.0;
uniform float _BacklightNormalStrength = 1.0;
uniform float _BacklightBorder = 0.65;
uniform float _BacklightBlur = 0.05;
uniform float _BacklightDirectivity = 5.0;
uniform float _BacklightViewStrength = 1.0;
uniform int _BacklightReceiveShadow = 1;
uniform int _BacklightBackfaceMask = 1;
uniform float _BacklightGlobalMask = 0.0;
uniform float _BacklightGlobalMaskBlendType = 2.0;
uniform float _BacklightApplyGlobalMaskIndex = 0.0;
uniform float _BacklightApplyGlobalMaskBlendType = 2.0;
#endif

#if defined(POI_VORONOI)
uniform vec4 _VoronoiMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _VoronoiMaskPan = vec2(0.0, 0.0);
uniform float _VoronoiMaskUV = 0.0;
uniform int _VoronoiMaskChannel = 0;
uniform vec4 _VoronoiNoise_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec2 _VoronoiNoisePan = vec2(0.0, 0.0);
uniform float _VoronoiNoiseUV = 0.0;
uniform int _VoronoiNoiseChannel = 0;
uniform int _VoronoiSpace = 0;
uniform float _VoronoiUV = 0.0;
uniform int _VoronoiBlend = 0;
uniform int _VoronoiType = 1;
uniform vec4 _VoronoiOuterColor : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float _VoronoiOuterColorThemeIndex = 0.0;
uniform float _VoronoiOuterEmissionStrength = 0.0;
uniform vec4 _VoronoiInnerColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _VoronoiInnerColorThemeIndex = 0.0;
uniform float _VoronoiInnerEmissionStrength = 0.0;
uniform float _VoronoiPower = 0.45454545;
uniform vec2 _VoronoiGradient = vec2(0.0, 0.5);
uniform float _VoronoiScale = 5.0;
uniform vec3 _VoronoiSpeed = vec3(1.0, 1.0, 1.0);
uniform float _VoronoiEnableRandomCellColor = 0.0;
uniform vec2 _VoronoiRandomMinMaxSaturation = vec2(0.8, 1.0);
uniform vec2 _VoronoiRandomMinMaxBrightness = vec2(0.8, 1.0);
uniform float _VoronoiNoiseIntensity = 0.1;
uniform int _VoronoiAffectsMaterialAlpha = 0;
uniform float _VoronoiGlobalMask = 0.0;
uniform float _VoronoiGlobalMaskBlendType = 2.0;

#endif

#if defined(POI_TRUCHET)
uniform float _TruchetUV = 0.0;
uniform float _TruchetBlendType = 0.0;
uniform float _TruchetDensity = 10.0;
uniform float _TruchetThiccness = 0.1;
uniform vec4 _TruchetColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float _TruchetColorThemeIndex = 0.0;
uniform vec4 _TruchetTex_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _TruchetTexPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _TruchetPanOffset = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _TruchetAlpha = 1.0;
uniform float _TruchetEdgeSharpness = 1.0;
uniform float _TruchetEmissionStrength = 0.0;
uniform float _TruchetHideWhenNoAL = 0.0;
uniform vec4 _TruchetMask_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _TruchetMaskPan = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _TruchetMaskUV = 0.0;
uniform float _TruchetMaskChannel = 0.0;
uniform float _TruchetMaskInvert = 0.0;
uniform float _TruchetMaskGlobalMask = 0.0;
uniform float _TruchetMaskGlobalMaskBlendType = 2.0;
uniform float _TruchetMaskIntensity = 1.0;
uniform float _TruchetOverrideAlpha = 0.0;

#endif

uniform float _UzumoreCategoryToggle = 0.0;
uniform float _UzumoreEnabled = 1.0;
uniform float _UzumoreAmount = 0.1;
uniform float _UzumoreBias = 0.001;
uniform float _UzumoreObjectScale = 1.0;
uniform float _UzumoreMaskUV = 0.0;
uniform float _UzumoreMaskChannel = 3.0;
uniform vec4 _UzumoreMask_ST = vec4(1.0, 1.0, 0.0, 0.0);

