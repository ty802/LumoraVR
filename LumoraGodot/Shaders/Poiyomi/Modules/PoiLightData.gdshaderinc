// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Poiyomi's light data. The source fills it once per pass from its main light (or the add pass's light)
// and the SH ambient. fragment() has no light: PoiLightDataSetup fills the light-free options and the
// ambient state, the engine's ambient standing in for the SH as a unit light that reaches no surface
// directly (lightType 0). PoiCalculateShading turns that state into poiLight.ambientTint. In light(),
// PoiLightDataSetupLight fills the data for each engine light: directional lights take the main light's
// path, omni and spot lights the add pass's (PoiLightDataSetupAdd). There the SH terms are zero, since
// the engine adds its ambient on its own. -xlinka

const vec3 POI_LIGHT_FALLBACK_DIRECTION = vec3(0.4, 1.0, 0.4);

// HLSL's smoothstep(0, edge, x) with a zero range is a step at 0; the engine's is undefined there.
float poiSmoothstepFromZero(float edge, float x)
{
	if (edge <= 0.0)
	{
		return x > 0.0 ? 1.0 : 0.0;
	}
	return smoothstep(0.0, edge, x);
}

// The source's light direction modes. lightDirection and lightColor are the engine light's (zero in
// fragment()). Forced directions turn the Poi light map into normalized nDotL, as in the source.
void poiLightDataDirection(inout PoiLight poiLight, in PoiMesh poiMesh, in PoiCam poiCam, vec3 lightDirection, vec3 lightColor, inout int lightMapMode)
{
	switch (int(_LightingDirectionMode))
	{
		case 0:
		{
			// The SH L1 gradient is dropped.
			poiLight.direction = CalculateLuminance(lightColor) * lightDirection;
			break;
		}
		case 1:
		{
			poiLight.direction = mat3(poiMesh.modelMatrix) * _LightngForcedDirection;
			if (lightMapMode == 0)
			{
				lightMapMode = 1;
			}
			break;
		}
		case 2:
		{
			poiLight.direction = _LightngForcedDirection;
			if (lightMapMode == 0)
			{
				lightMapMode = 1;
			}
			break;
		}
		case 3: // UTS
		{
			vec3 viewRow2 = vec3(poiCam.viewMatrix[0][2], poiCam.viewMatrix[1][2], poiCam.viewMatrix[2][2]);
			vec3 viewRow1 = vec3(poiCam.viewMatrix[0][1], poiCam.viewMatrix[1][1], poiCam.viewMatrix[2][1]);
			vec3 defaultLightDirection = normalize(viewRow2 + viewRow1);
			float hasDirection = any(notEqual(lightDirection, vec3(0.0))) ? 1.0 : 0.0;
			poiLight.direction = normalize(mix(defaultLightDirection, lightDirection, hasDirection));
			break;
		}
		case 4: // OpenLit
		{
			poiLight.direction = OpenLitLightingDirection(poiMesh, vec4(0.001, 0.002, 0.001, 0.0), lightDirection * OpenLitLuminance(lightColor));
			break;
		}
		case 5: // View Direction
		{
			vec3 upViewDir = vec3(poiCam.viewMatrix[0][1], poiCam.viewMatrix[1][1], poiCam.viewMatrix[2][1]);
			vec3 rightViewDir = vec3(poiCam.viewMatrix[0][0], poiCam.viewMatrix[1][0], poiCam.viewMatrix[2][0]);
			float yawOffset_Rads = radians(!IsInMirror(poiCam) ? -_LightingViewDirOffsetYaw : _LightingViewDirOffsetYaw);
			vec3 rotatedViewYaw = normalize(RotateAroundAxis(rightViewDir, upViewDir, yawOffset_Rads));
			vec3 rotatedViewCameraMeshOffset = RotateAroundAxis((poiCam.worldPos - poiMesh.worldPos), upViewDir, yawOffset_Rads);
			float pitchOffset_Rads = radians(!IsInMirror(poiCam) ? _LightingViewDirOffsetPitch : -_LightingViewDirOffsetPitch);
			vec3 rotatedViewPitch = RotateAroundAxis(rotatedViewCameraMeshOffset, rotatedViewYaw, pitchOffset_Rads);
			poiLight.direction = normalize(rotatedViewPitch);
			break;
		}
	}

	if (!any(notEqual(poiLight.direction, vec3(0.0))))
	{
		poiLight.direction = POI_LIGHT_FALLBACK_DIRECTION;
	}

	poiLight.direction = normalize(poiLight.direction);

	if (!any(notEqual(poiLight.directColor, vec3(0.0))) && !any(notEqual(poiLight.indirectColor, vec3(0.0))) && lightMapMode == 0)
	{
		lightMapMode = 1;
		if (_LightingDirectionMode == 0.0)
		{
			poiLight.direction = normalize(POI_LIGHT_FALLBACK_DIRECTION);
		}
	}
}

// Half vector and dot products, main light form.
void poiLightDataDots(inout PoiLight poiLight, in PoiMesh poiMesh, in PoiCam poiCam)
{
	poiLight.halfDir = normalize(poiLight.direction + poiCam.eyeViewDir);
	poiLight.vertexNDotL = dot(poiMesh.normals[0], poiLight.direction);
	poiLight.nDotL = dot(poiMesh.normals[1], poiLight.direction);
	poiLight.nDotLSaturated = clamp(poiLight.nDotL, 0.0, 1.0);
	poiLight.nDotLNormalized = (poiLight.nDotL + 1.0) * 0.5;
	float rawNDotV = dot(poiMesh.normals[1], poiCam.eyeViewDir);
	poiLight.nDotV = abs(rawNDotV);
	poiLight.nDotVSaturated = clamp(rawNDotV, 0.0, 1.0);
	poiLight.nDotVCentered = abs(dot(poiMesh.normals[1], poiCam.viewDir));
	poiLight.vertexNDotV = abs(dot(poiMesh.normals[0], poiCam.eyeViewDir));
	poiLight.nDotH = dot(poiMesh.normals[1], poiLight.halfDir);
	poiLight.vertexNDotH = max(0.00001, dot(poiMesh.normals[0], poiLight.halfDir));
	poiLight.lDotv = dot(poiLight.direction, poiCam.eyeViewDir);
	poiLight.lDotH = max(0.00001, dot(poiLight.direction, poiLight.halfDir));
}

// The main light's color chain after the light map: floors, min brightness, grayscale, cap, forced color
// and the post process lighting terms.
void poiLightDataColors(inout PoiLight poiLight, in PoiMods poiMods)
{
	poiLight.directColor = max(poiLight.directColor, vec3(0.0001));
	poiLight.indirectColor = max(poiLight.indirectColor, vec3(0.0001));

	if (_LightingColorMode == 3.0)
	{
		// OpenLit
		poiLight.directColor = max(poiLight.directColor, vec3(poiLight.lightingMinLightBrightness));
	}
	else
	{
		poiLight.directColor = PoiMinBrightness(poiLight.directColor, poiLight.lightingMinLightBrightness);
		poiLight.indirectColor = PoiMinBrightness(poiLight.indirectColor, poiLight.lightingMinLightBrightness);
	}

	poiLight.directColor = mix(poiLight.directColor, vec3(dot(poiLight.directColor, vec3(0.299, 0.587, 0.114))), poiLight.lightingMonochromatic);
	poiLight.indirectColor = mix(poiLight.indirectColor, vec3(dot(poiLight.indirectColor, vec3(0.299, 0.587, 0.114))), poiLight.lightingMonochromatic);

	if (poiLight.lightingCapEnabled != 0.0)
	{
		poiLight.directColor = min(poiLight.directColor, vec3(_LightingCap));
		poiLight.indirectColor = min(poiLight.indirectColor, vec3(_LightingCap));
	}

	if (_LightingForceColorEnabled >= 0.5)
	{
		poiLight.directColor = poiThemeColor(poiMods, _LightingForcedColor.rgb, _LightingForcedColorThemeIndex);
	}

	poiLight.directColor = max(poiLight.directColor * _PPLightingMultiplier, vec3(0.0));
	poiLight.directColor = max(poiLight.directColor + _PPLightingAddition, vec3(0.0));
	poiLight.indirectColor = max(poiLight.indirectColor * _PPLightingMultiplier, vec3(0.0));
	poiLight.indirectColor = max(poiLight.indirectColor + _PPLightingAddition, vec3(0.0));
}

void poiLightDataDebugMain(in PoiLight poiLight, inout PoiShadingParams params)
{
	//ifex _LightDataDebugEnabled==0
	if (_LightDataDebugEnabled >= 0.5)
	{
		switch (int(_LightingDebugVisualize))
		{
			case 0: // Direct Light Color
			{
				params.debugColor = vec4(poiLight.directColor, 1.0);
				break;
			}
			case 1: // Indirect Light Color
			{
				params.debugColor = vec4(poiLight.indirectColor, 1.0);
				break;
			}
			case 2: // Light Map
			{
				params.debugColor = vec4(vec3(poiLight.lightMap), 1.0);
				break;
			}
			case 3: // Attenuation
			{
				params.debugColor = vec4(vec3(poiLight.attenDistance), 1.0);
				break;
			}
			case 12: // Shadow
			{
				params.debugColor = vec4(vec3(poiLight.attenShadows), 1.0);
				break;
			}
			case 4: // N Dot L
			{
				params.debugColor = vec4(vec3(poiLight.nDotLNormalized), 1.0);
				break;
			}
			case 5:
			{
				params.debugColor = vec4(poiLight.halfDir, 1.0);
				break;
			}
			case 6:
			{
				params.debugColor = vec4(poiLight.direction, 1.0);
				break;
			}
		}
	}
	//endex
}

// Omni and spot lights: the source's add pass. The engine's ATTENUATION folds the shadow into the
// distance falloff, so both ride the attenuation curve and the cast shadow strength has nothing left to
// scale. Directional lights never come here, so _DisableDirectionalInAdd has no effect.
void PoiLightDataSetupAdd(inout PoiLight poiLight, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, in PoiMods poiMods, inout PoiShadingParams params, vec3 lightDirection, vec3 lightColor, float attenuation)
{
	if (_LightingAdditiveEnable < 0.5)
	{
		params.exitCode = 1u;
		return;
	}

	poiLight.isAdditive = 1;

	poiLight.direction = lightDirection;

	poiLight.attenStrength = _LightingAdditiveCastedShadows;

	poiLight.attenShadows = 1.0;
	poiLight.attenDistance = poiSampleCurve4(_LightingAdditiveAttenCurve, attenuation);
	poiLight.lightCookie = vec3(1.0);
	poiLight.attenShadowsStrength = mix(1.0, poiLight.attenShadows, poiLight.attenStrength);

	vec3 additiveColorNoShadows = lightColor * poiLight.attenDistance;
	poiLight.directColor = _LightingAdditiveLimited != 0.0 ? MaxLuminance(additiveColorNoShadows, _LightingAdditiveLimit) : additiveColorNoShadows;

	poiLight.indirectColor = mix(vec3(0.0), additiveColorNoShadows, _LightingAdditivePassthrough);
	poiLight.indirectColor = _LightingAdditiveLimited != 0.0 ? MaxLuminance(poiLight.indirectColor, _LightingAdditiveLimit) : poiLight.indirectColor;

	poiLight.directColor = mix(poiLight.directColor, vec3(dot(poiLight.directColor, vec3(0.299, 0.587, 0.114))), _LightingAdditiveMonochromatic);
	poiLight.indirectColor = mix(poiLight.indirectColor, vec3(dot(poiLight.indirectColor, vec3(0.299, 0.587, 0.114))), _LightingAdditiveMonochromatic);

	poiLight.halfDir = normalize(poiLight.direction + poiCam.eyeViewDir);
	poiLight.nDotL = dot(poiMesh.normals[1], poiLight.direction);
	poiLight.nDotLSaturated = clamp(poiLight.nDotL, 0.0, 1.0);
	poiLight.nDotLNormalized = (poiLight.nDotL + 1.0) * 0.5;
	poiLight.nDotV = abs(dot(poiMesh.normals[1], poiCam.eyeViewDir));
	poiLight.nDotVCentered = abs(dot(poiMesh.normals[1], poiCam.viewDir));
	poiLight.nDotH = dot(poiMesh.normals[1], poiLight.halfDir);
	poiLight.lDotv = dot(poiLight.direction, poiCam.eyeViewDir);
	poiLight.lDotH = dot(poiLight.direction, poiLight.halfDir);
	poiLight.vertexNDotL = dot(poiMesh.normals[0], poiLight.direction);
	poiLight.vertexNDotV = abs(dot(poiMesh.normals[0], poiCam.eyeViewDir));
	poiLight.vertexNDotH = max(0.00001, dot(poiMesh.normals[0], poiLight.halfDir));

	switch (int(_LightingMapMode))
	{
		// Saturated NDotL
		case 2:
		{
			poiLight.lightMap = poiLight.nDotLSaturated;
			break;
		}
		// Casted Shadows Only
		case 3:
		{
			poiLight.lightMap = 1.0;
			break;
		}
		default:
		{
			poiLight.lightMap = poiLight.nDotLNormalized;
			break;
		}
	}

	poiLight.lightMap *= poiLight.detailShadow;
	poiLight.lightMapNoAttenuation = poiLight.lightMap;
	poiLight.lightMap *= CalculateLuminance(poiLight.lightCookie) * poiLight.attenShadowsStrength;
	// backwards compatibility
	poiLight.attenuation = poiLight.lightMap;
	poiLight.additiveShadow = poiLight.lightMap;

	//ifex _LightDataDebugEnabled==0
	if (_LightDataDebugEnabled >= 0.5)
	{
		switch (int(_LightingDebugVisualize))
		{
			case 7:
			{
				params.debugColor = vec4(poiLight.directColor, 1.0);
				break;
			}
			case 8:
			{
				params.debugColor = vec4(vec3(poiLight.attenDistance), 1.0);
				break;
			}
			case 9:
			{
				params.debugColor = vec4(vec3(poiLight.attenShadows), 1.0);
				break;
			}
			case 10:
			{
				params.debugColor = vec4(vec3(poiLight.nDotLNormalized), 1.0);
				break;
			}
			case 11:
			{
				params.debugColor = vec4(poiLight.halfDir, 1.0);
				break;
			}
			case 13:
			{
				params.debugColor = vec4(vec3(poiLight.lightMap), 1.0);
				break;
			}
		}
	}
	//endex

	params.exitCode = 0u;
}

// fragment(). Vertex lights and the SH are gone; the light is the ambient state (see the file header):
// direct and indirect colors are how much of a unit ambient each color mode puts in them when no realtime
// light exists (Standard keeps its with-a-light split: the ambient is indirect only), the direction is the
// mode's no-light direction, and every surface is unlit by it (nDotL -1, light map 0, or 1 for Casted
// Shadows Only, which has no N dot L term). The color chain then runs on it as on any light, so min
// brightness, grayscale, the cap, the forced color and the post process terms reach the ambient tint.
void PoiLightDataSetup(inout PoiLight poiLight, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, inout PoiShadingParams poiShadingParams)
{
	poiLight.lightType = 0;
	poiLight.isAdditive = 0;
	poiLight.isDirectional = false;
	poiLight.ambientTint = vec3(1.0);

	if (_LightingVertexLightingEnabled >= 0.5)
	{
		poiFragData.toggleVertexLights = 1.0;
	}
	if (IsInMirror(poiCam) && _LightingMirrorVertexLightingEnabled < 0.5)
	{
		poiFragData.toggleVertexLights = 0.0;
	}

	poiLight.attenStrength = _LightingCastedShadows;
	poiLight.attenShadows = 1.0;
	poiLight.attenDistance = 1.0;
	poiLight.lightCookie = vec3(1.0);
	poiLight.attenShadowsStrength = mix(1.0, poiLight.attenShadows, poiLight.attenStrength);

	switch (int(_LightingColorMode))
	{
		case 0: // Poi Custom: the SH blend and the SH sampled at the normal
		{
			poiLight.directColor = vec3(1.0);
			poiLight.indirectColor = vec3(1.0);
			break;
		}
		case 1: // Standard
		{
			poiLight.directColor = vec3(0.0);
			poiLight.indirectColor = vec3(1.0);
			break;
		}
		case 2: // UTS style; the 0.05 floor and the clamp to 1 have no unit counterpart
		{
			poiLight.indirectColor = vec3(_Unlit_Intensity);
			poiLight.directColor = poiLight.indirectColor;
			break;
		}
		case 3: // OpenLit: SH toward and away from the light
		{
			poiLight.directColor = vec3(1.0);
			poiLight.indirectColor = vec3(1.0);
			break;
		}
	}

	int lightMapMode = int(_LightingMapMode);
	poiLightDataDirection(poiLight, poiMesh, poiCam, vec3(0.0), vec3(0.0), lightMapMode);
	poiLightDataDots(poiLight, poiMesh, poiCam);

	poiLight.vertexNDotL = -1.0;
	poiLight.nDotL = -1.0;
	poiLight.nDotLSaturated = 0.0;
	poiLight.nDotLNormalized = 0.0;

	float lightCookieLuminance = CalculateLuminance(poiLight.lightCookie);
	poiLight.lightMapNoAttenuation = lightMapMode == 3 ? 1.0 : 0.0;
	poiLight.lightMap = lightMapMode == 3 ? poiLight.attenShadowsStrength * lightCookieLuminance : 0.0;

	poiLight.lightMapNoAttenuation *= poiLight.detailShadow;
	poiLight.lightMap *= poiLight.detailShadow;
	// backwards compatibility
	poiLight.attenuation = poiLight.lightMap;
	poiLight.additiveShadow = poiLight.lightMap;

	poiLightDataColors(poiLight, poiMods);

	// No light: the raw light color PoiCalculateShading reads from rampedLightMap is black.
	poiLight.rampedLightMap = vec3(0.0);

	poiLightDataDebugMain(poiLight, poiShadingParams);

	poiShadingParams.exitCode = 0u;
}

// light(), once per engine light. lightDirection: world space, toward the light. lightColor: LIGHT_COLOR / PI
// (the source's _LightColor0 scale). attenuation: ATTENUATION (directional: the shadow; omni and spot:
// distance falloff times shadow). Fills the source's main light data (directional) or add pass data.
// The light's raw color travels to PoiCalculateShading in rampedLightMap, which every lighting mode
// writes before reading.
void PoiLightDataSetupLight(inout PoiLight poiLight, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, inout PoiShadingParams poiShadingParams, vec3 lightDirection, vec3 lightColor, float attenuation, bool isDirectional)
{
	poiLight.isDirectional = isDirectional;
	poiLight.lightType = isDirectional ? int(POI_DIRECTIONAL) : int(POI_POINT_SPOT);
	poiLight.isAdditive = 0;
	poiShadingParams.exitCode = 0u;

	// View terms the add pass keeps from the main setup.
	float rawNDotV = dot(poiMesh.normals[1], poiCam.eyeViewDir);
	poiLight.nDotVSaturated = clamp(rawNDotV, 0.0, 1.0);

	if (!isDirectional)
	{
		PoiLightDataSetupAdd(poiLight, poiFragData, poiMesh, poiCam, poiMods, poiShadingParams, lightDirection, lightColor, attenuation);
		poiLight.rampedLightMap = lightColor;
		return;
	}

	bool lightExists = any(greaterThanEqual(lightColor, vec3(0.002)));

	poiLight.attenStrength = _LightingCastedShadows;
	// A directional light's attenuation is its shadow; cookies arrive in LIGHT_COLOR.
	// _LightingShadowSoftness blurred the screen space shadow map: no counterpart.
	poiLight.attenShadows = attenuation;
	poiLight.attenDistance = 1.0;
	poiLight.lightCookie = vec3(1.0);
	poiLight.attenShadowsStrength = mix(1.0, poiLight.attenShadows, poiLight.attenStrength);

	// The SH share of every mode is the engine's ambient, so the indirect color is zero here.
	poiLight.indirectColor = vec3(0.0);
	switch (int(_LightingColorMode))
	{
		case 0: // Poi Custom Light Color: with the SH terms at zero the luminance blend keeps the light
		{
			poiLight.directColor = lightColor * poiLight.attenDistance;
			break;
		}
		case 1: // More standard approach to light color
		{
			poiLight.directColor = lightExists ? lightColor * poiLight.attenDistance : vec3(0.0);
			break;
		}
		case 2: // UTS style
		{
			poiLight.directColor = max(poiLight.indirectColor, lightColor * poiLight.attenDistance);
			break;
		}
		case 3: // OpenLit
		{
			poiLight.directColor = lightColor * poiLight.attenDistance;
			break;
		}
	}

	int lightMapMode = int(_LightingMapMode);
	poiLightDataDirection(poiLight, poiMesh, poiCam, lightDirection, lightColor, lightMapMode);
	poiLightDataDots(poiLight, poiMesh, poiCam);

	float lightCookieLuminance = CalculateLuminance(poiLight.lightCookie);
	switch (lightMapMode)
	{
		// Poi special light map; the SH top, bottom and normal terms are zero.
		case 0:
		{
			vec3 greyScaleVector = vec3(0.33333, 0.33333, 0.33333);
			float bw_lightColor = dot(poiLight.directColor, greyScaleVector);
			float bw_directLighting = (poiLight.nDotLNormalized * bw_lightColor * poiLight.attenShadowsStrength) * lightCookieLuminance;
			float bw_directLightingNoAtten = poiLight.nDotLNormalized * bw_lightColor;
			float lightDifference = bw_lightColor;

			poiLight.lightMap = poiSmoothstepFromZero(lightDifference, bw_directLighting);
			poiLight.lightMapNoAttenuation = poiSmoothstepFromZero(lightDifference, bw_directLightingNoAtten);
			break;
		}
		// Normalized nDotL
		case 1:
		{
			poiLight.lightMapNoAttenuation = poiLight.nDotLNormalized;
			poiLight.lightMap = poiLight.nDotLNormalized * poiLight.attenShadowsStrength * lightCookieLuminance;
			break;
		}
		// Saturated nDotL
		case 2:
		{
			poiLight.lightMapNoAttenuation = poiLight.nDotLSaturated;
			poiLight.lightMap = poiLight.nDotLSaturated * poiLight.attenShadowsStrength * lightCookieLuminance;
			break;
		}
		case 3:
		{
			poiLight.lightMapNoAttenuation = 1.0;
			poiLight.lightMap = poiLight.attenShadowsStrength * lightCookieLuminance;
			break;
		}
		case 4:
		{
#if defined(PROP_LIGHTDATASDFMAP)
			vec2 lightDataSDFMap = vec2(1.0);
			vec2 sdfUV = poiUV(poiMesh.uv[int(_LightDataSDFMapUV)], _LightDataSDFMap_ST);
			if (_LightDataSDFMapLOD > 0.0)
			{
				float sdfLod = pow(_LightDataSDFMapLOD, 4.0);
				lightDataSDFMap = poiTexPanGrad(_LightDataSDFMap, sdfUV, _LightDataSDFMapPan, max(poiMesh.dx, vec2(sdfLod)), max(poiMesh.dy, vec2(sdfLod))).rg;
			}
			else
			{
				lightDataSDFMap = poiTexPan(_LightDataSDFMap, sdfUV, _LightDataSDFMapPan).rg;
			}

			poiLight.lightMapNoAttenuation = poiLight.nDotLSaturated;
			vec3 faceR = mat3(poiMesh.modelMatrix) * vec3(-1.0, 0.0, 0.0);
			float LdotR = dot(poiLight.direction.xz, faceR.xz);
			float sdf = LdotR < 0.0 ? lightDataSDFMap.g : lightDataSDFMap.r;

			vec3 faceF = mat3(poiMesh.modelMatrix) * vec3(0.0, 0.0, 1.0);
			faceF.y *= _LightDataSDFBlendY;
			faceF = dot(faceF, faceF) == 0.0 ? vec3(0.0) : normalize(faceF);
			vec3 faceL = poiLight.direction;
			faceL.y *= _LightDataSDFBlendY;
			faceL = dot(faceL, faceL) == 0.0 ? vec3(0.0) : normalize(faceL);

			float lnSDF = dot(faceL, faceF);
			poiLight.lightMapNoAttenuation = clamp(lnSDF * 0.5 + sdf * 0.5 + 0.25, 0.0, 1.0);
			poiLight.lightMap = clamp(lnSDF * 0.5 + sdf * 0.5 + 0.25, 0.0, 1.0) * poiLight.attenShadowsStrength * lightCookieLuminance;
#else
			poiLight.lightMapNoAttenuation = poiLight.nDotLNormalized;
			poiLight.lightMap = poiLight.nDotLNormalized * poiLight.attenShadowsStrength * lightCookieLuminance;
#endif
			break;
		}
	}

	poiLight.lightMapNoAttenuation *= poiLight.detailShadow;
	poiLight.lightMap *= poiLight.detailShadow;
	// backwards compatibility
	poiLight.attenuation = poiLight.lightMap;
	poiLight.additiveShadow = poiLight.lightMap;

	poiLightDataColors(poiLight, poiMods);

	// The SH is the engine's: no indirect share per light.
	poiLight.indirectColor = vec3(0.0);

	poiLight.rampedLightMap = lightColor;

	poiLightDataDebugMain(poiLight, poiShadingParams);

	poiShadingParams.exitCode = 0u;
}
