// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Mochie BRDF, clear coat and environment rim.
//
// A shader here cannot sample the scene's reflection probes; the engine adds its own environment specular
// (sky radiance and reflection probes, times its environment BRDF of METALLIC, SPECULAR, ALBEDO and
// ROUGHNESS), and its clear coat lobe (CLEARCOAT, CLEARCOAT_ROUGHNESS) on top. The source's probe terms are
// expressed through those inputs: applyReflectionOutputs (in PoiStylizedSpecular, after lilToon's part) and
// applyClearCoatOutputs. Material fallback cubemaps the shader samples itself. Per-light parts (Mochie
// specular, clear coat specular, the lit share of a fallback cubemap) run in light().
//
// Mochie's base lobe:
//   METALLIC  = metallic * reflection mask. The engine's 1 - METALLIC on ambient and light diffuse is the
//               source's metallic darkening, and its F0 mixes toward the albedo like specCol.
//   SPECULAR  = sqrt(dielectric F0 / 0.16) for the source's 0.04 * (1 - metallic) * reflection mask.
//   ROUGHNESS = the perceptual roughness after GSAA (the probe mip the source picks).
//   RADIANCE  = alpha 1 - (exposure occlusion * bent normal occlusion * AO * tint luminance), black, which
//               scales the sky radiance by that visibility; a forced fallback cubemap replaces it (alpha 1).

//ifex _MochieBRDF==0 && _ClearCoatBRDF==0
#if defined(POI_MOCHIE_BRDF) || defined(POI_CLEARCOAT)

/*
* Copyright 2022 orels1
*
* Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

// https://github.com/orels1/orels-Unity-Shaders

float GSAA_Filament(vec3 worldNormal, float perceptualRoughness, float gsaaVariance, float gsaaThreshold)
{
	// Kaplanyan 2016, "Stable specular highlights"
	// Tokuyoshi 2017, "Error Reduction and Simplification for Shading Anti-Aliasing"
	// Tokuyoshi and Kaplanyan 2019, "Improved Geometric Specular Antialiasing"

	vec3 du = dFdx(worldNormal);
	vec3 dv = dFdy(worldNormal);

	float variance = gsaaVariance * (dot(du, du) + dot(dv, dv));

	float roughness = perceptualRoughness * perceptualRoughness;
	float kernelRoughness = min(2.0 * variance, gsaaThreshold);
	float squareRoughness = clamp(roughness * roughness + kernelRoughness, 0.0, 1.0);

	return sqrt(sqrt(squareRoughness));
}

/*
MIT END
*/

// The source's GetWorldReflections and probe GetReflections are the engine's environment (see the top).

#if defined(PROP_MOCHIEREFLCUBE) || defined(PROP_CLEARCOATFALLBACK)
// A macro: a sampler cannot be a function parameter here. Blends the two mips around the roughness's level.
#define SampleFallbackCube(reflectionCube, reflectionDir, perceptualRoughness) mix(textureLod(reflectionCube, (reflectionDir), floor((perceptualRoughness) * POI_SPECCUBE_LOD_STEPS)).rgb, textureLod(reflectionCube, (reflectionDir), floor((perceptualRoughness) * POI_SPECCUBE_LOD_STEPS) + 1.0).rgb, fract((perceptualRoughness) * POI_SPECCUBE_LOD_STEPS))
#endif

#if defined(POI_MOCHIE_BRDF) && defined(POI_MOCHIE_GGX_ANISO)
float GetGGXTerm(float nDotL, float nDotV, float nDotH, float roughness, float tDotV, float bDotV, float tDotL, float bDotL, float tDotH, float bDotH, vec2 adjustedAnisotropy)
{
	float pbrAnistropics = mix(roughness, 1.0, (1.0 - _RoughnessAnisotropy) * adjustedAnisotropy.y);
	float at = max(pbrAnistropics * ((1.0 + (roughness * adjustedAnisotropy.y)) + adjustedAnisotropy.x), 0.005);
	float ab = max(pbrAnistropics * ((1.0 + (roughness * adjustedAnisotropy.y)) - adjustedAnisotropy.x), 0.005);
	float visibilityTerm = 0.0;

	if (nDotL > 0.0)
	{
		float a2 = at * ab;
		// V_SmithGGXCorrelated
		float lambdaV = nDotL * length(vec3(at * tDotV, ab * bDotV, nDotV));
		float lambdaL = nDotV * length(vec3(at * tDotL, ab * bDotL, nDotL));
		visibilityTerm = 0.5 / (lambdaV + lambdaL + 1e-5);

		vec3 v = vec3(ab * tDotH, at * bDotH, a2 * nDotH);
		float v2 = dot(v, v);
		float w2 = a2 / (v2 + 1e-5);
		float dotTerm = a2 * (w2 * w2 * INV_PI);

		visibilityTerm *= dotTerm;
	}
	return visibilityTerm;
}
#endif

float GetGGXTerm(float nDotL, float nDotV, float nDotH, float roughness)
{
	float visibilityTerm = 0.0;
	if (nDotL > 0.0)
	{
		float lambdaV = nDotL * (nDotV * (1.0 - roughness) + roughness);
		float lambdaL = nDotV * (nDotL * (1.0 - roughness) + roughness);

		visibilityTerm = 0.5 / (lambdaV + lambdaL + 1e-5);
		float a = nDotH * roughness;
		float k = roughness / (1.0 - nDotH * nDotH + a * a + 1e-5);
		float dotTerm = k * k * INV_PI;

		visibilityTerm *= dotTerm;
	}
	return visibilityTerm;
}

#if defined(POI_MOCHIE_BRDF) && defined(POI_MOCHIE_GGX_ANISO)
void GetSpecFresTerm(float nDotL, float nDotV, float nDotH, float lDotH, inout vec3 specularTerm, inout vec3 fresnelTerm, vec3 specCol, float roughness, float tDotV, float bDotV, float tDotL, float bDotL, float tDotH, float bDotH, vec2 adjustedAnisotropy)
{
	specularTerm = vec3(GetGGXTerm(nDotL, nDotV, nDotH, roughness, tDotV, bDotV, tDotL, bDotL, tDotH, bDotH, adjustedAnisotropy));
	fresnelTerm = FresnelTerm(specCol, lDotH);
	specularTerm = max(vec3(0.0), specularTerm * max(0.00001, nDotL));
}
#endif

void GetSpecFresTerm(float nDotL, float nDotV, float nDotH, float lDotH, inout vec3 specularTerm, inout vec3 fresnelTerm, vec3 specCol, float roughness)
{
	specularTerm = vec3(GetGGXTerm(nDotL, nDotV, nDotH, roughness));
	fresnelTerm = FresnelTerm(specCol, lDotH);
	specularTerm = max(vec3(0.0), specularTerm * max(0.00001, nDotL));
}

float GetRoughness(float smoothness)
{
	float rough = 1.0 - smoothness;
	rough *= 1.7 - 0.7 * rough;
	return rough;
}

// fragment() has no light: its exposure occlusion reads the brighter of the unit light state's colors, so a
// color mode without a direct color does not black the reflections out.
#define POI_FRAGMENT_EXPOSURE_COLOR(poiLight) max((poiLight).directColor, (poiLight).indirectColor)

float SFVisibility(float brdfRoughness, vec3 directColor, float NDotV, float ExposureOcclusion)
{
	//Silent's exposure occlusion
	float Visibility = clamp(length(directColor + EPSILON) * (1.0 / max(ExposureOcclusion, EPSILON)), 0.0, 1.0); //Using direct color because I think it should be generally more forgiving
	// Lagarde and de Rousiers 2014, "Moving Frostbite to PBR"
	return clamp(pow(NDotV + Visibility, exp2(-16.0 * brdfRoughness - 1.0)) - 1.0 + Visibility, 0.0, 1.0);
}
#endif
//endex

//ifex _MochieBRDF==0
#if defined(POI_MOCHIE_BRDF)
void MetallicAndSpecularFragDataInit(inout PoiFragData poiFragData, inout MochieBRDFData mochieBRDFData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	float smoothness = _MochieRoughnessMultiplier;
	float smoothness2 = _MochieRoughnessMultiplier2;
	float metallic = _MochieMetallicMultiplier;
	float specularMask = 1.0;
	float reflectionMask = 1.0;

	smoothness *= poiFragData.smoothness;
	smoothness2 *= poiFragData.smoothness2;
	metallic *= poiFragData.metallic;
	specularMask *= poiFragData.specularMask;
	reflectionMask *= poiFragData.reflectionMask;

#if defined(PROP_MOCHIEMETALLICMAPS)
	vec4 PBRMaps = POI2D_SAMPLER_PAN_STOCHASTIC(_MochieMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_MochieMetallicMapsUV)], _MochieMetallicMaps_ST), _MochieMetallicMapsPan, _MochieMetallicMapsStochastic);
	if (_PBRSplitMaskSample >= 0.5)
	{
		vec4 PBRSplitMask = POI2D_SAMPLER_PAN_STOCHASTIC(_MochieMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_MochieMetallicMasksUV)], _PBRMaskScaleTiling), _MochieMetallicMasksPan.xy, _PBRSplitMaskStochastic);
		float reflVal = _MochieMetallicMapsReflectionMaskChannel < 4.0 ? PBRSplitMask[int(min(_MochieMetallicMapsReflectionMaskChannel, 3.0))] : 1.0;
		float specVal = _MochieMetallicMapsSpecularMaskChannel < 4.0 ? PBRSplitMask[int(min(_MochieMetallicMapsSpecularMaskChannel, 3.0))] : 1.0;
		if (_MochieMetallicMapsReflectionMaskChannel < 4.0)
		{
			assignValueToVectorFromIndex(PBRMaps, _MochieMetallicMapsReflectionMaskChannel, reflVal);
		}
		if (_MochieMetallicMapsSpecularMaskChannel < 4.0)
		{
			assignValueToVectorFromIndex(PBRMaps, _MochieMetallicMapsSpecularMaskChannel, specVal);
		}
	}

	if (_MochieMetallicMapsMetallicChannel < 4.0)
	{
		metallic *= PBRMaps[int(min(_MochieMetallicMapsMetallicChannel, 3.0))];
	}
	if (_MochieMetallicMapsRoughnessChannel < 4.0)
	{
		smoothness *= PBRMaps[int(min(_MochieMetallicMapsRoughnessChannel, 3.0))];
		smoothness2 *= PBRMaps[int(min(_MochieMetallicMapsRoughnessChannel, 3.0))];
	}
	if (_MochieMetallicMapsReflectionMaskChannel < 4.0)
	{
		reflectionMask *= PBRMaps[int(min(_MochieMetallicMapsReflectionMaskChannel, 3.0))];
	}
	if (_MochieMetallicMapsSpecularMaskChannel < 4.0)
	{
		specularMask *= PBRMaps[int(min(_MochieMetallicMapsSpecularMaskChannel, 3.0))];
	}
#endif

	reflectionMask *= _MochieReflectionStrength;
	specularMask *= _MochieSpecularStrength;

	if (_MochieMetallicMapInvert >= 0.5)
	{
		metallic = 1.0 - metallic;
	}
	if (_MochieRoughnessMapInvert >= 0.5)
	{
		smoothness = 1.0 - smoothness;
		smoothness2 = 1.0 - smoothness2;
	}
	if (_MochieReflectionMaskInvert >= 0.5)
	{
		reflectionMask = 1.0 - reflectionMask;
	}
	if (_MochieSpecularMaskInvert >= 0.5)
	{
		specularMask = 1.0 - specularMask;
	}

	poiFragData.smoothness *= smoothness;
	poiFragData.smoothness2 *= smoothness2;
	poiFragData.metallic *= metallic;
	poiFragData.specularMask *= specularMask;
	poiFragData.reflectionMask *= reflectionMask;

	// specific metallics and specular data for mochie brdf

	mochieBRDFData.smoothness = poiFragData.smoothness;
	mochieBRDFData.smoothness2 = poiFragData.smoothness2;
	mochieBRDFData.metallic = poiFragData.metallic;
	mochieBRDFData.specularMask = poiFragData.specularMask;
	mochieBRDFData.reflectionMask = poiFragData.reflectionMask;

	vec2 adjustedAnisotropy = vec2(0.0);
#if defined(POI_MOCHIE_GGX_ANISO)
#if defined(PROP_ANISOTROPYMAP)
	vec4 anisotropyMap = poiTexPan(_AnisotropyMap, poiUV(poiMesh.uv[int(_AnisotropyMapUV)], _AnisotropyMap_ST), _AnisotropyMapPan);
	adjustedAnisotropy = vec2((anisotropyMap[int(_AnisotropyMapChannel)] - 0.5) * 2.0);
#endif
	adjustedAnisotropy = clamp(adjustedAnisotropy + _Anisotropy, -1.0, 1.0);
	adjustedAnisotropy.y = abs(adjustedAnisotropy.y);
#endif

	mochieBRDFData.adjustedAnisotropy = adjustedAnisotropy;

	vec3 pbrNSelect = mix(poiMesh.normals[0], poiMesh.normals[1], _PBRNormalSelect);
	vec3 pbrTSelect = mix(poiMesh.tangent[0], poiMesh.tangent[1], _PBRNormalSelect);
	vec3 pbrBSelect = mix(poiMesh.binormal[0], poiMesh.binormal[1], _PBRNormalSelect);

	mochieBRDFData.pbrNSelect = pbrNSelect;
	mochieBRDFData.pbrTSelect = pbrTSelect;
	mochieBRDFData.pbrBSelect = pbrBSelect;
}

void FinalizeMochieBRDFData(inout MochieBRDFData mochieBRDFData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	if (mochieBRDFData.isInitialized >= 0.5)
	{
		return;
	}
	mochieBRDFData.isInitialized = 1.0;

	if (_MochieMetallicGlobalMask > 0.0)
	{
		mochieBRDFData.metallic = maskBlend(mochieBRDFData.metallic, poiMods.globalMask[int(_MochieMetallicGlobalMask) - 1], _MochieMetallicGlobalMaskBlendType);
	}
	if (_MochieSmoothnessGlobalMask > 0.0)
	{
		mochieBRDFData.smoothness = maskBlend(mochieBRDFData.smoothness, poiMods.globalMask[int(_MochieSmoothnessGlobalMask) - 1], _MochieSmoothnessGlobalMaskBlendType);
		mochieBRDFData.smoothness2 = maskBlend(mochieBRDFData.smoothness2, poiMods.globalMask[int(_MochieSmoothnessGlobalMask) - 1], _MochieSmoothnessGlobalMaskBlendType);
	}
	if (_MochieReflectionStrengthGlobalMask > 0.0)
	{
		mochieBRDFData.reflectionMask = customBlend(mochieBRDFData.reflectionMask, poiMods.globalMask[int(_MochieReflectionStrengthGlobalMask) - 1], _MochieReflectionStrengthGlobalMaskBlendType);
	}
	if (_MochieSpecularStrengthGlobalMask > 0.0)
	{
		mochieBRDFData.specularMask = customBlend(mochieBRDFData.specularMask, poiMods.globalMask[int(_MochieSpecularStrengthGlobalMask) - 1], _MochieSpecularStrengthGlobalMaskBlendType);
	}

	//ifex _RGBMaskEnabled==0
#if defined(POI_RGBMASK)
	// Apply RGBA Masking PBR values to mochieBRDFData
	if (_RGBAPBRRedEnabled >= 0.5 || _RGBAPBRGreenEnabled >= 0.5 || _RGBAPBRBlueEnabled >= 0.5 || _RGBAPBRAlphaEnabled >= 0.5)
	{
		vec4 rgbMaskPBR = vec4(1.0);

#if defined(PROP_RGBMASK)
		if (_RGBMaskType == 0.0)
		{
			rgbMaskPBR = poiTexPan(_RGBMask, poiUV(poiMesh.uv[int(_RGBMaskUV)], _RGBMask_ST), _RGBMaskPan);
		}
#endif

		if (_RGBMaskType == 1.0)
		{
			rgbMaskPBR = poiMesh.vertexColor;
		}

		float redPBR = float(_RGBAPBRRedEnabled >= 0.5 && _RGBARedEnable >= 0.5);
		float greenPBR = float(_RGBAPBRGreenEnabled >= 0.5 && _RGBAGreenEnable >= 0.5);
		float bluePBR = float(_RGBAPBRBlueEnabled >= 0.5 && _RGBABlueEnable >= 0.5);
		float alphaPBR = float(_RGBAPBRAlphaEnabled >= 0.5 && _RGBAAlphaEnable >= 0.5);

#if defined(PROP_RGBASMOOTHNESSMAPS)
		vec4 smoothnessMaps = vec4(1.0);
		if (_RGBARedPBRSplitMaskSample < 0.5 || _RGBAGreenPBRSplitMaskSample < 0.5 || _RGBABluePBRSplitMaskSample < 0.5 || _RGBAAlphaPBRSplitMaskSample < 0.5)
		{
			smoothnessMaps = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBASmoothnessMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBASmoothnessMapsUV)], _RGBASmoothnessMaps_ST), _RGBASmoothnessMapsPan.xy, _RGBASmoothnessMapsStochastic);
		}

		if (_RGBARedPBRSplitMaskSample >= 0.5 && redPBR > 0.5)
		{
			smoothnessMaps.r = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBASmoothnessMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBARedPBRUV)], PoiUVMerge(_RGBARedPBRMaskScaleTiling, _RGBARedScaleOffset)), _RGBARedPBRMasksPan.xy, _RGBARedPBRSplitMaskStochastic).r;
		}
		if (_RGBAGreenPBRSplitMaskSample >= 0.5 && greenPBR > 0.5)
		{
			smoothnessMaps.g = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBASmoothnessMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBAGreenPBRUV)], PoiUVMerge(_RGBAGreenPBRMaskScaleTiling, _RGBAGreenScaleOffset)), _RGBAGreenPBRMasksPan.xy, _RGBAGreenPBRSplitMaskStochastic).g;
		}
		if (_RGBABluePBRSplitMaskSample >= 0.5 && bluePBR > 0.5)
		{
			smoothnessMaps.b = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBASmoothnessMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBABluePBRUV)], PoiUVMerge(_RGBABluePBRMaskScaleTiling, _RGBABlueScaleOffset)), _RGBABluePBRMasksPan.xy, _RGBABluePBRSplitMaskStochastic).b;
		}
		if (_RGBAAlphaPBRSplitMaskSample >= 0.5 && alphaPBR > 0.5)
		{
			smoothnessMaps.a = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBASmoothnessMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBAAlphaPBRUV)], PoiUVMerge(_RGBAAlphaPBRMaskScaleTiling, _RGBAAlphaScaleOffset)), _RGBAAlphaPBRMasksPan.xy, _RGBAAlphaPBRSplitMaskStochastic).a;
		}

		mochieBRDFData.smoothness = mix(mochieBRDFData.smoothness, poiInvertToggle(smoothnessMaps.r, _RGBARedSmoothnessInvert), rgbMaskPBR[int(_RgbRedMaskChannel)] * redPBR);
		mochieBRDFData.smoothness = mix(mochieBRDFData.smoothness, poiInvertToggle(smoothnessMaps.g, _RGBAGreenSmoothnessInvert), rgbMaskPBR[int(_RgbGreenMaskChannel)] * greenPBR);
		mochieBRDFData.smoothness = mix(mochieBRDFData.smoothness, poiInvertToggle(smoothnessMaps.b, _RGBABlueSmoothnessInvert), rgbMaskPBR[int(_RgbBlueMaskChannel)] * bluePBR);
		mochieBRDFData.smoothness = mix(mochieBRDFData.smoothness, poiInvertToggle(smoothnessMaps.a, _RGBAAlphaSmoothnessInvert), rgbMaskPBR[int(_RgbAlphaMaskChannel)] * alphaPBR);
#endif

#if defined(PROP_RGBAMETALLICMAPS)
		vec4 metallicMaps = vec4(1.0);
		if (_RGBARedPBRSplitMaskSample < 0.5 || _RGBAGreenPBRSplitMaskSample < 0.5 || _RGBABluePBRSplitMaskSample < 0.5 || _RGBAAlphaPBRSplitMaskSample < 0.5)
		{
			metallicMaps = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBAMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBAMetallicMapsUV)], _RGBAMetallicMaps_ST), _RGBAMetallicMapsPan.xy, _RGBAMetallicMapsStochastic);
		}

		if (_RGBARedPBRSplitMaskSample >= 0.5 && redPBR > 0.5)
		{
			metallicMaps.r = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBAMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBARedPBRUV)], PoiUVMerge(_RGBARedPBRMaskScaleTiling, _RGBARedScaleOffset)), _RGBARedPBRMasksPan.xy, _RGBARedPBRSplitMaskStochastic).r;
		}
		if (_RGBAGreenPBRSplitMaskSample >= 0.5 && greenPBR > 0.5)
		{
			metallicMaps.g = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBAMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBAGreenPBRUV)], PoiUVMerge(_RGBAGreenPBRMaskScaleTiling, _RGBAGreenScaleOffset)), _RGBAGreenPBRMasksPan.xy, _RGBAGreenPBRSplitMaskStochastic).g;
		}
		if (_RGBABluePBRSplitMaskSample >= 0.5 && bluePBR > 0.5)
		{
			metallicMaps.b = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBAMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBABluePBRUV)], PoiUVMerge(_RGBABluePBRMaskScaleTiling, _RGBABlueScaleOffset)), _RGBABluePBRMasksPan.xy, _RGBABluePBRSplitMaskStochastic).b;
		}
		if (_RGBAAlphaPBRSplitMaskSample >= 0.5 && alphaPBR > 0.5)
		{
			metallicMaps.a = POI2D_SAMPLER_PAN_STOCHASTIC(_RGBAMetallicMaps, _MainTex, poiUV(poiMesh.uv[int(_RGBAAlphaPBRUV)], PoiUVMerge(_RGBAAlphaPBRMaskScaleTiling, _RGBAAlphaScaleOffset)), _RGBAAlphaPBRMasksPan.xy, _RGBAAlphaPBRSplitMaskStochastic).a;
		}

		mochieBRDFData.metallic = mix(mochieBRDFData.metallic, poiInvertToggle(metallicMaps.r, _RGBARedMetallicInvert), rgbMaskPBR[int(_RgbRedMaskChannel)] * redPBR);
		mochieBRDFData.metallic = mix(mochieBRDFData.metallic, poiInvertToggle(metallicMaps.g, _RGBAGreenMetallicInvert), rgbMaskPBR[int(_RgbGreenMaskChannel)] * greenPBR);
		mochieBRDFData.metallic = mix(mochieBRDFData.metallic, poiInvertToggle(metallicMaps.b, _RGBABlueMetallicInvert), rgbMaskPBR[int(_RgbBlueMaskChannel)] * bluePBR);
		mochieBRDFData.metallic = mix(mochieBRDFData.metallic, poiInvertToggle(metallicMaps.a, _RGBAAlphaMetallicInvert), rgbMaskPBR[int(_RgbAlphaMaskChannel)] * alphaPBR);
#endif
	}
#endif
	//endex

	float roughness = GetRoughness(mochieBRDFData.smoothness);

	float omr = POI_COLOR_SPACE_DIELECTRIC_SPEC.a - mochieBRDFData.metallic * POI_COLOR_SPACE_DIELECTRIC_SPEC.a;
	float percepRough = 1.0 - mochieBRDFData.smoothness;
	float percepRough2 = 1.0 - mochieBRDFData.smoothness2;
	if (_MochieGSAAEnabled >= 0.5)
	{
		vec3 normals = mix(poiMesh.normals[0], poiMesh.normals[1], _PBRNormalSelect);
		percepRough = GSAA_Filament(normals, percepRough, _PoiGSAAVariance, _PoiGSAAThreshold);
		if (_Specular2ndLayer >= 0.5 && _MochieSpecularStrength2 > 0.0)
		{
			percepRough2 = GSAA_Filament(normals, percepRough2, _PoiGSAAVariance, _PoiGSAAThreshold);
		}
	}
	float brdfRoughness = percepRough * percepRough;
	brdfRoughness = max(brdfRoughness, 0.002);

	float brdfRoughness2 = percepRough2 * percepRough2;
	brdfRoughness2 = max(brdfRoughness2, 0.002);

	mochieBRDFData.roughness = roughness;
	mochieBRDFData.brdfRoughness = brdfRoughness;
	mochieBRDFData.brdfRoughness2 = brdfRoughness2;
	mochieBRDFData.percepRough = percepRough;
	mochieBRDFData.percepRough2 = percepRough2;
	mochieBRDFData.omr = omr;
}

// Per light.
void MochieBRDFSpecular(inout PoiFragData poiFragData, inout MochieBRDFData mochieBRDFData, in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, inout PoiMods poiMods)
{
	FinalizeMochieBRDFData(mochieBRDFData, poiMesh, poiMods);

	float specularMask = mochieBRDFData.specularMask;
	vec3 specTint = poiThemeColor(poiMods, _MochieSpecularTint.rgb, _MochieSpecularTintThemeIndex);

	vec3 specCol = mix(POI_COLOR_SPACE_DIELECTRIC_SPEC.rgb, poiFragData.baseColor, mochieBRDFData.metallic);
	mochieBRDFData.specCol = specCol;

	float brdfRoughness = mochieBRDFData.brdfRoughness;
	float brdfRoughness2 = mochieBRDFData.brdfRoughness2;

	vec3 specular = vec3(0.0);
	vec3 specular2 = vec3(0.0);
	vec3 environment = vec3(0.0);

	// In the additive pass directColor already carries atten
	float specLightMap = poiLight.lightMap;
	float attenuation = mix(specLightMap, 1.0, _IgnoreCastedShadows);

	vec3 fresnelTerm = vec3(1.0);
	vec3 specularTerm = vec3(1.0);

	float pbrNDotL = mix(poiLight.vertexNDotL, poiLight.nDotL, _PBRNormalSelect);
	float pbrNDotV = mix(poiLight.vertexNDotV, poiLight.nDotV, _PBRNormalSelect);
	float pbrNDotH = mix(poiLight.vertexNDotH, poiLight.nDotH, _PBRNormalSelect);

	vec3 pbrNSelect = mochieBRDFData.pbrNSelect;
	vec3 pbrTSelect = mochieBRDFData.pbrTSelect;
	vec3 pbrBSelect = mochieBRDFData.pbrBSelect;

	float pbrTDotV = dot(pbrTSelect, poiCam.eyeViewDir);
	float pbrBDotV = dot(pbrBSelect, poiCam.eyeViewDir);
	float pbrTDotL = dot(pbrTSelect, poiLight.direction);
	float pbrBDotL = dot(pbrBSelect, poiLight.direction);
	float pbrTDotH = dot(pbrTSelect, poiLight.halfDir);
	float pbrBDotH = dot(pbrBSelect, poiLight.halfDir);

#if defined(POI_MOCHIE_GGX_ANISO)
	vec2 adjustedAnisotropy = mochieBRDFData.adjustedAnisotropy;
	GetSpecFresTerm(pbrNDotL, pbrNDotV, pbrNDotH, poiLight.lDotH, specularTerm, fresnelTerm, specCol, brdfRoughness, pbrTDotV, pbrBDotV, pbrTDotL, pbrBDotL, pbrTDotH, pbrBDotH, adjustedAnisotropy);
#else
	GetSpecFresTerm(pbrNDotL, pbrNDotV, pbrNDotH, poiLight.lDotH, specularTerm, fresnelTerm, specCol, brdfRoughness);
#endif
	float specOcclusion = poiLight.occlusion;
#if defined(POI_BENTNORMALMAP)
	if (poiMesh.hasBentNormal > 0.5)
	{
		vec3 specReflDir = reflect(-poiCam.eyeViewDir, mix(poiMesh.normals[0], poiMesh.normals[1], _PBRNormalSelect));
		specOcclusion = mix(poiLight.occlusion, PoiSpecularOcclusionBentNormal(specReflDir, poiMesh.bentNormal, poiLight.occlusion, mochieBRDFData.percepRough), _BentNormalSpecOccStrength);
	}
#endif
	specular = poiLight.directColor * specularTerm * fresnelTerm * specularMask * specTint * specOcclusion * attenuation;

	if (_Specular2ndLayer >= 0.5)
	{
		vec3 fresnelTerm2 = vec3(1.0);
		vec3 specularTerm2 = vec3(1.0);
#if defined(POI_MOCHIE_GGX_ANISO)
		GetSpecFresTerm(pbrNDotL, pbrNDotV, pbrNDotH, poiLight.lDotH, specularTerm2, fresnelTerm2, specCol, brdfRoughness2, pbrTDotV, pbrBDotV, pbrTDotL, pbrBDotL, pbrTDotH, pbrBDotH, adjustedAnisotropy);
#else
		GetSpecFresTerm(pbrNDotL, pbrNDotV, pbrNDotH, poiLight.lDotH, specularTerm2, fresnelTerm2, specCol, brdfRoughness2);
#endif
		specular2 = poiLight.directColor * specularTerm2 * fresnelTerm2 * specularMask * specTint * specOcclusion * attenuation * _MochieSpecularStrength2;
	}
	environment = max(specular, specular2);
	poiLight.finalLightAdd += environment;
}

// The probe direction, bent along the anisotropy as the source bends it.
vec3 MochieReflectionDir(in MochieBRDFData mochieBRDFData, in PoiCam poiCam)
{
	vec3 pbrReflectionDir = mix(poiCam.vertexReflectionDir, poiCam.reflectionDir, _PBRNormalSelect);
#if defined(POI_MOCHIE_GGX_ANISO)
	float roughness = mochieBRDFData.roughness;
	vec2 adjustedAnisotropy = mochieBRDFData.adjustedAnisotropy;
	vec3 pbrNSelect = mochieBRDFData.pbrNSelect;
	vec3 pbrTSelect = mochieBRDFData.pbrTSelect;
	vec3 pbrBSelect = mochieBRDFData.pbrBSelect;

	pbrReflectionDir = vec3(dot(pbrReflectionDir, pbrTSelect), dot(pbrReflectionDir, pbrBSelect), dot(pbrReflectionDir, pbrNSelect));
	float pbrReflRoughness = mix(1.0, roughness, _RoughnessAnisotropy);
	pbrReflectionDir.xy *= vec2((1.0 - roughness * adjustedAnisotropy.y) * (1.0 - pbrReflRoughness * adjustedAnisotropy.x * (_ReflectionAnisotropicStretch * adjustedAnisotropy.y)), (1.0 - roughness * adjustedAnisotropy.y) * (1.0 + pbrReflRoughness * adjustedAnisotropy.x * (_ReflectionAnisotropicStretch * adjustedAnisotropy.y)));
	pbrReflectionDir = normalize(pbrReflectionDir.x * pbrTSelect + pbrReflectionDir.y * pbrBSelect + pbrReflectionDir.z * pbrNSelect);
#endif
	return pbrReflectionDir;
}

// The reflection's occlusion: the source's AO times the bent normal ratio.
float MochieReflectionOcclusion(in MochieBRDFData mochieBRDFData, in PoiMesh poiMesh, in PoiLight poiLight, vec3 pbrReflectionDir)
{
	float occlusion = poiLight.occlusion;
#if defined(POI_BENTNORMALMAP)
	if (poiMesh.hasBentNormal > 0.5)
	{
		float bentNormalSO = PoiSpecularOcclusionBentNormal(pbrReflectionDir, poiMesh.bentNormal, poiLight.occlusion, mochieBRDFData.percepRough);
		float occlusionRatio = (poiLight.occlusion > 0.001) ? (bentNormalSO / poiLight.occlusion) : bentNormalSO;
		occlusion *= mix(1.0, occlusionRatio, _BentNormalSpecOccStrength);
	}
#endif
	return occlusion;
}

// The source's reflections for a reflection color reflCol: its fresnel, exposure occlusion, tint, occlusion
// and mask. For the parts the shader samples itself (a lit fallback cubemap per light).
vec3 MochieReflection(in PoiFragData poiFragData, in MochieBRDFData mochieBRDFData, in PoiMesh poiMesh, in PoiCam poiCam, in PoiLight poiLight, in PoiMods poiMods, vec3 reflCol, vec3 pbrReflectionDir)
{
	float brdfRoughness = mochieBRDFData.brdfRoughness;
	vec3 specCol = mix(POI_COLOR_SPACE_DIELECTRIC_SPEC.rgb, poiFragData.baseColor, mochieBRDFData.metallic);
	float pbrNDotV = mix(poiLight.vertexNDotV, poiLight.nDotV, _PBRNormalSelect);
	float surfaceReduction = (1.0 / (brdfRoughness * brdfRoughness + 1.0));
	vec3 reflections = surfaceReduction * reflCol * FresnelLerp(specCol, specCol + mix(specCol, vec3(1.0), _RefSpecFresnelStrength) * _RefSpecFresnelStrength, pbrNDotV) * SFVisibility(brdfRoughness, poiLight.directColor, pbrNDotV, _SFExposureOcclusion);
	reflections *= poiThemeColor(poiMods, _MochieReflectionTint.rgb, _MochieReflectionTintThemeIndex);
	reflections *= MochieReflectionOcclusion(mochieBRDFData, poiMesh, poiLight, pbrReflectionDir);
	reflections *= mochieBRDFData.reflectionMask;
	return reflections;
}

// fragment(), where the source composes its reflections. The reflections come from the engine's
// environment (MochieBRDFReflectionOutputs) and, for a lit fallback cubemap, each light's share
// (applyReflectionsLight). The metallic darkening of the lit part is the engine's 1 - METALLIC; the unlit part
// takes it here. Without shading the lit part is emitted, so its darkening is taken out of the unlit part.
void MochieBRDFMetallics(inout PoiFragData poiFragData, inout MochieBRDFData mochieBRDFData, in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, inout PoiMods poiMods)
{
#if !defined(POI_PASS_OUTLINE)
	FinalizeMochieBRDFData(mochieBRDFData, poiMesh, poiMods);

	// Apply metallic diffuse darkening
	poiFragData.finalColor = mix(poiFragData.finalColor, poiFragData.finalColor * mochieBRDFData.omr, mochieBRDFData.reflectionMask);
#if !defined(POI_SHADING)
	poiFragData.finalColor -= poiFragData.baseColor * (1.0 - mix(1.0, mochieBRDFData.omr, mochieBRDFData.reflectionMask));
#endif

	// Correct specular/reflections for baseColor changes after specCol was computed
	// (e.g., effects like matcap or decals that modify baseColor post-specular)
	if (any(greaterThan(mochieBRDFData.specCol, vec3(0.0))))
	{
		vec3 currentSpecCol = mix(POI_COLOR_SPACE_DIELECTRIC_SPEC.rgb, poiFragData.baseColor, mochieBRDFData.metallic);
		vec3 safeSpecCol = max(mochieBRDFData.specCol, POI_COLOR_SPACE_DIELECTRIC_SPEC.rgb);
		poiLight.finalLightAdd *= mix(vec3(1.0), currentSpecCol / safeSpecCol, mochieBRDFData.reflectionMask);
	}
#endif
}

// The engine's environment inputs for Mochie's reflections (see the top of this file).
void MochieBRDFReflectionOutputs(inout PoiFragData poiFragData, inout MochieBRDFData mochieBRDFData, in PoiMesh poiMesh, in PoiCam poiCam, in PoiLight poiLight, in PoiMods poiMods, inout vec4 pbrOut, inout vec4 radianceOut)
{
	FinalizeMochieBRDFData(mochieBRDFData, poiMesh, poiMods);

	float metallic = clamp(mochieBRDFData.metallic, 0.0, 1.0);
	float reflectionMask = clamp(mochieBRDFData.reflectionMask, 0.0, 1.0);
	float engineMetallic = metallic * reflectionMask;
	float dielectricF0 = 0.04 * (1.0 - metallic) * reflectionMask;
	pbrOut.x = engineMetallic;
	pbrOut.y = mochieBRDFData.percepRough;
	pbrOut.z = sqrt(clamp(dielectricF0 / (0.16 * max(1.0 - engineMetallic, 1e-4)), 0.0, 1.0));

	vec3 pbrReflectionDir = MochieReflectionDir(mochieBRDFData, poiCam);
	float pbrNDotV = mix(poiLight.vertexNDotV, poiLight.nDotV, _PBRNormalSelect);
	float visibility = SFVisibility(mochieBRDFData.brdfRoughness, POI_FRAGMENT_EXPOSURE_COLOR(poiLight), pbrNDotV, _SFExposureOcclusion) * MochieReflectionOcclusion(mochieBRDFData, poiMesh, poiLight, pbrReflectionDir);
	vec3 tint = poiThemeColor(poiMods, _MochieReflectionTint.rgb, _MochieReflectionTintThemeIndex);

#if defined(PROP_MOCHIEREFLCUBE)
	if (_MochieForceFallback != 0.0)
	{
		// The main light's lit share goes per light (applyReflectionsLight); finalLighting here stands for the
		// ambient share.
		vec3 reflCol = SampleFallbackCube(_MochieReflCube, pbrReflectionDir, mochieBRDFData.percepRough) * mix(vec3(1.0), poiLight.finalLighting, _MochieLitFallback);
		radianceOut = vec4(reflCol * visibility * tint, 1.0);
		return;
	}
#endif
	radianceOut = vec4(0.0, 0.0, 0.0, 1.0 - clamp(visibility * CalculateLuminance(tint), 0.0, 1.0));
}
#endif
//endex

//ifex _ClearCoatBRDF==0
#if defined(POI_CLEARCOAT)
// The source's clear coat inputs after its maps, global masks and inverts.
void ClearCoatInputs(in PoiMesh poiMesh, in PoiMods poiMods, inout float clearCoatMask, inout float smoothness, inout float reflectionMask, inout float specularMask)
{
	clearCoatMask = _ClearCoatStrength;
	smoothness = _ClearCoatSmoothness;
	reflectionMask = _ClearCoatReflectionStrength;
	specularMask = _ClearCoatSpecularStrength;

#if defined(PROP_CLEARCOATMAPS)
	vec4 PBRMaps = POI2D_SAMPLER_PAN_STOCHASTIC(_ClearCoatMaps, _MainTex, poiUV(poiMesh.uv[int(_ClearCoatMapsUV)], _ClearCoatMaps_ST), _ClearCoatMapsPan, _ClearCoatMapsStochastic);

	if (_ClearCoatMapsClearCoatMaskChannel < 4.0)
	{
		clearCoatMask *= PBRMaps[int(min(_ClearCoatMapsClearCoatMaskChannel, 3.0))];
	}
	if (_ClearCoatMapsRoughnessChannel < 4.0)
	{
		smoothness *= PBRMaps[int(min(_ClearCoatMapsRoughnessChannel, 3.0))];
	}
	if (_ClearCoatMapsReflectionMaskChannel < 4.0)
	{
		reflectionMask *= PBRMaps[int(min(_ClearCoatMapsReflectionMaskChannel, 3.0))];
	}
	if (_ClearCoatMapsSpecularMaskChannel < 4.0)
	{
		specularMask *= PBRMaps[int(min(_ClearCoatMapsSpecularMaskChannel, 3.0))];
	}
#endif

	if (_ClearCoatGlobalMask > 0.0)
	{
		clearCoatMask = maskBlend(clearCoatMask, poiMods.globalMask[int(_ClearCoatGlobalMask) - 1], _ClearCoatGlobalMaskBlendType);
	}
	if (_ClearCoatSmoothnessGlobalMask > 0.0)
	{
		smoothness = maskBlend(smoothness, poiMods.globalMask[int(_ClearCoatSmoothnessGlobalMask) - 1], _ClearCoatSmoothnessGlobalMaskBlendType);
	}
	if (_ClearCoatReflectionStrengthGlobalMask > 0.0)
	{
		reflectionMask = maskBlend(reflectionMask, poiMods.globalMask[int(_ClearCoatReflectionStrengthGlobalMask) - 1], _ClearCoatReflectionStrengthGlobalMaskBlendType);
	}
	if (_ClearCoatSpecularStrengthGlobalMask > 0.0)
	{
		specularMask = maskBlend(specularMask, poiMods.globalMask[int(_ClearCoatSpecularStrengthGlobalMask) - 1], _ClearCoatSpecularStrengthGlobalMaskBlendType);
	}

	if (_ClearCoatMaskInvert >= 0.5)
	{
		clearCoatMask = 1.0 - clearCoatMask;
	}
	if (_ClearCoatSmoothnessMapInvert >= 0.5)
	{
		smoothness = 1.0 - smoothness;
	}
	if (_ClearCoatReflectionMaskInvert >= 0.5)
	{
		reflectionMask = 1.0 - reflectionMask;
	}
	if (_ClearCoatSpecularMaskInvert >= 0.5)
	{
		specularMask = 1.0 - specularMask;
	}
}

float ClearCoatPercepRough(in PoiMesh poiMesh, float smoothness)
{
	float percepRough = 1.0 - smoothness;
	if (_ClearCoatGSAAEnabled >= 0.5)
	{
		percepRough = GSAA_Filament(poiMesh.normals[int(_ClearCoatNormalSelect)], percepRough, _ClearCoatGSAAVariance, _ClearCoatGSAAThreshold);
	}
	return percepRough;
}

bool ClearCoatFallbackForced()
{
#if defined(PROP_CLEARCOATFALLBACK)
	return _ClearCoatForceFallback != 0.0;
#else
	return false;
#endif
}

// The source's clear coat reflections for a reflection color reflCol (fresnel, exposure occlusion, tint, mask).
// exposureColor is the light color the exposure occlusion reads.
vec3 ClearCoatReflection(in PoiMesh poiMesh, in PoiCam poiCam, in PoiLight poiLight, in PoiMods poiMods, vec3 reflCol, float percepRough, float reflectionMask, vec3 exposureColor)
{
	vec3 specCol = vec3(0.220916301);
	float brdfRoughness = max(percepRough * percepRough, 0.002);
	float clearcoatNDotV = mix(poiLight.vertexNDotV, poiLight.nDotV, _ClearCoatNormalSelect);
	float surfaceReduction = (1.0 / (brdfRoughness * brdfRoughness + 1.0));
	vec3 reflections = surfaceReduction * reflCol * FresnelLerp(specCol, specCol + mix(specCol, vec3(1.0), _ClearcoatFresnelStrength) * _ClearcoatFresnelStrength, clearcoatNDotV) * SFVisibility(brdfRoughness, exposureColor, clearcoatNDotV, _ClearcoatExposureOcclusion);
	reflections *= poiThemeColor(poiMods, _ClearCoatReflectionTint.rgb, _ClearCoatReflectionTintThemeIndex) * reflectionMask;
	return reflections;
}

// fragment(): the source's base pass adds the coat's probe reflection once, with the main light. Here that is
// the engine's clear coat lobe (applyClearCoatOutputs); a forced fallback cubemap is sampled here (its unlit
// and ambient share; each light adds its lit share in applyReflectionsLight).
void poiClearCoat(inout PoiFragData poiFragData, in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, in PoiMods poiMods)
{
#if !defined(POI_PASS_OUTLINE)
	float clearCoatMask = 1.0;
	float smoothness = 1.0;
	float reflectionMask = 1.0;
	float specularMask = 1.0;
	ClearCoatInputs(poiMesh, poiMods, clearCoatMask, smoothness, reflectionMask, specularMask);

	if (clearCoatMask < 0.001)
	{
		return;
	}

#if defined(PROP_CLEARCOATFALLBACK)
	if (ClearCoatFallbackForced())
	{
		float percepRough = ClearCoatPercepRough(poiMesh, smoothness);
		vec3 clearcoatReflectionDir = mix(poiCam.vertexReflectionDir, poiCam.reflectionDir, _ClearCoatNormalSelect);
		vec3 reflCol = SampleFallbackCube(_ClearCoatFallback, clearcoatReflectionDir, percepRough) * mix(vec3(1.0), poiLight.finalLighting, _ClearCoatLitFallback) * poiLight.occlusion;
		vec3 reflections = ClearCoatReflection(poiMesh, poiCam, poiLight, poiMods, reflCol, percepRough, reflectionMask, POI_FRAGMENT_EXPOSURE_COLOR(poiLight));
		poiLight.finalLightAdd += clamp(reflections * clearCoatMask, 0.0, 1.0);
	}
#endif
#endif
}

// Per light: the coat's specular, and for a directional light the lit share of a forced fallback cubemap.
void poiClearCoatLight(inout PoiFragData poiFragData, in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, in PoiMods poiMods)
{
	float clearCoatMask = 1.0;
	float smoothness = 1.0;
	float reflectionMask = 1.0;
	float specularMask = 1.0;
	ClearCoatInputs(poiMesh, poiMods, clearCoatMask, smoothness, reflectionMask, specularMask);

	if (clearCoatMask < 0.001)
	{
		return;
	}

	vec3 specCol = vec3(0.220916301);
	float percepRough = ClearCoatPercepRough(poiMesh, smoothness);
	float brdfRoughness = percepRough * percepRough;
	brdfRoughness = max(brdfRoughness, 0.002);

	vec3 specular = vec3(0.0);
	vec3 environment = vec3(0.0);
	float ccLightMap = poiLight.lightMap;
	float attenuation = min(poiLight.nDotLSaturated, mix(ccLightMap, 1.0, _CCIgnoreCastedShadows));

	vec3 fresnelTerm = vec3(1.0);
	vec3 specularTerm = vec3(1.0);

	float clearcoatNDotL = mix(poiLight.vertexNDotL, poiLight.nDotL, _ClearCoatNormalSelect);
	float clearcoatNDotV = mix(poiLight.vertexNDotV, poiLight.nDotV, _ClearCoatNormalSelect);
	float clearcoatNDotH = mix(poiLight.vertexNDotH, poiLight.nDotH, _ClearCoatNormalSelect);

	vec3 ccSpecTint = poiThemeColor(poiMods, _ClearCoatSpecularTint.rgb, _ClearCoatSpecularTintThemeIndex);
	GetSpecFresTerm(clearcoatNDotL, clearcoatNDotV, clearcoatNDotH, poiLight.lDotH, specularTerm, fresnelTerm, specCol, brdfRoughness);
	specular = poiLight.directColor * specularTerm * fresnelTerm * specularMask * ccSpecTint * poiLight.occlusion * attenuation;

	environment = specular;
#if defined(PROP_CLEARCOATFALLBACK)
	if (poiLight.isDirectional && ClearCoatFallbackForced())
	{
		vec3 clearcoatReflectionDir = mix(poiCam.vertexReflectionDir, poiCam.reflectionDir, _ClearCoatNormalSelect);
		vec3 reflCol = SampleFallbackCube(_ClearCoatFallback, clearcoatReflectionDir, percepRough) * poiLight.finalLighting * _ClearCoatLitFallback * poiLight.occlusion;
		environment += ClearCoatReflection(poiMesh, poiCam, poiLight, poiMods, reflCol, percepRough, reflectionMask, poiLight.directColor);
	}
#endif
	poiLight.finalLightAdd += clamp(environment * clearCoatMask, 0.0, 1.0);
}
#endif
//endex

//ifex _EnableEnvironmentalRim==0
#if defined(POI_ENVIRORIM)
// The source adds the probe, sampled along the normal, at the rim. The probe is the engine's: the rim is its
// clear coat lobe (applyClearCoatOutputs), so nothing is left for frag's call.
void applyEnvironmentRim(inout PoiFragData poiFragData, in PoiCam poiCam, in PoiMesh poiMesh)
{
}

// The rim's reflectance: the source's rim alpha, mask and intensity, with the base color's luminance for its
// tint.
float EnvironmentRimReflectance(in PoiFragData poiFragData, in PoiCam poiCam, in PoiMesh poiMesh)
{
	float enviroRimAlpha = clamp(1.0 - smoothstep(min(_RimEnviroSharpness, _RimEnviroWidth), _RimEnviroWidth, poiCam.vDotNCentered), 0.0, 1.0);
	float enviroMask = 1.0;
#if defined(PROP_RIMENVIROMASK)
	enviroMask = poiTexPan(_RimEnviroMask, poiMesh.uv[int(_RimEnviroMaskUV)], _RimEnviroMaskPan)[int(_RimEnviroChannel)];
#endif
	return enviroRimAlpha * enviroMask * _RimEnviroIntensity * CalculateLuminance(max(poiFragData.baseColor, vec3(0.0)));
}
#endif
//endex

#if (defined(POI_CLEARCOAT) || defined(POI_ENVIRORIM))
#define POI_HAS_CLEARCOAT_OUTPUTS
// fragment(), with applyReflectionOutputs: clearcoatOut = (CLEARCOAT, CLEARCOAT_ROUGHNESS). The engine's coat
// adds radiance * mix(0.04, 1, Schlick(geometric N.V)) * CLEARCOAT; CLEARCOAT is set so that product is the
// source's reflectance (capped at 1), and the roughness so the coat's mip matches the source's.
void applyClearCoatOutputs(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiLight poiLight, inout PoiMods poiMods, inout vec2 clearcoatOut)
{
#if !defined(POI_PASS_OUTLINE)
	float geoNDotV = max(dot(poiMesh.normals[0], poiCam.eyeViewDir), 0.0001);
	float engineCoatFresnel = mix(0.04, 1.0, Pow5(1.0 - geoNDotV));
	float coatReflectance = 0.0;
	float coatPercepRough = -1.0;

#if defined(POI_CLEARCOAT)
	float clearCoatMask = 1.0;
	float smoothness = 1.0;
	float reflectionMask = 1.0;
	float specularMask = 1.0;
	ClearCoatInputs(poiMesh, poiMods, clearCoatMask, smoothness, reflectionMask, specularMask);
	if (clearCoatMask >= 0.001 && !ClearCoatFallbackForced())
	{
		float percepRough = ClearCoatPercepRough(poiMesh, smoothness);
		vec3 reflections = ClearCoatReflection(poiMesh, poiCam, poiLight, poiMods, vec3(poiLight.occlusion), percepRough, reflectionMask, POI_FRAGMENT_EXPOSURE_COLOR(poiLight));
		coatReflectance += clamp(CalculateLuminance(reflections) * clearCoatMask, 0.0, 1.0);
		coatPercepRough = percepRough;
	}
#endif

#if defined(POI_ENVIRORIM)
	coatReflectance += EnvironmentRimReflectance(poiFragData, poiCam, poiMesh);
	if (coatPercepRough < 0.0)
	{
		coatPercepRough = _RimEnviroBlur * (1.7 - 0.7 * _RimEnviroBlur);
	}
#endif

	// The engine's coat picks its mip at sqrt(mix(0.001, 0.1, roughness)) of the chain; the source at the
	// perceptual roughness.
	float coatRoughness = clamp((coatPercepRough * coatPercepRough - 0.001) / 0.099, 0.0, 1.0);
	clearcoatOut = vec2(clamp(coatReflectance / engineCoatFresnel, 0.0, 1.0), coatRoughness);
#endif
}
#endif

#if !defined(POI_PASS_OUTLINE) && (defined(POI_CLEARCOAT) || (defined(POI_MOCHIE_BRDF) && defined(PROP_MOCHIEREFLCUBE)))
#define POI_HAS_LIGHT_HOOK_REFLECTIONS
// Per light: the clear coat's specular, and the lit share of a forced fallback cubemap (the source's add pass
// lights it with each light's finalLighting, its base pass with the main light's when Lit Fallback is on).
void applyReflectionsLight(inout PoiLight poiLight, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods)
{
#if defined(POI_MOCHIE_BRDF) && defined(PROP_MOCHIEREFLCUBE)
	if (_MochieForceFallback != 0.0)
	{
		MochieBRDFData mochieBRDFData = MochieBRDFData_zero();
		PoiFragData mochieInitData = poiFragData;
		mochieInitData.smoothness = 1.0;
		mochieInitData.smoothness2 = 1.0;
		mochieInitData.metallic = 1.0;
		mochieInitData.specularMask = 1.0;
		mochieInitData.reflectionMask = 1.0;
		MetallicAndSpecularFragDataInit(mochieInitData, mochieBRDFData, poiMesh, poiMods);
		FinalizeMochieBRDFData(mochieBRDFData, poiMesh, poiMods);

		vec3 pbrReflectionDir = MochieReflectionDir(mochieBRDFData, poiCam);
		float litShare = poiLight.isDirectional ? _MochieLitFallback : 1.0;
		vec3 reflCol = SampleFallbackCube(_MochieReflCube, pbrReflectionDir, mochieBRDFData.percepRough) * poiLight.finalLighting * litShare;
		poiLight.finalLightAdd += MochieReflection(poiFragData, mochieBRDFData, poiMesh, poiCam, poiLight, poiMods, reflCol, pbrReflectionDir);
	}
#endif
#if defined(POI_CLEARCOAT)
	poiClearCoatLight(poiFragData, poiCam, poiLight, poiMesh, poiMods);
#endif
}
#endif
