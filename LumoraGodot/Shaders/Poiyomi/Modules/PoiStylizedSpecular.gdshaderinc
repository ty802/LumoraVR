// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Stylized specular: the UTS2 highlight and lilToon's reflection, per light (light() through
// PoiLightingDependantModules). light() cannot change the lit base color, so a base color edit reaches the
// light's share as its change times finalLighting. lilToon's environment reflection and its metallic
// darkening are fragment()'s: lilReflectionOutputs, through the engine's environment (see PoiReflections).
// Also here: applyReflectionOutputs, which needs both modules.

//ifex _StylizedSpecular==0
#if defined(POI_STYLIZED_SPECULAR)
#if defined(POI_STYLIZED_SPECULAR_UTS)
void CalculateUTSSpecular(inout PoiFragData poiFragData, in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, in PoiMods poiMods)
{
	vec3 blendedNormal = mix(poiMesh.normals[0], poiMesh.normals[1], _StylizedSpecularNormalStrength);
	float nDotH = dot(blendedNormal, poiLight.halfDir);
	float specArea = 0.5 * nDotH + 0.5;
#if defined(PROP_HIGHCOLOR_TEX)
	vec3 specularMap = poiTexPan(_HighColor_Tex, poiUV(poiMesh.uv[int(_HighColor_TexUV)], _HighColor_Tex_ST), _HighColor_TexPan).rgb;
#else
	vec3 specularMap = vec3(1.0);
#endif

	float layer1Exp = exp2(mix(11.0, 1.0, _HighColor_Power));
	float layer2Exp = exp2(mix(11.0, 1.0, _Layer2Size));
	float layer1Thresh = 1.0 - pow(_HighColor_Power, 5.0);
	float layer2Thresh = 1.0 - pow(_Layer2Size, 5.0);
	float specMask1 = 0.0;
	float specMask2 = 0.0;
	if (_Is_SpecularToHighColor != 0.0)
	{
		specMask1 += pow(specArea, layer1Exp) * _Layer1Strength;
		specMask2 += pow(specArea, layer2Exp) * _Layer2Strength;
	}
	else
	{
		specMask1 += poiEdgeNonLinear(specArea, layer1Thresh, _StylizedSpecularFeather) * _Layer1Strength;
		specMask2 += poiEdgeNonLinear(specArea, layer2Thresh, _StylizedSpecular2Feather) * _Layer2Strength;
	}

#if defined(PROP_SET_HIGHCOLORMASK)
	float specularMask = poiTexPan(_Set_HighColorMask, poiUV(poiMesh.uv[int(_Set_HighColorMaskUV)], _Set_HighColorMask_ST), _Set_HighColorMaskPan)[int(_Set_HighColorMaskChannel)];
#else
	float specularMask = 1.0;
#endif
	if (_StylizedSpecularInvertMask >= 0.5)
	{
		specularMask = 1.0 - specularMask;
	}
	specularMask = clamp(specularMask + _Tweak_HighColorMaskLevel, 0.0, 1.0);

	// The source stores the ramped light map's lerp in a scalar: its first channel.
	float specMask = clamp(specMask1 + specMask2, 0.0, 1.0) * specularMask * mix(poiLight.rampedLightMap, vec3(1.0), _StylizedSpecularIgnoreShadow).x;
	float specLightMap = poiLight.lightMap;
	float attenuation = min(mix(poiLight.nDotLSaturated, 1.0, _StylizedSpecularIgnoreNormal), mix(mix(specLightMap, 1.0, _SSIgnoreCastedShadows), 1.0, _StylizedSpecularIgnoreShadow));

	float finalSpecMask = min(min(specMask, poiLight.occlusion), attenuation) * _StylizedSpecularStrength;
	vec3 specColorBase = specularMap * poiThemeColor(poiMods, _HighColor.rgb, _HighColorThemeIndex);
	vec3 finalSpecColor = specColorBase * mix(vec3(1.0), poiLight.directColor, _UseLightColor);
	vec3 blendedBase = poiFragData.baseColor;
	switch (int(_Is_BlendAddToHiColor))
	{
		case 0:
		{
			blendedBase = mix(poiFragData.baseColor, finalSpecColor, finalSpecMask);
			break;
		}
		case 1:
		{
			poiLight.finalLightAdd += max(vec3(0.0), finalSpecColor * finalSpecMask);
			break;
		}
		case 2:
		{
			blendedBase = mix(poiFragData.baseColor, blendScreen(poiFragData.baseColor, finalSpecColor), finalSpecMask);
			break;
		}
		case 3:
		{
			blendedBase = mix(poiFragData.baseColor, poiFragData.baseColor * finalSpecColor, finalSpecMask);
			break;
		}
	}
	poiLight.finalLightAdd += (blendedBase - poiFragData.baseColor) * poiLight.finalLighting;
}
#endif

#if defined(POI_STYLIZED_SPECULAR_LILTOON)
void GSAA(inout float roughness, vec3 N, float strength)
{
	vec3 dx = abs(dFdx(N));
	vec3 dy = abs(dFdy(N));
	float dxy = max(dot(dx, dx), dot(dy, dy));
	float roughnessGSAA = dxy / (dxy * 5.0 + 0.002) * strength;
	roughness = max(roughness, roughnessGSAA);
}

float GSAAForSmoothness(float smoothness, vec3 N, float strength)
{
	float roughness = 0.0;
	GSAA(roughness, N, strength);
	smoothness = min(smoothness, clamp(1.0 - roughness, 0.0, 1.0));
	return smoothness;
}

vec3 lilFresnelTerm(vec3 F0, float cosA)
{
	float a = 1.0 - cosA;
	float a2 = a * a;
	return F0 + (1.0 - F0) * a2 * a2 * a;
}

vec3 lilFresnelLerp(vec3 F0, vec3 F90, float cosA)
{
	float a = 1.0 - cosA;
	float a2 = a * a;
	return mix(F0, F90, a2 * a2 * a);
}

// lilToon tags _Metallic and _Reflectance [Gamma] and uses the raw value, letting Unity
// convert on upload. We cannot use [Gamma] here: ShaderOptimizer bakes Float/Range
// constants without applying the flag, so a locked material would inline the unconverted
// value and diverge from the same material unlocked. Convert in-shader instead, matching
// Mathf.GammaToLinearSpace exactly rather than the pow(x, 2.2) approximation.
float lilGammaToLinear(float v)
{
	return v <= 0.04045 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4);
}

// A macro: a sampler cannot be a function parameter here. Cubemaps are linear, so the source's
// lilDecodeHDR is the identity.
#define lilCustomReflection(tex, viewDirection, normalDirection, perceptualRoughness) (textureLod(tex, reflect(-(viewDirection), (normalDirection)), (perceptualRoughness) * (10.2 - 4.2 * (perceptualRoughness))).rgb)

vec3 lilCalcSpecular(in PoiMesh poiMesh, in PoiCam poiCam, vec3 L, vec3 specular, float attenuation, float roughness, vec3 N, float nv)
{
	vec3 H = normalize(poiCam.eyeViewDir + L);
	float nh = clamp(dot(N, H), 0.0, 1.0);

	if (_SpecularToon != 0.0)
	{
		return vec3(poiEdgeLinear(pow(nh, 1.0 / max(roughness, 0.0001)), _SpecularBorder, _SpecularBlur));
	}

	float nl = clamp(dot(N, L), 0.0, 1.0);
	float lh = clamp(dot(L, H), 0.0, 1.0);

	float ggx = 0.0;
	float sjggx = 0.0;
	float lambdaV = 0.0;
	float lambdaL = 0.0;
	float d = 1.0;
	{
		float roughness2 = max(roughness, 0.002);
		lambdaV = nl * (nv * (1.0 - roughness2) + roughness2);
		lambdaL = nv * (nl * (1.0 - roughness2) + roughness2);

		float r2 = roughness2 * roughness2;
		d = (nh * r2 - nh) * nh + 1.0;
		ggx = r2 / (d * d + 1e-7);
	}

	sjggx = 0.5 / (lambdaV + lambdaL + 1e-5);

	float specularTerm = sjggx * ggx;
	specularTerm *= nl * attenuation;

	return specularTerm * lilFresnelTerm(specular, lh);
}

// The material's smoothness, roughness and metallic as lilReflection reads them.
void lilReflectionSurface(in PoiMesh poiMesh, inout float smoothness, inout float perceptualRoughness, inout float roughness, inout float metallic)
{
	perceptualRoughness = 1.0;
	smoothness = _Smoothness;
#if defined(PROP_SMOOTHNESSTEX)
	smoothness *= poiTexPan(_SmoothnessTex, poiUV(poiMesh.uv[int(_SmoothnessTexUV)], _SmoothnessTex_ST), _SmoothnessTexPan).r;
#endif
	smoothness = GSAAForSmoothness(smoothness, poiMesh.normals[1], _GSAAStrength);
	perceptualRoughness *= (1.0 - smoothness);
	roughness = perceptualRoughness * perceptualRoughness;

	metallic = lilGammaToLinear(_Metallic);
#if defined(PROP_METALLICGLOSSMAP)
	metallic *= poiTexPan(_MetallicGlossMap, poiUV(poiMesh.uv[int(_MetallicGlossMapUV)], _MetallicGlossMap_ST), _MetallicGlossMapPan).r;
#endif
}

vec4 lilReflectionColor(in PoiFragData poiFragData, in PoiMesh poiMesh)
{
	vec4 reflectionColor = _ReflectionColor;
#if defined(PROP_REFLECTIONCOLORTEX)
	reflectionColor *= poiTexPan(_ReflectionColorTex, poiUV(poiMesh.uv[int(_ReflectionColorTexUV)], _ReflectionColorTex_ST), _ReflectionColorTexPan);
#endif
	if (_ReflectionApplyTransparency >= 0.5)
	{
		reflectionColor.a *= poiFragData.alpha;
	}
	return reflectionColor;
}

// The specular, per light. poiLight is inout (the source passes a copy): the highlight's change to the base
// color is added as this light's share. A directional light takes the base pass's rules, an omni or spot
// light the add pass's.
void lilReflection(inout PoiFragData poiFragData, in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, in PoiMods poiMods)
{
	float perceptualRoughness = 1.0;
	float roughness = 1.0;
	float smoothness = 1.0;
	float metallic = 0.0;
	lilReflectionSurface(poiMesh, smoothness, perceptualRoughness, roughness, metallic);
	vec3 specular = mix(vec3(lilGammaToLinear(_Reflectance)), poiFragData.baseColor, metallic);

	vec3 specularN = mix(poiMesh.normals[0], poiMesh.normals[1], _SpecularNormalStrength);
	float specularNV = clamp(dot(specularN, poiCam.eyeViewDir), 0.0, 1.0);

	vec4 reflectionColor = lilReflectionColor(poiFragData, poiMesh);

	bool useReflection = poiLight.isDirectional ? _UseReflection >= 0.5 : (_UseReflection >= 0.5 && _ApplySpecular >= 0.5 && _ApplySpecularFA >= 0.5);
	if (useReflection)
	{
		// The source's per-pass baseColor -= metallic * baseColor. Without Mochie it is the engine's
		// (1 - METALLIC) on this light's diffuse; with Mochie, fragment() already took it from the base color.
		vec3 litBase = poiFragData.baseColor;
#if !defined(POI_MOCHIE_BRDF)
		litBase = litBase - metallic * litBase;
#endif

		// Specular
		if (!poiLight.isDirectional || _ApplySpecular >= 0.5)
		{
			// Directional: the source's shadowed main light (SHADOWS_SCREEN) path.
			float specAttenuation = poiLight.isDirectional ? poiLight.rampedLightMap.x : poiLight.lightMapNoAttenuation;
			vec3 reflectCol = lilCalcSpecular(poiMesh, poiCam, poiLight.direction, specular, specAttenuation, roughness, specularN, specularNV);
			vec3 blendedBase = lilBlendColor(litBase, reflectionColor.rgb * poiLight.directColor, reflectCol * reflectionColor.a, _ReflectionBlendMode);
			poiLight.finalLightAdd += (blendedBase - litBase) * poiLight.finalLighting;
		}
	}
}

// fragment(): the metallic darkening and the environment reflection (the source's Reflection block).
// Without Mochie this owns the engine's environment: METALLIC is the metallic (the engine's 1 - METALLIC is
// the source's subtract and its F0 mixes toward the albedo the same way), SPECULAR gives the dielectric F0 the
// Reflectance, ROUGHNESS the perceptual roughness. With Mochie (which owns those) the subtract lands on the base
// color and only the material cubemap reflects, blended into the base color as lilToon does.
void lilReflectionOutputs(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, in PoiLight poiLight, inout vec4 pbrOut, inout vec4 radianceOut)
{
	if (_UseReflection < 0.5)
	{
		return;
	}
	float perceptualRoughness = 1.0;
	float roughness = 1.0;
	float smoothness = 1.0;
	float metallic = 0.0;
	lilReflectionSurface(poiMesh, smoothness, perceptualRoughness, roughness, metallic);
	float reflectance = lilGammaToLinear(_Reflectance);
	vec3 specular = mix(vec3(reflectance), poiFragData.baseColor, metallic);
	vec4 reflectionColor = lilReflectionColor(poiFragData, poiMesh);
	vec3 N = mix(poiMesh.normals[0], poiMesh.normals[1], _ReflectionNormalStrength);

	// The material cubemap replaces the probe when overridden; the probe itself is always usable here.
	bool useCube = _ReflectionCubeOverride >= 0.5;
	vec3 envReflectionColor = vec3(0.0);
	if (_ApplyReflection >= 0.5 && useCube)
	{
#if defined(PROP_REFLECTIONCUBETEX)
		envReflectionColor = lilCustomReflection(_ReflectionCubeTex, poiCam.eyeViewDir, N, perceptualRoughness);
#endif
		// The source lights it with the main light's color; fragment() has only the unit light state.
		envReflectionColor *= _ReflectionCubeColor.rgb * mix(vec3(1.0), max(poiLight.directColor, poiLight.indirectColor), _ReflectionCubeEnableLighting);
	}

#if defined(POI_MOCHIE_BRDF)
	poiFragData.baseColor = poiFragData.baseColor - metallic * poiFragData.baseColor;
	if (_ApplyReflection >= 0.5 && useCube)
	{
		float oneMinusReflectivity = DielectricSpec.a - metallic * DielectricSpec.a;
		float grazingTerm = clamp(smoothness + (1.0 - oneMinusReflectivity), 0.0, 1.0);
		float surfaceReduction = 1.0 / (roughness * roughness + 1.0);
		vec3 reflectCol = surfaceReduction * envReflectionColor * lilFresnelLerp(specular, vec3(grazingTerm), poiLight.nDotV);
		poiFragData.baseColor = lilBlendColor(poiFragData.baseColor, reflectionColor.rgb, reflectCol * reflectionColor.a, _ReflectionBlendMode);
	}
#else
#if !defined(POI_SHADING)
	// Unshaded, the lit part is emitted and the engine's darkening never reaches it.
	poiFragData.finalColor -= metallic * poiFragData.baseColor;
#endif
	pbrOut.x = metallic;
	pbrOut.y = perceptualRoughness;
	pbrOut.z = 0.0;
	if (_ApplyReflection >= 0.5)
	{
		pbrOut.z = sqrt(clamp(reflectance / 0.16, 0.0, 1.0));
		if (useCube)
		{
			radianceOut = vec4(envReflectionColor * reflectionColor.rgb * reflectionColor.a, 1.0);
		}
		else
		{
			radianceOut = vec4(0.0, 0.0, 0.0, 1.0 - clamp(CalculateLuminance(reflectionColor.rgb) * reflectionColor.a, 0.0, 1.0));
		}
	}
	else if (metallic > 0.0)
	{
		// The darkening without the reflection: no sky radiance.
		radianceOut = vec4(0.0, 0.0, 0.0, 1.0);
	}
#endif
}
#endif
#endif
//endex

#if defined(POI_MOCHIE_BRDF) || defined(POI_CLEARCOAT) || defined(POI_ENVIRORIM) || (defined(POI_STYLIZED_SPECULAR) && defined(POI_STYLIZED_SPECULAR_LILTOON))
#define POI_HAS_REFLECTION_OUTPUTS
// fragment(), after everything that edits the base color and before ALBEDO and the handoff: the engine's
// environment inputs (pbrOut = METALLIC, ROUGHNESS, SPECULAR; radianceOut = RADIANCE).
void applyReflectionOutputs(inout PoiFragData poiFragData, inout MochieBRDFData mochieBRDFData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiLight poiLight, inout PoiMods poiMods, inout vec4 pbrOut, inout vec4 radianceOut)
{
#if !defined(POI_PASS_OUTLINE)
#if defined(POI_STYLIZED_SPECULAR) && defined(POI_STYLIZED_SPECULAR_LILTOON)
	lilReflectionOutputs(poiFragData, poiMesh, poiCam, poiLight, pbrOut, radianceOut);
#endif
#if defined(POI_MOCHIE_BRDF)
	MochieBRDFReflectionOutputs(poiFragData, mochieBRDFData, poiMesh, poiCam, poiLight, poiMods, pbrOut, radianceOut);
#endif
#endif
}
#endif
