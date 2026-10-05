// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Backlight. Every term of the source's body reads the light, so all of it runs per light in
// applyBacklightLight; ApplyBacklight, frag's call, has nothing left to do without one.

//ifex _BacklightEnabled!=1
#if defined(POI_BACKLIGHT)
void ApplyBacklight(inout PoiFragData poiFragData, in PoiMesh poiMesh, inout PoiLight poiLight, in PoiCam poiCam, inout PoiMods poiMods)
{
}

#define POI_HAS_LIGHT_HOOK_BACKLIGHT
void applyBacklightLight(inout PoiLight poiLight, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods)
{
	// Color
	vec3 backlightColor = _BacklightColor.rgb;
#if defined(PROP_BACKLIGHTCOLORTEX)
	backlightColor *= poiTexPan(_BacklightColorTex, poiUV(poiMesh.uv[int(_BacklightColorTexUV)], _BacklightColorTex_ST), _BacklightColorTexPan).rgb;
#endif

	vec3 normal = mix(poiMesh.normals[0], poiMesh.normals[1], _BacklightNormalStrength);
	// Factor
	vec3 headDir = normalize(getCameraPosition(poiCam) - poiMesh.worldPos);
	float headDotLight = dot(headDir, poiLight.direction);
	float backlightFactor = pow(clamp(-headDotLight * 0.5 + 0.5, 0.0, 1.0), max(0.0, _BacklightDirectivity));
	float backlightLN = dot(normalize(-headDir * _BacklightViewStrength + poiLight.direction), normal) * 0.5 + 0.5;

	if (float(_BacklightReceiveShadow) >= 0.5)
	{
		backlightLN *= poiLight.lightMap;
	}

	backlightLN = poiEdgeLinear(backlightLN, _BacklightBorder, _BacklightBlur);
	float backlight = clamp(backlightFactor * backlightLN, 0.0, 1.0);
	backlight = poiMesh.isFrontFace == 0.0 && float(_BacklightBackfaceMask) >= 0.5 ? 0.0 : backlight;

	// Global Masking
	float backlightMask = 1.0;

	if (_BacklightGlobalMask > 0.0)
	{
		backlightMask = maskBlend(backlightMask, poiMods.globalMask[int(_BacklightGlobalMask) - 1], _BacklightGlobalMaskBlendType);
	}

	backlight *= backlightMask;

	if (_BacklightApplyGlobalMaskIndex > 0.0)
	{
		applyToGlobalMask(poiMods, _BacklightApplyGlobalMaskIndex - 1.0, _BacklightApplyGlobalMaskBlendType, backlight);
	}

	// Blend
	backlightColor = mix(backlightColor, backlightColor * poiFragData.baseColor, _BacklightMainStrength);
	poiLight.finalLightAdd += backlight * backlightColor * poiLight.directColor;
}
#endif
//endex
