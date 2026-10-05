// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Dissolve: the fragment dissolve and the helpers the vertex height offset shares. The geometry shader
// dissolve is not ported.

#if !defined(POI_FX_SMOOTHSTEP)
#define POI_FX_SMOOTHSTEP
// smoothstep as the source evaluates it: reversed or equal edges stay defined.
float poiFxSmoothstep(float edge0, float edge1, float x)
{
	float t = clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
	return t * t * (3.0 - 2.0 * t);
}
#endif

#if defined(POI_DISSOLVE)
const float DISSOLVE_BLEND_MAX = 0.999999;

float dissolveDotNoise(vec3 position)
{
	const float PHI = 1.618033988;
	// Columns of the source's row-major GOLD.
	const mat3 GOLD = mat3(
		vec3(-0.571464913, -0.278044873, 0.772087367),
		vec3(0.814921382, -0.303026659, 0.494042493),
		vec3(0.096597072, 0.911518454, 0.399753815));
	vec3 rotatedPosition = GOLD * position;
	vec3 scaledRotatedPosition = PHI * (position * GOLD);
	return (dot(cos(rotatedPosition), sin(scaledRotatedPosition)) + 3.0) / 6.0;
}

float getDissolveDetailNoise(vec3 worldPos, vec2 uv)
{
	float detailNoise = 0.0;
	if (_DissolveDetailNoiseMode == 0.0)
	{
#if defined(PROP_DISSOLVEDETAILNOISE)
		vec2 detailUV = uv * _DissolveDetailNoise_ST.xy + _DissolveDetailNoise_ST.zw + POI_TIME.x * _DissolveDetailNoisePan;
		detailNoise = poiTexLod(_DissolveDetailNoise, detailUV, 0.0).r;
#endif
		detailNoise = _DissolveInvertDetailNoise != 0.0 ? 1.0 - detailNoise : detailNoise;
	}
	else
	{
		vec3 noisePos = worldPos * _DissolveDotNoiseScale.xyz + _DissolveDotNoiseOffset.xyz + POI_TIME.y * _DissolveDotNoiseSpeed.xyz;
		detailNoise = dissolveDotNoise(noisePos);
		detailNoise = _DissolveInvertDotNoise != 0.0 ? 1.0 - detailNoise : detailNoise;
	}
	return detailNoise;
}

struct DissolveEdgeInput
{
	float gradient;
	float dissolveAmount;
	float detailNoise;
	float detailStrength;
	float fwidthGradient;
	float heightOffset;
};

struct DissolveEdgeOutput
{
	float distFromEdge;
	float dissolveAlpha;
	float edgeAlpha;
};

DissolveEdgeOutput getDissolveEdge(DissolveEdgeInput input_)
{
	DissolveEdgeOutput output_;
	output_.distFromEdge = 0.0;
	output_.dissolveAlpha = 0.0;
	output_.edgeAlpha = 0.0;

	float softness = _DissolveSoftness * 0.5 + input_.fwidthGradient;
	float baseThreshold = input_.dissolveAmount + input_.heightOffset;
	float baseDistFromEdge = input_.gradient - baseThreshold;
	float edgeMaskFactor = clamp(-baseDistFromEdge / (softness + 0.001), 0.0, 1.0);
	float noiseMask = mix(1.0, edgeMaskFactor, _DissolveDetailEdgeMask);

	float noiseOffset = (input_.detailNoise - 0.5) * input_.detailStrength * noiseMask;
	float localThreshold = baseThreshold + noiseOffset;

	if (_DissolveWaveMode >= 0.5)
	{
		float halfWidth = _DissolveWaveWidth * 0.5;
		float localHalfWidth = max(halfWidth + noiseOffset, 0.001);
		float dist = 0.0;
		float waveAmount = input_.dissolveAmount;
		int waveBounds = int(_DissolveWaveBounds);
		if (waveBounds == 0)
		{
			waveAmount = remap(waveAmount, 0.0, 1.0, -halfWidth, 1.0 + halfWidth);
			dist = abs(input_.gradient - waveAmount);
		}
		else if (waveBounds == 2)
		{
			float diff = input_.gradient - waveAmount;
			diff = diff - round(diff);
			dist = abs(diff);
		}
		else
		{
			dist = abs(input_.gradient - waveAmount);
		}
		output_.distFromEdge = dist - localHalfWidth;
		output_.dissolveAlpha = 1.0 - poiFxSmoothstep(localHalfWidth - softness, localHalfWidth + softness, dist);
	}
	else
	{
		output_.distFromEdge = input_.gradient - localThreshold;
		output_.dissolveAlpha = 1.0 - poiFxSmoothstep(localThreshold - softness, localThreshold + softness, input_.gradient);
	}

	float peakPosition = output_.distFromEdge + softness;
	float edgeRange = max(_DissolveEdgeWidth + softness, 0.001);
	output_.edgeAlpha = clamp(1.0 - abs(peakPosition) / edgeRange, 0.0, 1.0);

	return output_;
}

float getUVTileDissolveAlpha(vec2 uv)
{
	vec2 udim = floor(uv);
	vec4 xMask = vec4(
		float(udim.x >= 0.0 && udim.x < 1.0),
		float(udim.x >= 1.0 && udim.x < 2.0),
		float(udim.x >= 2.0 && udim.x < 3.0),
		float(udim.x >= 3.0 && udim.x < 4.0));
	float alpha = 0.0;
	alpha += float(udim.y >= 0.0 && udim.y < 1.0) * dot(vec4(_UVTileDissolveAlpha_Row0_0, _UVTileDissolveAlpha_Row0_1, _UVTileDissolveAlpha_Row0_2, _UVTileDissolveAlpha_Row0_3), xMask);
	alpha += float(udim.y >= 1.0 && udim.y < 2.0) * dot(vec4(_UVTileDissolveAlpha_Row1_0, _UVTileDissolveAlpha_Row1_1, _UVTileDissolveAlpha_Row1_2, _UVTileDissolveAlpha_Row1_3), xMask);
	alpha += float(udim.y >= 2.0 && udim.y < 3.0) * dot(vec4(_UVTileDissolveAlpha_Row2_0, _UVTileDissolveAlpha_Row2_1, _UVTileDissolveAlpha_Row2_2, _UVTileDissolveAlpha_Row2_3), xMask);
	alpha += float(udim.y >= 3.0 && udim.y < 4.0) * dot(vec4(_UVTileDissolveAlpha_Row3_0, _UVTileDissolveAlpha_Row3_1, _UVTileDissolveAlpha_Row3_2, _UVTileDissolveAlpha_Row3_3), xMask);
	return alpha;
}

float getContinuousDissolve()
{
	float t = fract(POI_TIME.y * _ContinuousDissolveSpeed + _ContinuousDissolvePhase);
	float wave = 0.0;
	int waveType = int(_ContinuousDissolveWave);
	if (waveType == 0)
	{
		wave = sin(t * 6.28318) * 0.5 + 0.5;
	}
	else if (waveType == 1)
	{
		wave = 1.0 - abs(t * 2.0 - 1.0);
	}
	else if (waveType == 2)
	{
		wave = t;
	}
	else if (waveType == 3)
	{
		wave = t < 0.5 ? 0.0 : 1.0;
	}
	return mix(_ContinuousDissolveRange.x, _ContinuousDissolveRange.y, wave);
}

float getPointToPointGradient(vec3 localPos, vec3 worldPos, vec3 vertexColor, vec2 uv)
{
	vec3 start = _DissolveStartPoint.xyz;
	vec3 end = _DissolveEndPoint.xyz;
	float lineLength = distance(start, end);
	vec3 direction = lineLength > EPSILON ? (end - start) / lineLength : vec3(0.0, 1.0, 0.0);
	vec3 pos = localPos;
	int space = int(_DissolveP2PWorldLocal);
	if (space == 1)
	{
		pos = worldPos;
	}
	else if (space == 2)
	{
		pos = vertexColor;
	}
	else if (space == 3)
	{
		pos = vec3(uv, 0.0);
	}
	float projection = dot(pos - start, direction);
	float gradient = projection / (lineLength + EPSILON);
	return clamp(gradient, 0.0, 1.0);
}

float getSphericalGradient(vec3 localPos)
{
	float fragDistance = distance(_SphericalDissolveCenter, localPos);
	float gradient = fragDistance / (_SphericalDissolveRadius + EPSILON);
	gradient = _SphericalDissolveInvert != 0.0 ? 1.0 - gradient : gradient;
	return clamp(gradient, 0.0, 1.0);
}

// fragment() has no light of its own: mode 3 reads the direction the fragment-side light setup left in
// poiLight.direction, and the dot products are taken here from the normals as the source defines them.
float getCenterOutGradient(in PoiMesh poiMesh, in PoiLight poiLight, in PoiCam poiCam)
{
	float gradient = 0.5;
	int mode = int(_CenterOutDissolveMode);
	if (mode == 1)
	{
		float nDotVCentered = abs(dot(poiMesh.normals[1], poiCam.viewDir));
		gradient = mix(abs(dot(poiMesh.normals[0], poiCam.viewDir)), nDotVCentered, _CenterOutDissolveNormals);
	}
	else if (mode == 2)
	{
		gradient = dot(normalize(_CenterOutDissolveDirection), mix(poiMesh.normals[0], poiMesh.normals[1], _CenterOutDissolveNormals)) * 0.5 + 0.5;
	}
	else if (mode == 3)
	{
		float vertexNDotL = dot(poiMesh.normals[0], poiLight.direction);
		float nDotL = dot(poiMesh.normals[1], poiLight.direction);
		gradient = mix(vertexNDotL, nDotL, _CenterOutDissolveNormals) * 0.5 + 0.5;
	}
	gradient = clamp(gradient, 0.0, 1.0);
	gradient = _CenterOutDissolveLinear != 0.0 ? 1.0 - acos(gradient) * 0.63662 : gradient;
	gradient = _CenterOutDissolveInvert != 0.0 ? gradient : 1.0 - gradient;
	return gradient;
}

float getDissolveGradient(vec3 localPos, vec3 worldPos, vec3 vertexColor, vec2 uv, float noiseTextureSample)
{
	float gradient = 0.0;
	int dissolveType = int(_DissolveType);
	if (dissolveType == 2)
	{
		gradient = getPointToPointGradient(localPos, worldPos, vertexColor, uv);
	}
	else if (dissolveType == 3)
	{
		gradient = getSphericalGradient(localPos);
	}
	else if (dissolveType == 4)
	{
		gradient = 0.5;
	}
	else
	{
		gradient = _DissolveInvertNoise != 0.0 ? 1.0 - noiseTextureSample : noiseTextureSample;
	}
	return clamp(gradient, 0.0, 1.0);
}

float getDissolveAmount(vec2 uvTileUV, float dissolveMask)
{
	float dissolveAmount = _DissolveAlpha;
	if (_UVTileDissolveEnabled >= 0.5)
	{
		dissolveAmount += getUVTileDissolveAlpha(uvTileUV);
	}
	if (_ContinuousDissolveEnabled >= 0.5)
	{
		float continuous = getContinuousDissolve();
		int continuousBlend = int(_ContinuousDissolveBlend);
		if (continuousBlend == 0)
		{
			dissolveAmount = continuous;
		}
		else if (continuousBlend == 1)
		{
			dissolveAmount = clamp(dissolveAmount + continuous, 0.0, 1.0);
		}
		else if (continuousBlend == 2)
		{
			dissolveAmount = dissolveAmount * continuous;
		}
	}
	dissolveAmount = clamp(dissolveAmount, 0.0, 1.0);
	dissolveAmount *= dissolveMask;
	return dissolveAmount;
}

void applyDissolve(inout PoiFragData poiFragData, in PoiMesh poiMesh, inout PoiMods poiMods, in PoiCam poiCam, in PoiLight poiLight)
{
	float dissolveMask = 1.0;
#if defined(PROP_DISSOLVEMASK)
	dissolveMask = poiTexPan(_DissolveMask, poiUV(poiMesh.uv[int(_DissolveMaskUV)], _DissolveMask_ST), _DissolveMaskPan).r;
#endif

	if (_DissolveUseVertexColors > 0.0)
	{
		dissolveMask = ceil(poiMesh.vertexColor[max(int(_DissolveUseVertexColors) - 1, 0)] * 100000.0) / 100000.0;
	}

	if (_DissolveMaskGlobalMask > 0.0)
	{
		dissolveMask = maskBlend(dissolveMask, poiMods.globalMask[int(_DissolveMaskGlobalMask) - 1], _DissolveMaskGlobalMaskBlendType);
	}

	if (_DissolveMaskInvert >= 0.5)
	{
		dissolveMask = 1.0 - dissolveMask;
	}

#if defined(PROP_DISSOLVETOTEXTURE)
	vec4 dissolveToTexture = poiTexPan(_DissolveToTexture, poiUV(poiMesh.uv[int(_DissolveToTextureUV)], _DissolveToTexture_ST), _DissolveToTexturePan) * vec4(poiThemeColor(poiMods, _DissolveTextureColor.rgb, _DissolveTextureColorThemeIndex), _DissolveTextureColor.a);
#else
	vec4 dissolveToTexture = vec4(poiThemeColor(poiMods, _DissolveTextureColor.rgb, _DissolveTextureColorThemeIndex), _DissolveTextureColor.a);
#endif
	float dissolveNoise = 1.0;
#if defined(PROP_DISSOLVENOISETEXTURE)
	dissolveNoise = poiTexPan(_DissolveNoiseTexture, poiUV(poiMesh.uv[int(_DissolveNoiseTextureUV)], _DissolveNoiseTexture_ST), _DissolveNoiseTexturePan).r;
#endif
	dissolveNoise = _DissolveInvertNoise != 0.0 ? 1.0 - dissolveNoise : dissolveNoise;

	float detailNoise = 0.0;
	float detailStrength = 0.0;
	if (_DissolveDetailNoiseEnabled >= 0.5)
	{
		detailNoise = getDissolveDetailNoise(poiMesh.worldPos, poiMesh.uv[int(_DissolveDetailNoiseUV)]);
		detailStrength = _DissolveDetailStrength;
	}

	float dissolveAmount = _DissolveAlpha;
	if (_UVTileDissolveEnabled >= 0.5)
	{
		dissolveAmount += getUVTileDissolveAlpha(poiMesh.uv[int(_UVTileDissolveUV)]);
	}
	if (_ContinuousDissolveEnabled >= 0.5)
	{
		float continuous = getContinuousDissolve();
		int continuousBlend = int(_ContinuousDissolveBlend);
		if (continuousBlend == 0)
		{
			dissolveAmount = continuous;
		}
		else if (continuousBlend == 1)
		{
			dissolveAmount = clamp(dissolveAmount + continuous, 0.0, 1.0);
		}
		else if (continuousBlend == 2)
		{
			dissolveAmount = dissolveAmount * continuous;
		}
	}
	dissolveAmount = clamp(dissolveAmount, 0.0, 1.0);
	dissolveAmount *= dissolveMask;
	detailStrength = clamp(detailStrength, 0.0, 1.0);

	float gradient = dissolveNoise;
	int dissolveType = int(_DissolveType);
	if (dissolveType == 2)
	{
		gradient = getPointToPointGradient(poiMesh.localPos.rgb, poiMesh.worldPos, poiMesh.vertexColor.rgb, poiMesh.uv[int(_DissolveP2PUV)]);
	}
	else if (dissolveType == 3)
	{
		gradient = getSphericalGradient(poiMesh.localPos.xyz);
	}
	else if (dissolveType == 4)
	{
		gradient = getCenterOutGradient(poiMesh, poiLight, poiCam);
	}

	if (_DissolveDebugGradient >= 0.5)
	{
		vec3 debugColor = mix(mix(mix(vec3(gradient), vec3(1.0, 0.0, 0.0), float(gradient <= 0.0)), vec3(0.0, 1.0, 0.0), float(gradient >= 1.0)), vec3(1.0, 1.0, 0.0), float(abs(gradient - _DissolveAlpha) < 0.003));
		poiFragData.baseColor = debugColor;
		return;
	}

	gradient = clamp(gradient, 0.0, 1.0);
	float fwidthGradient = fwidth(gradient);

	float softness = _DissolveSoftness * 0.5 + fwidthGradient;
	float edgeWidth = _DissolveEdgeEnabled != 0.0 ? _DissolveEdgeWidth : 0.0;
	float detailPadding = 0.5 * detailStrength * (1.0 - _DissolveDetailEdgeMask);
	float totalPadding = max(2.0 * softness + edgeWidth + detailPadding, 0.01);
	float remappedDissolveAmount = (_DissolveWaveMode >= 0.5 && _DissolveWaveBounds == 2.0)
		? dissolveAmount
		: mix(-totalPadding, 1.0 + totalPadding, dissolveAmount);

	DissolveEdgeInput edgeInput;
	edgeInput.gradient = gradient;
	edgeInput.dissolveAmount = remappedDissolveAmount;
	edgeInput.detailNoise = detailNoise;
	edgeInput.detailStrength = detailStrength;
	edgeInput.fwidthGradient = fwidthGradient;
	edgeInput.heightOffset = 0.0;

	DissolveEdgeOutput edgeOutput = getDissolveEdge(edgeInput);
	float distFromEdge = edgeOutput.distFromEdge;
	float dissolveAlpha = edgeOutput.dissolveAlpha;

	if (_DissolveHueShiftEnabled >= 0.5)
	{
		dissolveToTexture.rgb = hueShift(dissolveToTexture.rgb, _DissolveHueShift + POI_TIME.x * _DissolveHueShiftSpeed, _DissolveHueShiftColorSpace, _DissolveHueSelectOrShift);
	}

	if (_DissolveAlphaOnly >= 0.5)
	{
		float targetAlpha = evalRamp4(dissolveAlpha, _DissolveAlphaOnlyRamp);
		poiFragData.alpha *= targetAlpha;
	}
	else
	{
		poiFragData.alpha = mix(poiFragData.alpha, dissolveToTexture.a, dissolveAlpha * DISSOLVE_BLEND_MAX);
	}
#if !defined(POI_PASS_OUTLINE)
	if (_DissolveAlphaOnly < 0.5)
	{
		poiFragData.baseColor = mix(poiFragData.baseColor, dissolveToTexture.rgb, dissolveAlpha * DISSOLVE_BLEND_MAX);
		poiFragData.emission += dissolveToTexture.rgb * _DissolveToEmissionStrength * dissolveAlpha;
	}
	if (_DissolveEdgeEnabled >= 0.5)
	{
		float fwidthEdge = max(fwidth(distFromEdge), fwidthGradient);
		float edgeWidthAL = max(_DissolveEdgeWidth, 0.0);
		float undissolvedEdge = (1.0 - dissolveAlpha) * clamp((softness + edgeWidthAL - distFromEdge) / fwidthEdge, 0.0, 1.0);
		float dissolvedEdge = dissolveAlpha * clamp((softness + edgeWidthAL + distFromEdge) / fwidthEdge, 0.0, 1.0);
		float edgeAlpha = _DissolveEdgeFlip != 0.0 ? dissolvedEdge : undissolvedEdge;
		float edgeTotal = 2.0 * softness + edgeWidthAL;
		float edgeT = clamp((softness + edgeWidthAL + (_DissolveEdgeFlip >= 0.5 ? distFromEdge : -distFromEdge)) / (edgeTotal + 0.001), 0.0, 1.0);
		vec3 edgeColor = poiThemeColor(poiMods, _DissolveEdgeColor.rgb, _DissolveEdgeColorThemeIndex);
		if (_DissolveEdgeGradient >= 0.5)
		{
			vec3 color2 = poiThemeColor(poiMods, _DissolveEdgeColor2.rgb, _DissolveEdgeColor2ThemeIndex);
			vec3 color3 = poiThemeColor(poiMods, _DissolveEdgeColor3.rgb, _DissolveEdgeColor3ThemeIndex);

			float band1 = poiEdgeNonLinear(edgeT, _DissolveEdgeBorder1, _DissolveEdgeBlur1);
			float band2 = poiEdgeNonLinear(edgeT, _DissolveEdgeBorder2, _DissolveEdgeBlur2);

			band2 = min(band2, band1);
			edgeColor = mix(edgeColor, color2, band1);
			edgeColor = mix(edgeColor, color3, band2);
		}
		// Shift the composed gradient so all three stops rotate together.
		if (_DissolveEdgeHueShiftEnabled >= 0.5)
		{
			edgeColor = hueShift(edgeColor, _DissolveEdgeHueShift + POI_TIME.x * _DissolveEdgeHueShiftSpeed, _DissolveEdgeHueShiftColorSpace, _DissolveEdgeHueSelectOrShift);
		}
		poiFragData.baseColor = mix(poiFragData.baseColor, edgeColor, edgeAlpha);
		poiFragData.emission += edgeColor * max(_DissolveEdgeEmission, 0.0) * edgeAlpha;
	}
	if (_DissolveApplyGlobalMaskIndex > 0.0)
	{
		applyToGlobalMask(poiMods, int(_DissolveApplyGlobalMaskIndex) - 1, int(_DissolveApplyGlobalMaskBlendType), dissolveAlpha * DISSOLVE_BLEND_MAX);
	}
	if (_DissolveInverseApplyGlobalMaskIndex > 0.0)
	{
		applyToGlobalMask(poiMods, int(_DissolveInverseApplyGlobalMaskIndex) - 1, int(_DissolveInverseApplyGlobalMaskBlendType), 1.0 - dissolveAlpha * DISSOLVE_BLEND_MAX);
	}
#endif
}
#endif
