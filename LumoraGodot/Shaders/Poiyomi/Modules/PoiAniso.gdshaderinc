// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Anisotropic highlights, per light (light() through PoiLightingDependantModules). light() cannot change
// the lit base color, so Replace reaches this light's share as its change times finalLighting.

//ifex _EnableAniso==0
#if defined(POI_ANISOTROPICS)

const float ANISO_POWER_SCALE = 1000.0;
const vec3 LUMA_COEFFS = vec3(0.299, 0.587, 0.114);

struct AnisoLayerData
{
	float strength;
	float power;
	float offset;
	float switchDirection;
	vec4 tint;
	float tintIndex;
	float offsetMapStrength;
	float toonMode;
	float edge;
	float blur;
};

struct AnisoGeometry
{
	vec3 tangent;
	vec3 binormal;
	vec3 normal;
};

struct AnisoLightData
{
	vec3 direction;
	vec3 color;
	float shadowMask;
	float nDotL;
};

AnisoLayerData AnisoLayerData_zero()
{
	return AnisoLayerData(0.0, 0.0, 0.0, 0.0, vec4(0.0), 0.0, 0.0, 0.0, 0.0, 0.0);
}

AnisoGeometry AnisoGeometry_zero()
{
	return AnisoGeometry(vec3(0.0), vec3(0.0), vec3(0.0));
}

AnisoLightData AnisoLightData_zero()
{
	return AnisoLightData(vec3(0.0), vec3(0.0), 0.0, 0.0);
}

float calculateAnisoShadowMask(in PoiLight poiLight, in PoiMods poiMods)
{
	float anisoLightMap = poiLight.lightMap;
	vec3 anisoRampedLightMap = poiLight.rampedLightMap / max(poiLight.attenDistance, 1e-4);
	float mask = mix(1.0, poiMax(anisoRampedLightMap * anisoLightMap), _AnisoHideInShadow);
	mask *= mix(1.0, poiLight.nDotL, _AnisoFadeByLightDirection);
	if (_AnisoGlobalMask > 0.0)
	{
		mask = customBlend(mask, poiMods.globalMask[int(_AnisoGlobalMask) - 1], _AnisoGlobalMaskBlendType);
	}
	return mask;
}

AnisoGeometry getAnisoGeometry(in PoiMesh poiMesh)
{
	AnisoGeometry geom = AnisoGeometry_zero();
	geom.binormal = mix(poiMesh.binormal[0], poiMesh.binormal[1], _AnisoNormalBlend);
	geom.tangent = mix(poiMesh.tangent[0], poiMesh.tangent[1], _AnisoNormalBlend);
	geom.normal = mix(poiMesh.normals[0], poiMesh.normals[1], _AnisoNormalBlend);
	return geom;
}

vec3 getAnisoLightColor(vec3 lightColor)
{
	return mix(vec3(dot(lightColor, LUMA_COEFFS)), lightColor, _AnisoUseLightColor);
}

vec3 getAnisoBaseColor(vec3 baseColor)
{
	return mix(vec3(1.0), baseColor, _AnisoUseBaseColor);
}

float calculateAnisoSpecular(in AnisoGeometry geom, in float offset, in float switchDirection, in vec3 viewDir, in vec3 lightDir, in float power, in float strength, in float shadowMask)
{
	vec3 anisoDir = mix(geom.binormal, geom.tangent, switchDirection);
	vec3 shiftedTangent = normalize(anisoDir + offset * geom.normal);
	vec3 halfVec = normalize(lightDir + viewDir);
	float dotTH = dot(shiftedTangent, halfVec);
	float sinTH = sqrt(max(0.0, 1.0 - dotTH * dotTH));
	float dirAtten = smoothstep(-1.0, 0.0, dotTH);
	return clamp(dirAtten * pow(sinTH, power * ANISO_POWER_SCALE) * strength, 0.0, 1.0) * shadowMask;
}

float applyAnisoStylization(float specular, float edge, float blur)
{
	float edgeMin = clamp(edge - blur * 0.5, 0.0, 1.0);
	float edgeMax = clamp(edge + blur * 0.5, 0.0, 1.0);
	return clamp((specular - edgeMin) / max(edgeMax - edgeMin + fwidth(specular), 0.001), 0.0, 1.0);
}

vec3 calculateAnisoLayer(in AnisoGeometry geom, in AnisoLayerData layer, in vec3 viewDir, in AnisoLightData lightData, in float offsetMap, in vec3 specMapColor, in PoiMods poiMods)
{
	float offset = layer.offset + layer.offsetMapStrength * offsetMap;
	float specular = calculateAnisoSpecular(geom, offset, layer.switchDirection, viewDir, lightData.direction, layer.power, layer.strength, lightData.shadowMask);
	specular = mix(specular, applyAnisoStylization(specular, layer.edge, layer.blur), layer.toonMode);
	return specular * specMapColor * poiThemeColor(poiMods, layer.tint.rgb, layer.tintIndex);
}

void processAnisoLight(inout PoiFragData poiFragData, inout PoiLight poiLight, in PoiCam poiCam, in PoiMods poiMods, in AnisoGeometry geom, in vec3 specMapColor, in float offsetMap, in AnisoLayerData layer0, in AnisoLayerData layer1, in AnisoLightData lightData)
{
	vec3 layer0Color = calculateAnisoLayer(geom, layer0, poiCam.eyeViewDir, lightData, offsetMap, specMapColor, poiMods);
	vec3 layer1Color = calculateAnisoLayer(geom, layer1, poiCam.eyeViewDir, lightData, offsetMap, specMapColor, poiMods);

	vec3 baseColorMix = getAnisoBaseColor(poiFragData.baseColor);
	vec3 lightColorMix = getAnisoLightColor(lightData.color);
	vec3 finalSpec = (layer0Color + layer1Color) * baseColorMix * lightColorMix;

	poiLight.finalLightAdd += max(vec3(0.0), finalSpec * _AnisoAdd);

	if (_AnisoReplace > 0.0)
	{
		vec3 replaceColor = (layer0Color + layer1Color) * baseColorMix * lightColorMix;
		float replaceMask = clamp(poiMax(replaceColor), 0.0, 1.0);
		vec3 replacedBase = mix(poiFragData.baseColor, replaceColor, _AnisoReplace * replaceMask);
		poiLight.finalLightAdd += (replacedBase - poiFragData.baseColor) * poiLight.finalLighting;
	}
}

void applyAnisotropics(inout PoiFragData poiFragData, inout PoiLight poiLight, in PoiCam poiCam, in PoiMesh poiMesh, in PoiMods poiMods)
{
#if defined(PROP_ANISOCOLORMAP)
	vec4 specMap = poiTexPan(_AnisoColorMap, poiUV(poiMesh.uv[int(_AnisoColorMapUV)], _AnisoColorMap_ST), _AnisoColorMapPan);
#else
	vec4 specMap = vec4(1.0, 1.0, 1.0, 0.0);
#endif

	float shadowMask = calculateAnisoShadowMask(poiLight, poiMods);
	AnisoGeometry geom = getAnisoGeometry(poiMesh);

	AnisoLayerData layer0 = AnisoLayerData(_Aniso0Strength, _Aniso0Power, _Aniso0Offset, _Aniso0SwitchDirection, _Aniso0Tint, _Aniso0TintIndex, _Aniso0OffsetMapStrength, _Aniso0ToonMode, _Aniso0Edge, _Aniso0Blur);
	AnisoLayerData layer1 = AnisoLayerData(_Aniso1Strength, _Aniso1Power, _Aniso1Offset, _Aniso1SwitchDirection, _Aniso1Tint, _Aniso1TintIndex, _Aniso1OffsetMapStrength, _Aniso1ToonMode, _Aniso1Edge, _Aniso1Blur);

	AnisoLightData mainLight = AnisoLightData(poiLight.direction, poiLight.directColor, shadowMask, poiLight.nDotL);

	processAnisoLight(poiFragData, poiLight, poiCam, poiMods, geom, specMap.rgb, specMap.a, layer0, layer1, mainLight);
}
#endif
//endex
