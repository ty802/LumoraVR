// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Subsurface scattering, per light (light() through PoiLightingDependantModules).

//ifex _SubsurfaceScattering==0
#if defined(POI_SUBSURFACESCATTERING)
void applySubsurfaceScattering(in PoiCam poiCam, inout PoiLight poiLight, in PoiMesh poiMesh, in PoiFragData poiFragData, in PoiMods poiMods)
{
	vec4 SSS = vec4(1.0);
#if defined(PROP_SSSTHICKNESSMAP)
	SSS = poiTexPan(_SSSThicknessMap, poiUV(poiMesh.uv[int(_SSSThicknessMapUV)], _SSSThicknessMap_ST), _SSSThicknessMapPan);
#endif
	SSS.a = 1.0 - SSS.a;

	// The source rebuilds a point or spot light's direction from its position: the same vector.
	vec3 sssLightDir = poiLight.direction;

	// directColor hard-clips the transmitted light for point/spot, don't use the shadowAtten
	vec3 sssLightColor = poiLight.directColor;
	float sssReach = 1.0;
	if (!poiLight.isDirectional)
	{
		// The light's own color: on the add path directColor carries attenDistance.
		sssLightColor = poiLight.directColor / max(poiLight.attenDistance, 1e-4);
		sssReach = poiLight.attenDistance * CalculateLuminance(poiLight.lightCookie);
	}

	vec3 vLTLight = sssLightDir + poiMesh.normals[1] * _SSSDistortion;
	float flTDot = pow(clamp(dot(poiCam.eyeViewDir, -vLTLight), 0.0, 1.0), _SSSSpread) * _SSSStrength;
	vec3 fLT = vec3(flTDot * clamp(SSS.a - _SSSThicknessMod, 0.0, 1.0));

	float sssLightMap = poiLight.lightMap;
	float sssShadow = mix(1.0, sssLightMap, _SSSShadowStrength);

	float sssMask = 1.0;
	if (_SSSGlobalMaskIndex > 0.0)
	{
		sssMask = maskBlend(sssMask, poiMods.globalMask[int(_SSSGlobalMaskIndex) - 1], _SSSGlobalMaskBlendType);
	}

	vec3 sssTint = _SSSColor.rgb * SSS.rgb;
	vec3 sssLight = mix(vec3(1.0), sssLightColor, _SSSLightColorMix);

	poiLight.finalLightAdd += fLT * sssReach * sssLight * sssTint * sssShadow * mix(vec3(1.0), poiFragData.baseColor, _SSSBaseColorMix) * sssMask;
}
#endif
//endex
