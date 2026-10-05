// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Poiyomi's shading: PoiCalculateShading for every lighting mode and the BRDF terms they use.
//
// The engine lights the surface as ALBEDO * (ambient * AO + sum of DIFFUSE_LIGHT), with
// ALBEDO = baseColor * ambientTint. Each mode's formula therefore runs twice:
// - in light(), once per engine light, with the light's data and no SH: finalLighting is that light's
//   share (DIFFUSE_LIGHT += finalLighting / ambientTint);
// - in fragment(), on the ambient state from PoiLightDataSetup (lightType 0): a unit ambient that reaches
//   no surface directly. Its finalLighting is how the mode shades the ambient where no light falls, and
//   becomes ambientTint (shadow colors, ramps, ignore-ambient, the color chain). The engine's AO carries
//   the occlusion, so the formula sees occlusion 1 there.
// Where the source reads the SH (wrapped, realistic, cloth), the ambient state stands in for it in
// fragment() and it is zero in light(). -xlinka

void PoiShadingGlobalMasks(inout PoiMods poiMods, in PoiLight poiLight)
{
	if (_ShadingRampedLightMapApplyGlobalMaskIndex > 0.0)
	{
		applyToGlobalMask(poiMods, _ShadingRampedLightMapApplyGlobalMaskIndex - 1.0, _ShadingRampedLightMapApplyGlobalMaskBlendType, clamp(poiMax(poiLight.rampedLightMap * _ShadingRampedLightMapApplyGlobalMaskStrength), 0.0, 1.0));
	}
	if (_ShadingRampedLightMapInverseApplyGlobalMaskIndex > 0.0)
	{
		applyToGlobalMask(poiMods, _ShadingRampedLightMapInverseApplyGlobalMaskIndex - 1.0, _ShadingRampedLightMapInverseApplyGlobalMaskBlendType, (1.0 - clamp(poiMax(poiLight.rampedLightMap * _ShadingRampedLightMapInverseApplyGlobalMaskStrength), 0.0, 1.0)));
	}
}

//ifex _ShadingEnabled==0
#if defined(POI_SHADING)

#if defined(POI_LIGHTING_CLOTH)
float V_SmithGGXCorrelated(float roughness, float NoV, float NoL)
{
	// Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"
	float a2 = roughness * roughness;
	float lambdaV = NoL * sqrt((NoV - a2 * NoV) * NoV + a2);
	float lambdaL = NoV * sqrt((NoL - a2 * NoL) * NoL + a2);
	float v = 0.5 / (lambdaV + lambdaL);
	return v;
}

float PoiD_GGX(float roughness, float NoH)
{
	// Walter et al. 2007, "Microfacet Models for Refraction through Rough Surfaces"
	float oneMinusNoHSquared = 1.0 - NoH * NoH;

	float a = NoH * roughness;
	float k = roughness / (oneMinusNoHSquared + a * a);
	float d = k * k * (1.0 / PI);
	return d;
}

// https://github.com/google/filament/blob/main/shaders/src/brdf.fs#L94-L100
float PoiD_Charlie(float roughness, float NoH)
{
	// Estevez and Kulla 2017, "Production Friendly Microfacet Sheen BRDF"
	float invAlpha = 1.0 / roughness;
	float cos2h = NoH * NoH;
	float sin2h = max(1.0 - cos2h, 0.0078125); // 0.0078125 = 2^(-14/2), so sin2h^2 > 0 in fp16
	return (2.0 + invAlpha) * pow(sin2h, invAlpha * 0.5) / (2.0 * PI);
}

// https://github.com/google/filament/blob/main/shaders/src/brdf.fs#L136-L139
float V_Neubelt(float NoV, float NoL)
{
	// Neubelt and Pettineo 2013, "Crafting a Next-gen Material Pipeline for The Order: 1886"
	return 1.0 / (4.0 * (NoL + NoV - NoL * NoV));
}

float Distribution(float roughness, float NoH, float cloth)
{
	//ifex _ClothLerp==0
	if (_ClothLerp >= 0.5)
	{
		return mix(GGXTerm(NoH, roughness), PoiD_Charlie(roughness, NoH), cloth);
	}
	//endex
	return cloth <= 0.5 ? GGXTerm(NoH, roughness) : PoiD_Charlie(roughness, NoH);
}

float Visibility(float roughness, float NoV, float NoL, float cloth)
{
	//ifex _ClothLerp==0
	if (_ClothLerp >= 0.5)
	{
		return mix(V_SmithGGXCorrelated(roughness, NoV, NoL), V_Neubelt(NoV, NoL), cloth);
	}
	//endex
	return cloth <= 0.5 ? V_SmithGGXCorrelated(roughness, NoV, NoL) : V_Neubelt(NoV, NoL);
}

vec3 PoiF_Schlick(vec3 f0, float f90, float VoH)
{
	// Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"
	return f0 + (f90 - f0) * pow(1.0 - VoH, 5.0);
}

float PoiF_Schlick(vec3 f0, float VoH)
{
	float f = pow(1.0 - VoH, 5.0);
	return (f + f0 * (1.0 - f)).x;
}

vec3 Fresnel(vec3 f0, float LoH)
{
	float f90 = clamp(dot(f0, vec3(50.0 * 0.33)), 0.0, 1.0);
	return PoiF_Schlick(f0, f90, LoH);
}

float Fd_Burley(float roughness, float NoV, float NoL, float LoH)
{
	// Burley 2012, "Physically-Based Shading at Disney"
	float f90 = 0.5 + 2.0 * roughness * LoH * LoH;
	float lightScatter = PoiF_Schlick(vec3(1.0), f90, NoL).x;
	float viewScatter = PoiF_Schlick(vec3(1.0), f90, NoV).x;
	return lightScatter * viewScatter;
}

// Energy conserving wrap diffuse term, does *not* include the divide by PI
float Fd_Wrap(float NoL, float w)
{
	return clamp((NoL + w) / pow(1.0 + w, 2.0), 0.0, 1.0);
}

// A lookup table: sampled at its top mip so the view-dependent coordinate picks no mip level.
vec4 SampleDFG(float NoV, float perceptualRoughness)
{
	return poiTexLod(_ClothDFG, vec2(NoV, perceptualRoughness), 0.0);
}

vec3 EnvBRDF(vec2 dfg, vec3 f0)
{
	return f0 * dfg.x + dfg.y;
}

vec3 EnvBRDFMultiscatter(vec3 dfg, vec3 f0, float cloth)
{
	//ifex _ClothLerp==0
	if (_ClothLerp >= 0.5)
	{
		return mix(mix(dfg.xxx, dfg.yyy, f0), f0 * dfg.z, cloth);
	}
	//endex
	return cloth <= 0.5 ? mix(dfg.xxx, dfg.yyy, f0) : f0 * dfg.z;
}

vec3 EnvBRDFEnergyCompensation(vec3 dfg, vec3 f0, float cloth)
{
	//ifex _ClothLerp==0
	if (_ClothLerp >= 0.5)
	{
		return mix(1.0 + f0 * (1.0 / dfg.y - 1.0), vec3(1.0), cloth);
	}
	//endex
	return cloth <= 0.5 ? 1.0 + f0 * (1.0 / dfg.y - 1.0) : vec3(1.0);
}

vec3 Specular(float roughness, PoiLight poiLight, vec3 f0, vec3 normal, float cloth)
{
	float NoL = poiLight.nDotLSaturated;
	float NoH = poiLight.nDotH;
	float LoH = poiLight.lDotH;
	float NoV = poiLight.nDotV;

	float D = Distribution(roughness, NoH, cloth);
	float V = Visibility(roughness, NoV, NoL, cloth);
	vec3 F = Fresnel(f0, LoH);

	return (D * V) * F;
}

// The cloth map's metallic, roughness, reflectance and cloth mask, as PoiCalculateShading reads them.
vec4 poiClothSurface(in PoiMesh poiMesh)
{
#if defined(PROP_CLOTHMETALLICSMOOTHNESSMAP)
	vec4 clothmapsample = poiTexPan(_ClothMetallicSmoothnessMap, poiUV(poiMesh.uv[int(_ClothMetallicSmoothnessMapUV)], _ClothMetallicSmoothnessMap_ST), _ClothMetallicSmoothnessMapPan);
	float roughness = 1.0 - (clothmapsample.a * _ClothSmoothness);
	float reflectance = _ClothReflectance * clothmapsample.b;
	float clothmask = clothmapsample.g;
	float metallic = pow(clothmapsample.r * _ClothMetallic, 2.0);
	roughness = _ClothMetallicSmoothnessMapInvert >= 0.5 ? 1.0 - roughness : roughness;
#else
	float roughness = 1.0 - (_ClothSmoothness);
	float metallic = pow(_ClothMetallic, 2.0);
	float reflectance = _ClothReflectance;
	float clothmask = 1.0;
#endif
	return vec4(metallic, roughness, reflectance, clothmask);
}

// The cloth's reflection probe term (IndirectSpecular) becomes the engine's environment specular: FRAG
// writes ROUGHNESS and SPECULAR from pbrOut (y, z) in cloth mode. The engine's 0.16 * SPECULAR^2 is the
// source's dielectric f0; METALLIC stays 0 (the metallic scale is in ambientTint and the light shares).
void PoiClothReflectionOutputs(in PoiMesh poiMesh, inout vec4 pbrOut)
{
	vec4 cloth = poiClothSurface(poiMesh);
	pbrOut = vec4(0.0, cloth.y, cloth.z, pbrOut.w);
}
#endif

#if defined(POI_LIGHTING_WRAPPED)
// Wrapped
// Green's model with adjustable energy
// http://blog.stevemcauley.com/2011/12/03/energy-conserving-wrapped-diffuse/
// Modified for adjustable conservation ratio and over-wrap to directionless
float RTWrapFunc(in float dt, in float w, in float norm)
{
	float cw = clamp(w, 0.0, 1.0);

	float o = (dt + cw) / ((1.0 + cw) * (1.0 + cw * norm));
	float flt = 1.0 - 0.85 * norm;
	if (w > 1.0)
	{
		o = mix(o, flt, w - 1.0);
	}
	return o;
}

vec3 GreenWrapSH(float fA) // Greens unoptimized and non-normalized
{
	float fAs = clamp(fA, 0.0, 1.0);
	vec4 t = vec4(fA + 1.0, fAs - 1.0, fA - 2.0, fAs + 1.0); // DJL edit: allow wrapping to L0-only at w=2
	return vec3(t.x, -t.z * t.x / 3.0, 0.25 * t.y * t.y * t.w);
}

vec3 GreenWrapSHOpt(float fW) // optimised and normalized https://blog.selfshadow.com/2012/01/07/righting-wrap-part-2/
{
	const vec4 t0 = vec4(0.0, 1.0 / 4.0, -1.0 / 3.0, -1.0 / 2.0);
	const vec4 t1 = vec4(1.0, 2.0 / 3.0, 1.0 / 4.0, 0.0);
	vec3 fWs = vec3(fW, fW, clamp(fW, 0.0, 1.0)); // DJL edit: allow wrapping to L0-only at w=2

	vec3 r;
	r.xyz = t0.xxy * fWs + t0.xzw;
	r.xyz = r.xyz * fWs + t1.xyz;
	return r;
}

// ShadeSH9_wrapped's constant band weight: the engine's ambient carries its own directional terms, so
// only the wrap's scale of L0 reaches the ambient.
float poiWrappedSHScaleL0(float wrap)
{
	vec3 conv = mix(GreenWrapSH(wrap), GreenWrapSHOpt(wrap), _LightingWrappedNormalization);
	conv *= vec3(1.0, 1.5, 4.0); // Undo pre-applied cosine convolution by using the inverse
	return conv.x;
}
#endif

#if defined(POI_LIGHTING_SHADEMAP)
// applyShadeMapping's color for the light map in poiLight; mainShadowMask is the source's.
vec3 poiShadeMapColor(in PoiFragData poiFragData, in PoiMesh poiMesh, in PoiLight poiLight, out float mainShadowMask)
{
	float MainColorFeatherStep = _BaseColor_Step - _BaseShade_Feather;
	float firstColorFeatherStep = _ShadeColor_Step - _1st2nd_Shades_Feather;

#if defined(PROP_1ST_SHADEMAP)
	vec4 firstShadeMap = poiTexPan(_1st_ShadeMap, poiUV(poiMesh.uv[int(_1st_ShadeMapUV)], _1st_ShadeMap_ST), _1st_ShadeMapPan);
#else
	vec4 firstShadeMap = vec4(1.0, 1.0, 1.0, 1.0);
#endif
	firstShadeMap = mix(firstShadeMap, vec4(poiFragData.baseColor, 1.0), _Use_BaseAs1st);

#if defined(PROP_2ND_SHADEMAP)
	vec4 secondShadeMap = poiTexPan(_2nd_ShadeMap, poiUV(poiMesh.uv[int(_2nd_ShadeMapUV)], _2nd_ShadeMap_ST), _2nd_ShadeMapPan);
#else
	vec4 secondShadeMap = vec4(1.0, 1.0, 1.0, 1.0);
#endif
	secondShadeMap = mix(secondShadeMap, firstShadeMap, _Use_1stAs2nd);

	firstShadeMap.rgb *= _1st_ShadeColor.rgb * poiLight.occlusion; //* lighColor
	secondShadeMap.rgb *= _2nd_ShadeColor.rgb * poiLight.occlusion; //* LightColor;

	float shadowMask = 1.0;
	shadowMask *= _Use_1stShadeMapAlpha_As_ShadowMask != 0.0 ? (_1stShadeMapMask_Inverse != 0.0 ? (1.0 - firstShadeMap.a) : firstShadeMap.a) : 1.0;
	shadowMask *= _Use_2ndShadeMapAlpha_As_ShadowMask != 0.0 ? (_2ndShadeMapMask_Inverse != 0.0 ? (1.0 - secondShadeMap.a) : secondShadeMap.a) : 1.0;

	mainShadowMask = clamp(1.0 - ((poiLight.lightMap) - MainColorFeatherStep) / (_BaseColor_Step - MainColorFeatherStep) * (shadowMask), 0.0, 1.0);
	float firstSecondShadowMask = clamp(1.0 - ((poiLight.lightMap) - firstColorFeatherStep) / (_ShadeColor_Step - firstColorFeatherStep) * (shadowMask), 0.0, 1.0);

	mainShadowMask *= poiLight.toonShadowMask;
	firstSecondShadowMask *= poiLight.toonShadowMask;

	vec3 shadedColor = vec3(0.0);
	// 0 lerp | 1 multiply
	if (_ShadingShadeMapBlendType == 0.0)
	{
		shadedColor = mix(poiFragData.baseColor.rgb, mix(firstShadeMap.rgb, secondShadeMap.rgb, firstSecondShadowMask), mainShadowMask);
	}
	else
	{
		shadedColor = poiFragData.baseColor.rgb * mix(vec3(1.0), mix(firstShadeMap.rgb, secondShadeMap.rgb, firstSecondShadowMask), mainShadowMask);
	}
	return mix(poiFragData.baseColor.rgb, shadedColor, _ShadowStrength);
}

// The engine lights ALBEDO, the unshaded base: a shaded base reaches it as a ratio.
vec3 poiShadeMapRatio(vec3 shadedColor, vec3 baseColor)
{
	return shadedColor / max(baseColor, vec3(1e-3));
}

// fragment(): the ambient state's shade tints the engine's ambient; baseColor stays unshaded, since every
// light shades it again in PoiCalculateShading. In light() this does nothing for the same reason.
void applyShadeMapping(inout PoiFragData poiFragData, PoiMesh poiMesh, inout PoiLight poiLight)
{
	if (poiLight.lightType != 0)
	{
		return;
	}
	float mainShadowMask = 0.0;
	vec3 shadedColor = poiShadeMapColor(poiFragData, poiMesh, poiLight, mainShadowMask);
	poiLight.ambientTint *= poiShadeMapRatio(shadedColor, poiFragData.baseColor);
	poiLight.rampedLightMap = vec3(1.0 - mainShadowMask * _ShadowStrength);
}
#endif

#if defined(POI_LIGHTING_REALISTIC)
// Unity's mobile BRDF (BRDF3). Its NHxRoughness lookup texture is the modified normalized Blinn-Phong
// term it tabulates, evaluated here.
struct PoiDirectLight
{
	vec3 color;
	vec3 dir;
	float ndotl;
};

struct PoiIndirectLight
{
	vec3 diffuse;
	vec3 specular;
};

float poiNHxRoughness(float rlPow4, float perceptualRoughness)
{
	float LUT_RANGE = 16.0;
	float nh = pow(clamp(rlPow4, 0.0, 1.0), 0.25);
	float roughness = perceptualRoughness * perceptualRoughness;
	float sq = max(1e-4, roughness * roughness);
	float n = max(2.0 / sq - 2.0, 1e-4);
	float normTerm = (n + 2.0) * (0.5 / PI);
	return clamp(pow(nh, n) * normTerm, 0.0, LUT_RANGE) / LUT_RANGE;
}

// fragment(): the ambient state's unit SH (occlusion rides the engine's AO); light(): no SH.
PoiIndirectLight CreateIndirectLight(in PoiMesh poiMesh, in PoiCam poiCam, in PoiLight poiLight)
{
	PoiIndirectLight indirectLight;
	indirectLight.diffuse = vec3(0.0);
	indirectLight.specular = vec3(0.0);

	if (poiLight.lightType == 0)
	{
		indirectLight.diffuse = vec3(1.0);
	}

	indirectLight.diffuse *= poiLight.occlusion;

	return indirectLight;
}

vec3 BRDF3_Direct(vec3 diffColor, vec3 specColor, float rlPow4, float smoothness)
{
	float LUT_RANGE = 16.0; // must match range in NHxRoughness() function in GeneratedTextures.cpp
	float specular = poiNHxRoughness(rlPow4, 1.0 - smoothness) * LUT_RANGE;

	return diffColor + specular * specColor;
}

vec3 BRDF3_Indirect(vec3 diffColor, vec3 specColor, PoiIndirectLight indirect, float grazingTerm, float fresnelTerm)
{
	vec3 c = indirect.diffuse * diffColor;
	c += indirect.specular * mix(specColor, vec3(grazingTerm), fresnelTerm);
	return c;
}

vec4 POI_BRDF_PBS(vec3 diffColor, vec3 specColor, float oneMinusReflectivity, float smoothness, vec3 normal, vec3 viewDir, float nDotVSaturated, float nDotLSaturated, PoiDirectLight directLight, PoiIndirectLight gi)
{
	vec3 reflDir = reflect(viewDir, normal);
	// Vectorize Pow4 to save instructions
	vec2 rlPow4AndFresnelTerm = Pow4(vec2(dot(reflDir, directLight.dir), 1.0 - nDotVSaturated));  // use R.L instead of N.H to save couple of instructions
	float rlPow4 = rlPow4AndFresnelTerm.x; // power exponent must match kHorizontalWarpExp in NHxRoughness() function in GeneratedTextures.cpp
	float fresnelTerm = rlPow4AndFresnelTerm.y;

	float grazingTerm = clamp(smoothness + (1.0 - oneMinusReflectivity), 0.0, 1.0);

	vec3 color = BRDF3_Direct(diffColor, specColor, rlPow4, smoothness);
	color *= directLight.color * nDotLSaturated;
	color += BRDF3_Indirect(diffColor, specColor, gi, grazingTerm, fresnelTerm);

	return vec4(color, 1.0);
}
#endif
#endif

float GetRemapMinValue(float scale, float offset)
{
	return clamp(-offset / scale, -0.01, 1.01); // Remap min
}

float GetRemapMaxValue(float scale, float offset)
{
	return clamp((1.0 - offset) / scale, -0.01, 1.01); // Remap Max
}

#if defined(POI_SHADING)
// See the file header for the two passes. The light's raw color (the source's unityLight.color) arrives in
// rampedLightMap from the light data setup.
void PoiCalculateShading(inout PoiLight poiLight, inout PoiFragData poiFragData, inout PoiMods poiMods, in PoiMesh poiMesh, in PoiCam poiCam)
{
	bool ambientPass = poiLight.lightType == 0;
	vec3 rawLightColor = poiLight.rampedLightMap;
	float poiOcclusion = poiLight.occlusion;
	if (ambientPass)
	{
		poiLight.occlusion = 1.0;
	}

	vec3 litColor = poiLight.directColor * poiLight.lightCookie;
	vec3 passthroughColor = poiLight.indirectColor * poiLight.lightCookie;
	vec3 ambientColor = poiLight.isAdditive != 0 ? passthroughColor : poiLight.indirectColor;
	float ignoreAmbientColor = poiLight.isAdditive != 0 ? 0.0 : _LightingIgnoreAmbientColor;

	if (poiLight.isAdditive != 0)
	{
		if (_LightingAdditiveType == 3.0)
		{
#if defined(POI_LIGHTING_REALISTIC) || defined(POI_LIGHTING_CLOTH) || defined(POI_LIGHTING_WRAPPED)
			poiLight.rampedLightMap = vec3(poiLight.nDotLSaturated);
			poiLight.rampedLightMap *= poiLight.attenDistance;
			poiLight.finalLighting = litColor * poiLight.nDotLSaturated * poiLight.detailShadow * poiLight.attenShadowsStrength;
			poiLight.finalLighting = max(poiLight.finalLighting, passthroughColor);
			return;
#endif
		}
		// Realistic
		if (_LightingAdditiveType == 0.0)
		{
			poiLight.rampedLightMap = vec3(poiLight.nDotLSaturated);
			poiLight.rampedLightMap *= poiLight.attenDistance;
			poiLight.finalLighting = litColor * poiLight.nDotLSaturated * poiLight.detailShadow * poiLight.attenShadowsStrength;
			poiLight.finalLighting = max(poiLight.finalLighting, passthroughColor);
			return;
		}
		// Toon
		if (_LightingAdditiveType == 1.0)
		{
			vec2 ToonAddGradient = vec2(_LightingAdditiveGradientStart, _LightingAdditiveGradientEnd);

			if (ToonAddGradient.x == ToonAddGradient.y)
			{
				ToonAddGradient.y += 0.0001;
			}

			poiLight.rampedLightMap = vec3(smoothstep(ToonAddGradient.y, ToonAddGradient.x, 1.0 - poiLight.nDotLNormalized));
			poiLight.rampedLightMap *= poiLight.attenDistance;
			poiLight.finalLighting = mix(litColor * poiLight.detailShadow * poiLight.attenShadowsStrength, passthroughColor, smoothstep(ToonAddGradient.x, ToonAddGradient.y, 1.0 - poiLight.nDotLNormalized));
			return;
		}
	}

	float shadowStrength = _ShadowStrength * poiLight.toonShadowMask;

#if defined(POI_PASS_OUTLINE)
	shadowStrength = mix(0.0, shadowStrength, _OutlineShadowStrength);
#endif

#if defined(POI_LIGHTING_FLAT)
	poiLight.finalLighting = litColor * poiLight.attenShadowsStrength;
	if (_ForceFlatRampedLightmap >= 0.5)
	{
		poiLight.rampedLightMap = vec3(smoothstep(0.4, 0.6, poiLight.nDotLNormalized));
	}
	else
	{
		poiLight.rampedLightMap = vec3(1.0);
	}
	if (poiLight.isAdditive != 0)
	{
		poiLight.finalLighting = mix(passthroughColor, poiLight.finalLighting, poiLight.rampedLightMap);
	}
#endif

#if defined(POI_LIGHTING_TEXTURERAMP)
	vec2 rampUVs = vec2(poiLight.lightMap + _ShadowOffset);
	if (_ToonRampCount > 1)
	{
		rampUVs.y = (floor(poiMesh.uv[_ToonRampUVSelector].y * float(_ToonRampCount)) + 0.5) / float(_ToonRampCount);
	}
	// Top mip: the ramp's sharp steps would blur with a mip picked from the light map's slope.
	poiLight.rampedLightMap = mix(vec3(1.0), poiTexLod(_ToonRamp, rampUVs, 0.0).rgb, shadowStrength);
	poiLight.finalLighting = mix(_LightingShadowColor * mix(ambientColor, poiLight.rampedLightMap * poiLight.directColor, ignoreAmbientColor) * poiLight.occlusion, poiLight.directColor, poiLight.rampedLightMap);
#endif

#if defined(POI_LIGHTING_MULTILAYER_MATH)
#if defined(PROP_MULTILAYERMATHBLURMAP)
	vec4 blurMap = poiTexPan(_MultilayerMathBlurMap, poiUV(poiMesh.uv[int(_MultilayerMathBlurMapUV)], _MultilayerMathBlurMap_ST), _MultilayerMathBlurMapPan);
#else
	vec4 blurMap = vec4(1.0);
#endif

	vec4 lns = vec4(1.0, 1.0, 1.0, 1.0);

	// This mode builds its own light map instead of ramping poiLight.lightMap
	float mlReceive = max(_ShadowReceive, max(_Shadow2ndReceive, _Shadow3rdReceive));
	float mlCastDepth = mix(0.44, 0.15, mlReceive); // lower is lighter shadow
	float mlCast = mix(1.0, exp2(-(1.0 - poiLight.attenShadows) * mlCastDepth), poiLight.attenStrength);
	vec3 mlLitColor = litColor * mlCast;

	vec3 N1 = poiMesh.normals[1];
	vec3 N2 = poiMesh.normals[1];
	vec3 N3 = poiMesh.normals[1];
	N1 = mix(poiMesh.normals[0], poiMesh.normals[1], _ShadowNormalStrength);
	N2 = mix(poiMesh.normals[0], poiMesh.normals[1], _Shadow2ndNormalStrength);
	N3 = mix(poiMesh.normals[0], poiMesh.normals[1], _Shadow3rdNormalStrength);

	vec3 lightMap = vec3(0.0);
	lightMap.x = clamp(dot(poiLight.direction, N1) * 0.5 + 0.5, 0.0, 1.0);
	lightMap.y = clamp(dot(poiLight.direction, N2) * 0.5 + 0.5, 0.0, 1.0);
	lightMap.z = clamp(dot(poiLight.direction, N3) * 0.5 + 0.5, 0.0, 1.0);
	lightMap.x *= mix(1.0, poiLight.attenShadows, _ShadowReceive);
	lightMap.y *= mix(1.0, poiLight.attenShadows, _Shadow2ndReceive);
	lightMap.z *= mix(1.0, poiLight.attenShadows, _Shadow3rdReceive);

	vec4 shadowBorderMask = vec4(1.0);

	if (_ShadowBorderMapToggle >= 0.5)
	{
#if defined(PROP_SHADOWBORDERMASK)
		vec2 shadowShift0 = vec2(_ShadowAOShift.x, _ShadowAOShift.y);
		vec2 shadowShift1 = vec2(_ShadowAOShift.z, _ShadowAOShift.w);
		vec2 shadowShift2 = vec2(_ShadowAOShift2.x, _ShadowAOShift2.y);

		shadowShift0.y = (shadowShift0.x == shadowShift0.y) ? (shadowShift0.y + 0.001) : shadowShift0.y;
		shadowShift1.y = (shadowShift1.x == shadowShift1.y) ? (shadowShift1.y + 0.001) : shadowShift1.y;
		shadowShift2.y = (shadowShift2.x == shadowShift2.y) ? (shadowShift2.y + 0.001) : shadowShift2.y;

		shadowShift0 = vec2(1.0 / (shadowShift0.y - shadowShift0.x), shadowShift0.x / (shadowShift0.x - shadowShift0.y));
		shadowShift1 = vec2(1.0 / (shadowShift1.y - shadowShift1.x), shadowShift1.x / (shadowShift1.x - shadowShift1.y));
		shadowShift2 = vec2(1.0 / (shadowShift2.y - shadowShift2.x), shadowShift2.x / (shadowShift2.x - shadowShift2.y));

		vec2 shadowBorderMaskUV = poiUV(poiMesh.uv[int(_ShadowBorderMaskUV)], _ShadowBorderMask_ST);
		if (_ShadowBorderMaskLOD != 0.0)
		{
			vec2 shadowBorderLod = vec2(pow(_ShadowBorderMaskLOD, 4.0));
			shadowBorderMask = poiTexPanGrad(_ShadowBorderMask, shadowBorderMaskUV, _ShadowBorderMaskPan, max(abs(dFdx(shadowBorderMaskUV)), shadowBorderLod), max(abs(dFdy(shadowBorderMaskUV)), shadowBorderLod));
		}
		else
		{
			shadowBorderMask = poiTexPan(_ShadowBorderMask, shadowBorderMaskUV, _ShadowBorderMaskPan);
		}

		shadowBorderMask.r = clamp(shadowBorderMask.r * shadowShift0.x + shadowShift0.y, 0.0, 1.0);
		shadowBorderMask.g = clamp(shadowBorderMask.g * shadowShift1.x + shadowShift1.y, 0.0, 1.0);
		shadowBorderMask.b = clamp(shadowBorderMask.b * shadowShift2.x + shadowShift2.y, 0.0, 1.0);

		lightMap.xyz = _ShadowPostAO != 0.0 ? lightMap.xyz : lightMap.xyz * shadowBorderMask.rgb;
#endif
	}

	if (_LightingMapMode == 4.0)
	{
		// mlLitColor already carries the cookie and the shadow
		lightMap.xyz = vec3(poiLight.lightMapNoAttenuation);
	}
	if (ambientPass)
	{
		// The ambient reaches no surface directly: no second set of bands from its direction.
		lightMap = vec3(0.0);
	}
	if (_LightingMulitlayerNonLinear >= 0.5)
	{
		lns.x = poiEdgeNonLinearNoSaturate(lightMap.x, _ShadowBorder, _ShadowBlur * blurMap.r);
		lns.y = poiEdgeNonLinearNoSaturate(lightMap.y, _Shadow2ndBorder, _Shadow2ndBlur * blurMap.g);
		lns.z = poiEdgeNonLinearNoSaturate(lightMap.z, _Shadow3rdBorder, _Shadow3rdBlur * blurMap.b);
		lns.w = poiEdgeNonLinearNoSaturate(lightMap.x, _ShadowBorder, _ShadowBlur * blurMap.r, _ShadowBorderRange);
	}
	else
	{
		lns.x = poiEdgeLinearNoSaturate(lightMap.x, _ShadowBorder, _ShadowBlur * blurMap.r);
		lns.y = poiEdgeLinearNoSaturate(lightMap.y, _Shadow2ndBorder, _Shadow2ndBlur * blurMap.g);
		lns.z = poiEdgeLinearNoSaturate(lightMap.z, _Shadow3rdBorder, _Shadow3rdBlur * blurMap.b);
		lns.w = poiEdgeLinearNoSaturate(lightMap.x, _ShadowBorder, _ShadowBlur * blurMap.r, _ShadowBorderRange);
	}

#if defined(PROP_SHADOWBORDERMASK)
	lns = _ShadowPostAO != 0.0 ? lns * shadowBorderMask.rgbr : lns;
#endif
	lns *= poiLight.detailShadow;
	lns = clamp(lns, 0.0, 1.0);

#if defined(PROP_SHADOWSTRENGTHMASK)
	vec4 shadowStrengthMask = poiTexPan(_ShadowStrengthMask, poiUV(poiMesh.uv[int(_ShadowStrengthMaskUV)], _ShadowStrengthMask_ST), _ShadowStrengthMaskPan.xy);
#else
	vec4 shadowStrengthMask = vec4(1.0);
#endif
	if (_ShadowMaskType == 1.0)
	{
		vec3 flatN = normalize(mat3(poiMesh.modelMatrix) * vec3(0.0, 0.25, 1.0));
		float flatNDotL = ambientPass ? -1.0 : dot(flatN, poiLight.direction);
		float lnFlat = clamp((flatNDotL + _ShadowFlatBorder) / _ShadowFlatBlur, 0.0, 1.0);
		lns = mix(vec4(lnFlat), lns, shadowStrengthMask.r);
	}
	else if (_ShadowMaskType == 0.0)
	{
		shadowStrength *= shadowStrengthMask.r;
	}
	vec3 indirectColor = vec3(1.0);

	if (_ShadowColor.a > 0.0)
	{
#if defined(PROP_SHADOWCOLORTEX)
		vec4 shadowColorTex = poiTexPan(_ShadowColorTex, poiUV(poiMesh.uv[int(_ShadowColorTexUV)], _ShadowColorTex_ST), _ShadowColorTexPan);
		shadowColorTex.rgb = clamp(shadowColorTex.rgb + _ShadowColorTexAddition, 0.0, 1.0);
#else
		vec4 shadowColorTex = vec4(1.0, 1.0, 1.0, 1.0);
#endif
		indirectColor = mix(vec3(1.0, 1.0, 1.0), shadowColorTex.rgb, shadowColorTex.a) * _ShadowColor.rgb;
	}
	if (_Shadow2ndColor.a > 0.0)
	{
#if defined(PROP_SHADOW2NDCOLORTEX)
		vec4 shadow2ndColorTex = poiTexPan(_Shadow2ndColorTex, poiUV(poiMesh.uv[int(_Shadow2ndColorTexUV)], _Shadow2ndColorTex_ST), _Shadow2ndColorTexPan);
		shadow2ndColorTex.rgb = clamp(shadow2ndColorTex.rgb + _Shadow2ndColorTexAddition, 0.0, 1.0);
#else
		vec4 shadow2ndColorTex = vec4(1.0, 1.0, 1.0, 1.0);
#endif
		shadow2ndColorTex.rgb = mix(vec3(1.0, 1.0, 1.0), shadow2ndColorTex.rgb, shadow2ndColorTex.a) * _Shadow2ndColor.rgb;
		lns.y = _Shadow2ndColor.a - lns.y * _Shadow2ndColor.a;
		indirectColor = mix(indirectColor, shadow2ndColorTex.rgb, lns.y);
	}
	if (_Shadow3rdColor.a > 0.0)
	{
#if defined(PROP_SHADOW3RDCOLORTEX)
		vec4 shadow3rdColorTex = poiTexPan(_Shadow3rdColorTex, poiUV(poiMesh.uv[int(_Shadow3rdColorTexUV)], _Shadow3rdColorTex_ST), _Shadow3rdColorTexPan);
		shadow3rdColorTex.rgb = clamp(shadow3rdColorTex.rgb + _Shadow3rdColorTexAddition, 0.0, 1.0);
#else
		vec4 shadow3rdColorTex = vec4(1.0, 1.0, 1.0, 1.0);
#endif
		shadow3rdColorTex.rgb = mix(vec3(1.0, 1.0, 1.0), shadow3rdColorTex.rgb, shadow3rdColorTex.a) * _Shadow3rdColor.rgb;
		lns.z = _Shadow3rdColor.a - lns.z * _Shadow3rdColor.a;
		indirectColor = mix(indirectColor, shadow3rdColorTex.rgb, lns.z);
	}

	indirectColor = mix(indirectColor, indirectColor * poiFragData.baseColor, _ShadowMainStrength);
	poiLight.rampedLightMap = vec3(lns.x);
	indirectColor = mix(indirectColor, vec3(1.0), lns.w * _ShadowBorderColor.rgb * _ShadowBorderColor.a);
	indirectColor = indirectColor * mix(ambientColor * poiLight.occlusion, mlLitColor, ignoreAmbientColor);
	if (poiLight.isAdditive == 0)
	{
		indirectColor = mix(indirectColor, mlLitColor, poiLight.indirectColor * _ShadowEnvStrength);
	}
	indirectColor = mix(mlLitColor, indirectColor, shadowStrength);
	poiLight.finalLighting = mix(indirectColor, mlLitColor, lns.x);
#endif

#if defined(POI_LIGHTING_SHADEMAP)
#if !defined(POI_PASS_OUTLINE)
	// applyShadeMapping per light: the shade this light's light map gives the base.
	vec3 shadeMapRatio = vec3(1.0);
	if (!ambientPass)
	{
		float mainShadowMask = 0.0;
		vec3 shadedColor = poiShadeMapColor(poiFragData, poiMesh, poiLight, mainShadowMask);
		poiLight.rampedLightMap = vec3(1.0 - mainShadowMask * _ShadowStrength);
		shadeMapRatio = poiShadeMapRatio(shadedColor, poiFragData.baseColor);
		poiFragData.baseColor.rgb = shadedColor;
	}
#endif
	poiLight.finalLighting = litColor;
	if (poiLight.isAdditive != 0)
	{
		float addShadeMask = clamp(1.0 - (poiLight.lightMap - (_BaseColor_Step - _BaseShade_Feather)) / _BaseShade_Feather, 0.0, 1.0) * shadowStrength;
		poiLight.finalLighting = mix(litColor, passthroughColor, addShadeMask);
		poiLight.rampedLightMap = vec3(1.0 - addShadeMask);
	}
#if !defined(POI_PASS_OUTLINE)
	poiLight.finalLighting *= shadeMapRatio;
#endif
#endif

#if defined(POI_LIGHTING_REALISTIC)
	// The ramp is the map mode term only - the cookie and the casted shadow ride on the light
	// colour like every other mode, so _ShadowStrength scales the shading and nothing else.
	float realisticLightMap = mix(1.0, clamp(poiLight.lightMapNoAttenuation, 0.0, 1.0), shadowStrength);

	PoiDirectLight directLight;
	directLight.dir = poiLight.direction;
	directLight.color = max(vec3(0.0), rawLightColor * poiLight.lightCookie * poiLight.attenDistance * poiLight.attenShadowsStrength) * realisticLightMap;
	directLight.ndotl = poiLight.nDotLSaturated;
	PoiIndirectLight indirectLight = CreateIndirectLight(poiMesh, poiCam, poiLight);
	indirectLight.diffuse = mix(indirectLight.diffuse, vec3(dot(indirectLight.diffuse, vec3(0.299, 0.587, 0.114))), poiLight.lightingMonochromatic);
	directLight.color = max(directLight.color * _PPLightingMultiplier, vec3(0.0));
	directLight.color = max(directLight.color + _PPLightingAddition, vec3(0.0));
	indirectLight.diffuse = max(indirectLight.diffuse * _PPLightingMultiplier, vec3(0.0));
	indirectLight.diffuse = max(indirectLight.diffuse + _PPLightingAddition, vec3(0.0));

	poiLight.rampedLightMap = vec3(poiLight.nDotLSaturated);
	poiLight.finalLighting = POI_BRDF_PBS(vec3(1.0), POI_COLOR_SPACE_DIELECTRIC_SPEC.rgb, POI_COLOR_SPACE_DIELECTRIC_SPEC.a, 0.0, poiMesh.normals[1], poiCam.eyeViewDir, poiLight.nDotVSaturated, poiLight.nDotLSaturated, directLight, indirectLight).xyz;

	float realisticShadowAmount = clamp((1.0 - clamp(poiLight.lightMap, 0.0, 1.0)) * shadowStrength, 0.0, 1.0);
	poiLight.finalLighting *= mix(vec3(1.0), _LightingShadowColor, realisticShadowAmount);
#endif

#if defined(POI_LIGHTING_CLOTH)
	vec4 clothSurface = poiClothSurface(poiMesh);
	float metallic = clothSurface.x;
	float roughness = clothSurface.y;
	float reflectance = clothSurface.z;
	float clothmask = clothSurface.w;

	float clothBrdfMask = clothmask * (1.0 - metallic);

	float perceptualRoughness = pow(roughness, 2.0);
	float clampedRoughness = max(0.002, perceptualRoughness);

	vec3 f0 = 0.16 * reflectance * reflectance * (1.0 - metallic) + poiFragData.baseColor * metallic;

	vec3 dfg = SampleDFG(poiLight.nDotV, perceptualRoughness).xyz;

	vec3 energyCompensation = EnvBRDFEnergyCompensation(dfg, f0, clothBrdfMask);

	poiLight.finalLighting = vec3(Fd_Burley(perceptualRoughness, poiLight.nDotV, poiLight.nDotLSaturated, poiLight.lDotH));
	poiLight.finalLighting *= litColor * poiLight.attenShadowsStrength * poiLight.nDotLSaturated;
	poiLight.rampedLightMap = vec3(poiLight.nDotLSaturated);

	vec3 specular = max(vec3(0.0), Specular(clampedRoughness, poiLight, f0, poiMesh.normals[1], clothBrdfMask) * poiLight.finalLighting * energyCompensation * PI); // (D * V) * F

	poiLight.finalLightAdd += max(vec3(0.0), specular);
	if (poiLight.isAdditive == 0)
	{
		vec3 indirectDiffuse = poiLight.indirectColor;
		indirectDiffuse = max(vec3(0.0), indirectDiffuse);
		indirectDiffuse = mix(indirectDiffuse, vec3(dot(indirectDiffuse, vec3(0.299, 0.587, 0.114))), poiLight.lightingMonochromatic);
		// The reflection probe term is the engine's environment specular (PoiClothReflectionOutputs).
		poiLight.finalLighting += indirectDiffuse * poiLight.occlusion;
	}

	// The source scales baseColor here; the engine lights ALBEDO, which keeps the unscaled base, so the
	// scale also rides the share (and so the ambient tint).
	poiLight.finalLighting *= (1.0 - metallic);
	if (!ambientPass)
	{
		poiFragData.baseColor.xyz *= (1.0 - metallic);
	}
#endif

#if defined(POI_LIGHTING_WRAPPED)
	vec3 GREYSCALE_VECTOR = vec3(0.33333, 0.33333, 0.33333);
	vec3 wrappedLightColor = rawLightColor * poiLight.lightCookie;
	if (_LightingForceColorEnabled >= 0.5)
	{
		wrappedLightColor = poiThemeColor(poiMods, _LightingForcedColor.rgb, _LightingForcedColorThemeIndex);
	}
	vec3 directColor = wrappedLightColor * clamp(RTWrapFunc(poiLight.nDotL, _LightingWrappedWrap, _LightingWrappedNormalization), 0.0, 1.0);
	vec3 indirectColor = vec3(0.0);
	// GetSHMaxL1: the SH at its brightest, the unit ambient in fragment().
	vec3 ShadeSH9Plus_2 = vec3(0.0);
	if (poiLight.isAdditive == 0 && ambientPass)
	{
		indirectColor = vec3(poiWrappedSHScaleL0(_LightingWrappedWrap)) * poiLight.occlusion;
		ShadeSH9Plus_2 = vec3(1.0);
	}
	directColor = mix(directColor, vec3(dot(directColor, vec3(0.299, 0.587, 0.114))), poiLight.lightingMonochromatic); // Duplicated from Lightdata due to recreating the light colour
	indirectColor = mix(indirectColor, vec3(dot(indirectColor, vec3(0.299, 0.587, 0.114))), poiLight.lightingMonochromatic); // Ditto^

	float bw_topDirectLighting_2 = dot(wrappedLightColor, GREYSCALE_VECTOR);
	float bw_directLighting = dot(directColor, GREYSCALE_VECTOR);
	float bw_indirectLighting = dot(indirectColor, GREYSCALE_VECTOR);
	float bw_topIndirectLighting = dot(ShadeSH9Plus_2, GREYSCALE_VECTOR);

	poiLight.lightMap = poiSmoothstepFromZero(bw_topIndirectLighting + bw_topDirectLighting_2, bw_indirectLighting + bw_directLighting) * min(poiLight.detailShadow, poiLight.attenShadowsStrength);
	if (ambientPass)
	{
		// As in every mode, the ambient is shaded the way the source shades it where the light does not reach.
		poiLight.lightMap = 0.0;
	}
	poiLight.rampedLightMap = vec3(clamp((poiLight.lightMap - (1.0 - _LightingGradientEnd)) / clamp((1.0 - _LightingGradientStart) - (1.0 - _LightingGradientEnd) + 0.001, 0.0, 1.0), 0.0, 1.0));
	vec3 mathRamp = mix(vec3(1.0, 1.0, 1.0), clamp(mix((_LightingShadowColor * mix(indirectColor, vec3(1.0), _LightingIgnoreAmbientColor)), vec3(1.0, 1.0, 1.0), clamp(poiLight.rampedLightMap, 0.0, 1.0)), 0.0, 1.0), shadowStrength);

	directColor *= clamp(poiLight.rampedLightMap + 1.0 - shadowStrength, 0.0, 1.0) * _LightingWrappedColor.rgb;

	vec3 finalWrap = directColor + indirectColor;
	if (poiLight.lightingCapEnabled != 0.0)
	{
		finalWrap = min(finalWrap, vec3(_LightingCap));
	}
	poiLight.finalLighting = finalWrap * clamp(mathRamp + 1.0 - shadowStrength, 0.0, 1.0);
#endif

#if defined(POI_LIGHTING_SKIN)
	poiLight.rampedLightMap = vec3(poiLight.nDotLSaturated);

	// Scattering mask.
#if defined(PROP_SKINTHICKNESSMAP)
	float subsurface = 1.0 - poiTexPan(_SkinThicknessMap, poiUV(poiMesh.uv[int(_SkinThicknessMapUV)], _SkinThicknessMap_ST), _SkinThicknessMapPan).r;
#else
	float subsurface = 1.0;
#endif
	if (_SkinThicknessMapInvert >= 0.5)
	{
		subsurface = 1.0 - subsurface;
	}

	subsurface = pow(subsurface, max(_SkinThicknessPower, 0.001));

	float skinScattering = clamp(subsurface * _SssScale * 2.0, 0.0, 1.0);

	vec3 skinLitColor = litColor * poiLight.attenShadowsStrength;
	float ndlBlur = poiLight.nDotLNormalized;
	float lumi = dot(litColor, vec3(0.2126, 0.7152, 0.0722));
	vec4 sssLookupUv = vec4(ndlBlur, skinScattering * lumi, 0.0, 0.0);
#if defined(PROP_SKINLUT)
	vec3 sss = poiLight.lightMapNoAttenuation * poiTexLod(_SkinLUT, sssLookupUv.xy, sssLookupUv.w).rgb;
#else
	vec3 sss = vec3(poiLight.lightMapNoAttenuation);
#endif

	poiLight.finalLighting = mix(skinLitColor, min(mix(ambientColor * _LightingShadowColor, _LightingShadowColor, ignoreAmbientColor) * poiLight.occlusion + (sss * skinLitColor), litColor), shadowStrength);
#endif

#if defined(POI_LIGHTING_SDF)
	vec3 forward = normalize(mat3(poiMesh.modelMatrix) * _SDFForward.xyz);
	vec3 left = normalize(mat3(poiMesh.modelMatrix) * _SDFLeft.xyz);
	vec3 lightDirHorizontal = normalize(vec3(poiLight.direction.x, 0.0, poiLight.direction.z));

	float lightAtten = 1.0 - (dot(lightDirHorizontal, forward) * 0.5 + 0.5);
	float filpU = sign(dot(lightDirHorizontal, left));

#if defined(PROP_SDFSHADINGTEXTURE)
	float shadowSDF = poiTexPan(_SDFShadingTexture, poiUV(poiMesh.uv[int(_SDFShadingTextureUV)], _SDFShadingTexture_ST) * vec2(filpU, 1.0), _SDFShadingTexturePan).r;
#else
	float shadowSDF = 1.0;
#endif
	float blur = _SDFBlur * 0.1;
	float faceShadow = (blur > 0.0 ? smoothstep(lightAtten - blur, lightAtten + blur, shadowSDF) : step(lightAtten, shadowSDF)) * poiLight.detailShadow;
	if (ambientPass)
	{
		faceShadow = 0.0;
	}
	vec3 sdfLitColor = litColor * poiLight.attenShadowsStrength;
	poiLight.rampedLightMap = vec3(faceShadow * poiLight.attenShadowsStrength);

	vec3 indirectColor = _LightingShadowColor.rgb;
	indirectColor = indirectColor * mix(ambientColor * poiLight.occlusion, sdfLitColor, ignoreAmbientColor);
	indirectColor = mix(sdfLitColor, indirectColor, shadowStrength);

	poiLight.finalLighting = mix(indirectColor, sdfLitColor, faceShadow);
#endif

	poiLight.rampedLightMap *= poiLight.attenDistance;
	PoiShadingGlobalMasks(poiMods, poiLight);

	if (ambientPass)
	{
		poiLight.occlusion = poiOcclusion;
		poiLight.ambientTint *= max(poiLight.finalLighting, vec3(1e-3));
	}
}

// The source caps the main light's colors (and the SH) at _LightingCap and limits each add pass light on
// its own. Directional lights reach light() before omni and spot lights, so capping the running total
// after each directional light caps the main lights together and leaves the others alone. The realistic
// mode lights with the raw light color and is not capped. Call after DIFFUSE_LIGHT += finalLighting / tint.
vec3 PoiLightingCapDiffuse(vec3 diffuseLight, in PoiLight poiLight)
{
#if !defined(POI_LIGHTING_REALISTIC)
	if (poiLight.isDirectional && poiLight.lightingCapEnabled != 0.0)
	{
		return min(diffuseLight, vec3(_LightingCap) / max(poiLight.ambientTint, vec3(1e-4)));
	}
#endif
	return diffuseLight;
}
#endif
//endex
