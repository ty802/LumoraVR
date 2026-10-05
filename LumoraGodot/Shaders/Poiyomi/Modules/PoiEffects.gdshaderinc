// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Pathing, mirror options, depth FX (touch glow), text, post processing and internal parallax.

#if !defined(POI_FX_SMOOTHSTEP)
#define POI_FX_SMOOTHSTEP
// smoothstep as the source evaluates it: reversed or equal edges stay defined.
float poiFxSmoothstep(float edge0, float edge1, float x)
{
	float t = clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
	return t * t * (3.0 - 2.0 * t);
}
#endif

#if defined(POI_PATHING)
void applyPathing(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	// Sample path source: texture-based gradient or UV-derived
	vec4 pathSourceOrMask = vec4(1.0);
#if defined(PROP_PATHINGMAP)
	if (_PathPointSampling >= 0.5)
	{
		// The source's point_repeat sampler: nearest texel of the nearest mip, wrapped.
		vec2 pointUV = poiUvToTexture(POI_PAN_UV(poiUV(poiMesh.uv[int(_PathingMapUV)], _PathingMap_ST), _PathingMapPan.xy));
		int pointLevel = int(floor(textureQueryLod(_PathingMap, pointUV).x + 0.5));
		vec2 pointSize = vec2(textureSize(_PathingMap, pointLevel));
		pathSourceOrMask = texelFetch(_PathingMap, ivec2(mod(floor(pointUV * pointSize), pointSize)), pointLevel);
	}
	else
	{
		pathSourceOrMask = poiTexPan(_PathingMap, poiUV(poiMesh.uv[int(_PathingMapUV)], _PathingMap_ST), _PathingMapPan.xy);
	}
#endif

	vec4 path = vec4(0.0);
	if (_PathSource == 0.0)
	{
		path = pathSourceOrMask;
	}
	else
	{
		vec2 uv = poiMesh.uv[int(_PathingUVSelect)];
		vec4 dirs = vec4(_PathSourceDirR, _PathSourceDirG, _PathSourceDirB, _PathSourceDirA);
		path = mix(uv.xxxx, uv.yyyy, dirs);
	}

	// Merge channels if selected for texture source
	if (_PathSource == 0.0 && _PathGradientType == 1.0)
	{
		path = vec4(dot(path, vec4(0.25)));
	}

	// Remapping: mask outside range and normalize inside
	vec4 pathRemapMask = vec4(1.0);
	if (_EnablePathRemapping >= 0.5)
	{
		vec4 remapStarts = vec4(_PathRemapR.x, _PathRemapG.x, _PathRemapB.x, _PathRemapA.x);
		vec4 remapEnds = vec4(_PathRemapR.y, _PathRemapG.y, _PathRemapB.y, _PathRemapA.y);
		pathRemapMask = step(remapStarts, path) * (1.0 - step(remapEnds, path));
		path = clamp((path - remapStarts) / (remapEnds - remapStarts + 1e-6), 0.0, 1.0);
	}

#if defined(PROP_PATHINGCOLORMAP)
	vec4 pathColorMap = poiTexPan(_PathingColorMap, poiUV(poiMesh.uv[int(_PathingColorMapUV)], _PathingColorMap_ST), _PathingColorMapPan.xy);
#else
	vec4 pathColorMap = vec4(1.0);
#endif

	vec4 PathColor[4] = vec4[4](_PathColorR, _PathColorG, _PathColorB, _PathColorA);
	vec4 pathTypes = vec4(_PathTypeR, _PathTypeG, _PathTypeB, _PathTypeA);
	float pathGlobalMaskIndices[4] = float[4](_PathGlobalMaskR, _PathGlobalMaskG, _PathGlobalMaskB, _PathGlobalMaskA);
	float pathGlobalMaskBlends[4] = float[4](_PathGlobalMaskRBlendType, _PathGlobalMaskGBlendType, _PathGlobalMaskBBlendType, _PathGlobalMaskABlendType);
	float finalPathAlpha[4] = float[4](0.0, 0.0, 0.0, 0.0);

	for (int i = 0; i < 4; i++)
	{
		float currentTime = fract((_PathTime[i] == -999.0 ? POI_TIME.y * _PathSpeed[i] : _PathTime[i]) + _PathOffset[i]);

		float pathSegments = abs(_PathSegments[i]);
		if (pathSegments > 0.0)
		{
			currentTime = (ceil(currentTime * pathSegments) - 0.5) * (1.0 / pathSegments);
		}

		float pathWidth = _PathWidth[i] * 0.5;
		float rcpPathWidth = 1.0 / (pathWidth + 1e-6);
		float softness = max(_PathSoftness[i], 0.0);

		float fillAlpha = 0.0;
		if (softness > 1e-5)
		{
			float totalAnimationLength = 1.0 + softness;
			float timeAlongPath = currentTime * totalAnimationLength;
			fillAlpha = poiFxSmoothstep(timeAlongPath, timeAlongPath - softness, path[i]);
		}
		else
		{
			fillAlpha = step(path[i], currentTime);
		}

		float pathAlpha = clamp(1.0 - abs(mix(-pathWidth, 1.0 + pathWidth, currentTime) - path[i]) * rcpPathWidth, 0.0, 1.0);
		float loopAlpha = clamp(1.0 - min(abs(currentTime - path[i]), 1.0 - abs(currentTime - path[i])) * rcpPathWidth, 0.0, 1.0);

		float dashAlpha = 0.0;
		float totalDashLength = _PathWidth[i] + _PathGapLengths[i];
		if (totalDashLength > 1e-6)
		{
			float relativeDashLength = _PathWidth[i] / totalDashLength;
			float dashPattern = fract(path[i] / totalDashLength - currentTime);

			float dashSoftness = softness * 0.5 * relativeDashLength;
			dashSoftness = min(dashSoftness, relativeDashLength * 0.499);

			float rise = poiFxSmoothstep(0.0, dashSoftness, dashPattern);
			float fall = poiFxSmoothstep(relativeDashLength, relativeDashLength - dashSoftness, dashPattern);
			dashAlpha = rise * fall;
		}

		float currentAlpha = 0.0;
		if (pathTypes[i] == 0.0)
		{
			currentAlpha = fillAlpha;
		}
		else if (pathTypes[i] == 1.0)
		{
			currentAlpha = pathAlpha;
		}
		else if (pathTypes[i] == 2.0)
		{
			currentAlpha = loopAlpha;
		}
		else if (pathTypes[i] == 3.0)
		{
			currentAlpha = dashAlpha;
		}

		currentAlpha *= pathRemapMask[i];

		// Avoid zero-path artifacts
		currentAlpha *= step(1e-6, path[i]);

		if (pathTypes[i] == 3.0 || pathTypes[i] == 0.0)
		{
			finalPathAlpha[i] = currentAlpha;
		}
		else
		{
			finalPathAlpha[i] = poiFxSmoothstep(0.0, softness + 1e-6, currentAlpha);
		}

		if (path[i] <= 0.0)
		{
			finalPathAlpha[i] = 0.0;
		}

		finalPathAlpha[i] = globalMaskBlend(finalPathAlpha[i], pathGlobalMaskIndices[i], pathGlobalMaskBlends[i], poiMods);
	}

	vec4 finalPathAlphaV = vec4(finalPathAlpha[0], finalPathAlpha[1], finalPathAlpha[2], finalPathAlpha[3]);
	if (_PathSource == 1.0)
	{
		finalPathAlphaV *= pathSourceOrMask;
	}

	// Emission ignores color alpha
	vec3 pathEmission = vec3(0.0);
	pathEmission += finalPathAlphaV.r * poiThemeColor(poiMods, PathColor[0].rgb, _PathColorRThemeIndex) * _PathEmissionStrength.r;
	pathEmission += finalPathAlphaV.g * poiThemeColor(poiMods, PathColor[1].rgb, _PathColorGThemeIndex) * _PathEmissionStrength.g;
	pathEmission += finalPathAlphaV.b * poiThemeColor(poiMods, PathColor[2].rgb, _PathColorBThemeIndex) * _PathEmissionStrength.b;
	pathEmission += finalPathAlphaV.a * poiThemeColor(poiMods, PathColor[3].rgb, _PathColorAThemeIndex) * _PathEmissionStrength.a;

	vec3 albedo = poiFragData.baseColor;
	vec3 finalPathColor = vec3(0.0);
	float finalAlpha = 0.0;

	vec3 colorR = poiThemeColor(poiMods, PathColor[0].rgb, _PathColorRThemeIndex);
	float alphaR = finalPathAlphaV.r * PathColor[0].a;
	finalPathColor = colorR * alphaR;
	finalAlpha = alphaR;

	vec3 colorG = poiThemeColor(poiMods, PathColor[1].rgb, _PathColorGThemeIndex);
	float alphaG = finalPathAlphaV.g * PathColor[1].a;
	finalPathColor = (colorG * alphaG) + finalPathColor * (1.0 - alphaG);
	finalAlpha = alphaG + finalAlpha * (1.0 - alphaG);

	vec3 colorB = poiThemeColor(poiMods, PathColor[2].rgb, _PathColorBThemeIndex);
	float alphaB = finalPathAlphaV.b * PathColor[2].a;
	finalPathColor = (colorB * alphaB) + finalPathColor * (1.0 - alphaB);
	finalAlpha = alphaB + finalAlpha * (1.0 - alphaB);

	vec3 colorA = poiThemeColor(poiMods, PathColor[3].rgb, _PathColorAThemeIndex);
	float alphaA = finalPathAlphaV.a * PathColor[3].a;
	finalPathColor = (colorA * alphaA) + finalPathColor * (1.0 - alphaA);
	finalAlpha = alphaA + finalAlpha * (1.0 - alphaA);

	finalPathColor *= pathColorMap.rgb;
	finalAlpha *= pathColorMap.a;
	finalPathColor *= pathColorMap.a;
	pathEmission *= pathColorMap.rgb * pathColorMap.a;

	if (_PathSurfaceBlendMode == 0.0)
	{
		albedo = albedo * (1.0 - finalAlpha) + finalPathColor;
	}
	else if (_PathSurfaceBlendMode == 1.0)
	{
		albedo += finalPathColor;
	}
	else
	{
		albedo *= (finalPathColor + (1.0 - finalAlpha));
	}

	poiFragData.alpha = mix(poiFragData.alpha, finalAlpha, _PathingOverrideAlpha);
	poiFragData.baseColor = albedo;
	poiFragData.emission += pathEmission;
}
#endif

#if defined(POI_MIRROR)
// Both visibility modes test the camera itself: poiCam.isMirror (oblique projection).
void applyMirror(in PoiCam poiCam, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	bool inMirror = poiCam.isMirror;

#if defined(POI_PASS_BASE) && defined(POI_MIRROR_TEXTURE)
	if (inMirror || _MirrorTextureForceEnabled >= 0.5)
	{
#if defined(PROP_MIRRORTEXTURE)
		vec4 mirrorTexture = poiTexPan(_MirrorTexture, poiUV(poiMesh.uv[int(_MirrorTextureUV)], _MirrorTexture_ST), _MirrorTexturePan);
		poiFragData.baseColor = mix(poiFragData.baseColor, customBlend(poiFragData.baseColor, mirrorTexture.rgb, _MirrorTextureBlendType), mirrorTexture.a * _MirrorColor.a);
#endif
		poiFragData.baseColor *= mix(vec3(1.0), poiThemeColor(poiMods, _MirrorColor.rgb, _MirrorColorThemeIndex), _MirrorColor.a);
	}
#endif
}
#endif

#if defined(POI_DEPTH_FX)
// The scene position behind the pixel comes from the depth texture through the inverse projection and view.
void applyDepthFX(inout PoiFragData poiFragData, in PoiCam poiCam, in PoiMesh poiMesh, in PoiMods poiMods)
{
	vec2 screenPos = vec2(poiCam.fragCoord.x / poiCam.screenSize.x, 1.0 - poiCam.fragCoord.y / poiCam.screenSize.y);

	if (!poiDepthTextureExists())
	{
		return;
	}
	float z = poiSampleScreenDepth(screenPos);

	// Reversed depth: 0 is the far plane.
	if (z == 0.0)
	{
		return;
	}

	vec4 sceneView = poiCam.invProjectionMatrix * vec4(poiScreenToTexture(screenPos) * 2.0 - 1.0, z, 1.0);
	vec3 worldpos = (poiCam.invViewMatrix * vec4(sceneView.xyz / sceneView.w, 1.0)).xyz;

	float diff = distance(worldpos, poiMesh.worldPos);

#if defined(PROP_DEPTHMASK)
	float depthMask = poiTexPan(_DepthMask, poiUV(poiMesh.uv[int(_DepthMaskUV)], _DepthMask_ST), _DepthMaskPan)[int(_DepthMaskChannel)];
#else
	float depthMask = 1.0;
#endif

	if (_DepthMaskGlobalMask > 0.0)
	{
		depthMask = maskBlend(depthMask, poiMods.globalMask[int(_DepthMaskGlobalMask) - 1], _DepthMaskGlobalMaskBlendType);
	}

	if (_DepthColorToggle >= 0.5)
	{
		float colorBlendAlpha = mix(_DepthColorMinValue, _DepthColorMaxValue, remapClamped(_DepthColorMinDepth, _DepthColorMaxDepth, diff));

#if defined(PROP_DEPTHTEXTURE)
		vec2 depthTextureUV = vec2(0.0);
		if (_DepthTextureUV == 8.0)
		{
			depthTextureUV = vec2(remapClamped(_DepthColorMinDepth, _DepthColorMaxDepth, diff));
		}
		else
		{
			depthTextureUV = poiMesh.uv[int(_DepthTextureUV)];
		}
		vec3 depthColor = poiTexPan(_DepthTexture, poiUV(depthTextureUV, _DepthTexture_ST), _DepthTexturePan).rgb * poiThemeColor(poiMods, _DepthColor.rgb, _DepthColorThemeIndex);
#else
		vec3 depthColor = poiThemeColor(poiMods, _DepthColor.rgb, _DepthColorThemeIndex);
#endif

		int blendMode = int(_DepthColorBlendMode);
		if (blendMode == 0)
		{
			poiFragData.baseColor = mix(poiFragData.baseColor, depthColor, colorBlendAlpha * depthMask);
		}
		else if (blendMode == 1)
		{
			poiFragData.baseColor *= mix(vec3(1.0), depthColor, colorBlendAlpha * depthMask);
		}
		else if (blendMode == 2)
		{
			poiFragData.baseColor = clamp(poiFragData.baseColor + mix(vec3(0.0), depthColor, colorBlendAlpha * depthMask), 0.0, 1.0);
		}
		poiFragData.emission += depthColor * colorBlendAlpha * _DepthEmissionStrength * depthMask;
	}

	if (_DepthAlphaToggle >= 0.5)
	{
		poiFragData.alpha *= mix(poiFragData.alpha, clamp(mix(_DepthAlphaMinValue, _DepthAlphaMaxValue, remapClamped(_DepthAlphaMinDepth, _DepthAlphaMaxDepth, diff)), 0.0, 1.0), depthMask);
	}
}
#endif

#if defined(POI_TEXT)
#define ASCII_SPACE 32.0
#define ASCII_LEFT_PARENTHESIS 40.0
#define ASCII_RIGHT_PARENTHESIS 41.0
#define ASCII_POSITIVE 43.0
#define ASCII_PERIOD 46.0
#define ASCII_NEGATIVE 45.0
#define ASCII_COMMA 44.0
#define ASCII_A 65.0
#define ASCII_E 69.0
#define ASCII_F 70.0
#define ASCII_I 73.0
#define ASCII_M 77.0
#define ASCII_O 79.0
#define ASCII_P 80.0
#define ASCII_R 82.0
#define ASCII_S 83.0
#define ASCII_T 84.0
#define ASCII_SEMICOLON 58.0
#define glyphWidth 0.0625

vec2 TransformUV(vec2 offset, float rotation, vec2 scale, vec2 uv)
{
	float theta = radians(rotation);
	// A zero or near-zero scale collapses the remap range
	vec2 scaleSign = vec2(scale.x < 0.0 ? -1.0 : 1.0, scale.y < 0.0 ? -1.0 : 1.0);
	vec2 s = scaleSign * max(abs(scale), vec2(1e-4, 1e-4));
	s = 1.0 - s;
	float cs = cos(theta);
	float sn = sin(theta);
	vec2 centerPoint = offset + 0.5;
	vec2 r = vec2((uv.x - centerPoint.x) * cs - (uv.y - centerPoint.y) * sn + centerPoint.x, (uv.x - centerPoint.x) * sn + (uv.y - centerPoint.y) * cs + centerPoint.y);

	return remap(r, vec2(0.0, 0.0) + offset + (s * 0.5), vec2(1.0, 1.0) + offset - (s * 0.5), vec2(0.0, 0.0), vec2(1.0, 1.0));
}

vec2 getAsciiCoordinate(float index)
{
	return vec2((index - 1.0) / 16.0, 1.0 - ((floor(index / 16.0 - glyphWidth)) / 16.0));
}

float median(float r, float g, float b)
{
	return max(min(r, g), min(max(r, g), b));
}

bool GetTextGlyphIndex(inout vec2 uv, vec2 offset, float rotation, vec2 scale, float totalCharacters, out float glyphIndex)
{
	glyphIndex = 0.0;
	uv = TransformUV(offset, rotation, scale, uv);

	if (uv.x >= 1.0 || uv.x < 0.0 || uv.y >= 1.0 || uv.y < 0.0)
	{
		return false;
	}

	glyphIndex = min(floor(uv.x * totalCharacters), totalCharacters - 1.0);
	return true;
}

float SampleTextDistance(vec2 uv, vec2 uvDx, vec2 uvDy)
{
	vec2 sampleUV = uv * _TextGlyphs_ST.xy + _TextGlyphs_ST.zw;
	vec2 sampleDx = uvDx * _TextGlyphs_ST.xy;
	vec2 sampleDy = uvDy * _TextGlyphs_ST.xy;
	vec3 samp = poiTexGrad(_TextGlyphs, sampleUV, sampleDx, sampleDy).rgb;
	return median(samp.r, samp.g, samp.b);
}

void BlendTextLayer(inout vec3 dstColor, inout float dstAlpha, vec3 srcColor, float srcAlpha)
{
	dstColor = srcColor * srcAlpha + dstColor * (1.0 - srcAlpha);
	dstAlpha = srcAlpha + dstAlpha * (1.0 - srcAlpha);
}

void DrawTextGlyph(inout PoiFragData poiFragData, inout vec3 globalTextEmission, in PoiMods poiMods, vec2 glyphUV, float asciiCode, float glyphIndex, float totalCharacters, vec4 glyphPadding, vec4 color, float emissionStrength, float themeIndex)
{
	vec2 uv = glyphUV;
	vec2 glyphPos = getAsciiCoordinate(asciiCode);
	float cellWidth = 1.0 / totalCharacters;
	vec2 startUV = vec2(cellWidth * glyphIndex, 0.0);
	vec2 endUV = vec2(cellWidth * (glyphIndex + 1.0), 1.0);
	// Capture derivatives while the text UV is still continuous.
	vec2 textUvDx = dFdx(uv);
	vec2 textUvDy = dFdy(uv);

	vec2 textScaleSign = vec2(_TextScale.x < 0.0 ? -1.0 : 1.0, _TextScale.y < 0.0 ? -1.0 : 1.0);
	vec2 safeTextScale = textScaleSign * max(abs(_TextScale), vec2(1e-4, 1e-4));
	vec2 scale = vec2(cellWidth, 1.0) * (1.0 - safeTextScale) * 0.5;
	startUV += scale;
	endUV -= scale;

	vec4 padding = glyphPadding * cellWidth;
	vec2 glyphMin = vec2(glyphPos.x + padding.x, glyphPos.y - glyphWidth + padding.y);
	vec2 glyphMax = vec2(glyphPos.x + glyphWidth - padding.z, glyphPos.y - padding.w);
	vec2 atlasRemapScale = (glyphMax - glyphMin) / (endUV - startUV);
	vec2 atlasUvDx = textUvDx * atlasRemapScale;
	vec2 atlasUvDy = textUvDy * atlasRemapScale;

	uv = remapClamped(startUV, endUV, uv, glyphMin, glyphMax);

	// Keep bilinear filtering inside the selected atlas cell at every scale.
	vec4 glyphsTexelSize = poiTexelSize(_TextGlyphs);
	vec2 atlasInset = max(glyphsTexelSize.xy * 0.5, vec2(0.001, 0.001));
	if (uv.x >= glyphMax.x - atlasInset.x || uv.x <= glyphMin.x + atlasInset.x || uv.y >= glyphMax.y - atlasInset.y || uv.y <= glyphMin.y + atlasInset.y)
	{
		return;
	}

	vec2 msdfUnit = _TextPixelRange / glyphsTexelSize.zw;
	vec2 atlasFwidth = max(abs(atlasUvDx) + abs(atlasUvDy), vec2(1e-7, 1e-7));
	float screenPxRange = max(dot(msdfUnit, 0.5 / atlasFwidth), 1.0);
	// one screen pixel in distance field units
	float fieldPerPixel = 1.0 / screenPxRange;

	float thiccnessBias = _TextThiccness;

	float glyphDistance = SampleTextDistance(uv, atlasUvDx, atlasUvDy) - 0.5 + thiccnessBias;
	float coverage = clamp(screenPxRange * glyphDistance + 0.5, 0.0, 1.0);

	if (_TextHollowEnabled >= 0.5)
	{
		float hollowWidth = max(_TextHollowWidth, fieldPerPixel);
		coverage = clamp(screenPxRange * (hollowWidth - abs(glyphDistance)) + 0.5, 0.0, 1.0);
	}

	vec3 textColor = vec3(0.0);
	float textAlpha = 0.0;

	if (_TextShadowEnabled >= 0.5)
	{
		vec2 shadowUV = uv - _TextShadowOffset * glyphWidth;
		vec2 shadowMin = glyphMin + atlasInset;
		vec2 shadowMax = glyphMax - atlasInset;
		bool shadowInTile = all(greaterThan(shadowUV, shadowMin)) && all(lessThan(shadowUV, shadowMax));
		float shadowDistance = -0.5;
		if (shadowInTile)
		{
			shadowDistance = SampleTextDistance(shadowUV, atlasUvDx, atlasUvDy) - 0.5;
		}

		float shadowSize = _TextShadowSize * 0.4;
		float shadowStart = -shadowSize;
		float shadowEnd = max(-shadowSize * (1.0 - _TextShadowSoftness), shadowStart + fieldPerPixel);
		float shadowCoverage = poiFxSmoothstep(shadowStart, shadowEnd, shadowDistance);

		vec3 shadowColor = poiThemeColor(poiMods, _TextShadowColor.rgb, _TextShadowColorThemeIndex);
		BlendTextLayer(textColor, textAlpha, shadowColor, shadowCoverage * _TextShadowColor.a);
		float shadowVisible = shadowCoverage * (1.0 - coverage * color.a);
		globalTextEmission += shadowColor * shadowVisible * _TextShadowEmissionStrength;
	}

	vec3 themedColor = poiThemeColor(poiMods, color.rgb, themeIndex);

	if (_TextBevelEnabled >= 0.5)
	{
		vec2 bevelGradient = vec2(dFdx(glyphDistance), dFdy(glyphDistance));
		vec2 bevelNormal = bevelGradient * inversesqrt(max(dot(bevelGradient, bevelGradient), 1e-12));
		float bevelAngle = radians(_TextBevelLightAngle);
		float bevelLight = 0.5 * (1.0 + dot(bevelNormal, vec2(cos(bevelAngle), sin(bevelAngle))));

		float bevelBlend = (1.0 - poiFxSmoothstep(0.0, _TextBevelWidth, glyphDistance)) * _TextBevelStrength;
		themedColor = mix(themedColor, themedColor * bevelLight * 2.0, bevelBlend);
	}

	BlendTextLayer(textColor, textAlpha, themedColor, coverage * color.a);
	globalTextEmission += themedColor * coverage * emissionStrength;

	poiFragData.baseColor = textColor + poiFragData.baseColor * (1.0 - textAlpha);
}

void ApplyPositionText(in PoiCam poiCam, inout PoiFragData poiFragData, inout vec3 globalTextEmission, vec2 textUV, in PoiMods poiMods)
{
	vec2 uv = textUV;
	vec3 cameraPos = clamp(getCameraPosition(poiCam), -999.0, 999.0);
	vec3 absCameraPos = abs(cameraPos);
	float totalCharacters = 20.0;
	float positionArray[20];
	positionArray[0] = cameraPos.x < 0.0 ? ASCII_NEGATIVE : ASCII_POSITIVE;
	positionArray[1] = floor(mod(absCameraPos.x * 0.01, 10.0)) + 48.0;
	positionArray[2] = floor(mod(absCameraPos.x * 0.1, 10.0)) + 48.0;
	positionArray[3] = floor(mod(absCameraPos.x, 10.0)) + 48.0;
	positionArray[4] = ASCII_PERIOD;
	positionArray[5] = floor(mod(absCameraPos.x * 10.0, 10.0)) + 48.0;
	positionArray[6] = ASCII_COMMA;
	positionArray[7] = cameraPos.y < 0.0 ? ASCII_NEGATIVE : ASCII_POSITIVE;
	positionArray[8] = floor(mod(absCameraPos.y * 0.01, 10.0)) + 48.0;
	positionArray[9] = floor(mod(absCameraPos.y * 0.1, 10.0)) + 48.0;
	positionArray[10] = floor(mod(absCameraPos.y, 10.0)) + 48.0;
	positionArray[11] = ASCII_PERIOD;
	positionArray[12] = floor(mod(absCameraPos.y * 10.0, 10.0)) + 48.0;
	positionArray[13] = ASCII_COMMA;
	positionArray[14] = cameraPos.z < 0.0 ? ASCII_NEGATIVE : ASCII_POSITIVE;
	positionArray[15] = floor(mod(absCameraPos.z * 0.01, 10.0)) + 48.0;
	positionArray[16] = floor(mod(absCameraPos.z * 0.1, 10.0)) + 48.0;
	positionArray[17] = floor(mod(absCameraPos.z, 10.0)) + 48.0;
	positionArray[18] = ASCII_PERIOD;
	positionArray[19] = floor(mod(absCameraPos.z * 10.0, 10.0)) + 48.0;

	float glyphIndex = 0.0;
	if (!GetTextGlyphIndex(uv, _TextPositionOffset, _TextPositionRotation, _TextPositionScale, totalCharacters, glyphIndex))
	{
		return;
	}

	DrawTextGlyph(poiFragData, globalTextEmission, poiMods, uv, positionArray[int(glyphIndex)], glyphIndex, totalCharacters, _TextPositionPadding, _TextPositionColor, _TextPositionEmissionStrength, _TextPositionColorThemeIndex);
}

void ApplyTimeText(inout PoiFragData poiFragData, inout vec3 globalTextEmission, vec2 textUV, in PoiMods poiMods)
{
	vec2 uv = textUV;
	float instanceTime = TIME;
	float hours = instanceTime / 3600.0;
	float minutes = mod(instanceTime / 60.0, 60.0);
	float seconds = mod(instanceTime, 60.0);
	float totalCharacters = 8.0;
	float timeArray[8];
	timeArray[0] = floor(mod(hours * 0.1, 10.0)) + 48.0;
	timeArray[1] = floor(mod(hours, 10.0)) + 48.0;
	timeArray[2] = ASCII_SEMICOLON;
	timeArray[3] = floor(mod(minutes * 0.1, 10.0)) + 48.0;
	timeArray[4] = floor(mod(minutes, 10.0)) + 48.0;
	timeArray[5] = ASCII_SEMICOLON;
	timeArray[6] = floor(mod(seconds * 0.1, 10.0)) + 48.0;
	timeArray[7] = floor(mod(seconds, 10.0)) + 48.0;

	float glyphIndex = 0.0;
	if (!GetTextGlyphIndex(uv, _TextTimeOffset, _TextTimeRotation, _TextTimeScale, totalCharacters, glyphIndex))
	{
		return;
	}

	DrawTextGlyph(poiFragData, globalTextEmission, poiMods, uv, timeArray[int(glyphIndex)], glyphIndex, totalCharacters, _TextTimePadding, _TextTimeColor, _TextTimeEmissionStrength, _TextTimeColorThemeIndex);
}

// No wall clock reaches the shader: both time zone modes count TIME (seconds since start, wrapping at the
// time rollover) as the time of day, shifted by the offset.
void ApplyUTCTimeText(inout PoiFragData poiFragData, inout vec3 globalTextEmission, vec2 textUV, in PoiMods poiMods)
{
	vec2 uv = textUV;
	uint daySeconds = uint(max(TIME, 0.0)) % 86400u;
	daySeconds = uint(mod(float(daySeconds) + _TextUTCTimeZoneOffset, 86400.0));
	uint hours = daySeconds / 3600u;
	uint minutes = (daySeconds / 60u) % 60u;
	uint seconds = daySeconds % 60u;

	bool is12Hour = _TextUTCTimeFormat >= 0.5;
	bool isPM = hours >= 12u;

	if (is12Hour)
	{
		hours = hours % 12u;
		// Midnight and noon both read as 12
		hours = hours == 0u ? 12u : hours;
	}

	// HH:MM:SS, plus " AM" on 12 hour
	float totalCharacters = is12Hour ? 11.0 : 8.0;
	float utcArray[11];
	utcArray[0] = float(hours / 10u) + 48.0;
	utcArray[1] = float(hours % 10u) + 48.0;
	utcArray[2] = ASCII_SEMICOLON;
	utcArray[3] = float(minutes / 10u) + 48.0;
	utcArray[4] = float(minutes % 10u) + 48.0;
	utcArray[5] = ASCII_SEMICOLON;
	utcArray[6] = float(seconds / 10u) + 48.0;
	utcArray[7] = float(seconds % 10u) + 48.0;
	utcArray[8] = ASCII_SPACE;
	utcArray[9] = isPM ? ASCII_P : ASCII_A;
	utcArray[10] = ASCII_M;
	// 12 hour drops the leading zero on the hour
	utcArray[0] = (is12Hour && hours < 10u) ? ASCII_SPACE : utcArray[0];

	float glyphIndex = 0.0;
	if (!GetTextGlyphIndex(uv, _TextUTCTimeOffset, _TextUTCTimeRotation, _TextUTCTimeScale, totalCharacters, glyphIndex))
	{
		return;
	}

	DrawTextGlyph(poiFragData, globalTextEmission, poiMods, uv, utcArray[int(glyphIndex)], glyphIndex, totalCharacters, _TextUTCTimePadding, _TextUTCTimeColor, _TextUTCTimeEmissionStrength, _TextUTCTimeColorThemeIndex);
}

// The frame rate does not reach the shader: the digits read 000.
void ApplyFPSText(inout PoiFragData poiFragData, inout vec3 globalTextEmission, vec2 textUV, in PoiMods poiMods)
{
	vec2 uv = textUV;
	float smoothDeltaTime = 0.0;
	float totalCharacters = 7.0;
	float fpsArray[7];
	fpsArray[0] = ASCII_F;
	fpsArray[1] = ASCII_P;
	fpsArray[2] = ASCII_S;
	fpsArray[3] = ASCII_SEMICOLON;
	fpsArray[4] = floor(mod(smoothDeltaTime * 0.01, 10.0)) + 48.0;
	fpsArray[5] = floor(mod(smoothDeltaTime * 0.1, 10.0)) + 48.0;
	fpsArray[6] = floor(mod(smoothDeltaTime, 10.0)) + 48.0;

	float glyphIndex = 0.0;
	if (!GetTextGlyphIndex(uv, _TextFPSOffset, _TextFPSRotation, _TextFPSScale, totalCharacters, glyphIndex))
	{
		return;
	}

	DrawTextGlyph(poiFragData, globalTextEmission, poiMods, uv, fpsArray[int(glyphIndex)], glyphIndex, totalCharacters, _TextFPSPadding, _TextFPSColor, _TextFPSEmissionStrength, _TextFPSColorThemeIndex);
}

void ApplyNumericText(inout PoiFragData poiFragData, inout vec3 globalTextEmission, vec2 textUV, in PoiMods poiMods)
{
	vec2 uv = textUV;
	// If both digits are set to zero: exit.
	if (_TextNumericWholeDigits == 0.0 && _TextNumericDecimalDigits == 0.0)
	{
		return;
	}

	uint wholeNumber = 0u;
	uint decimalNumber = 0u;
	uint wholeDigits = uint(_TextNumericWholeDigits);
	uint decimalDigits = uint(_TextNumericDecimalDigits);
	// 1 sign + 4 max whole digits + 1 decimal mark + 4 max decimal digits
	float NumericArray[10] = float[10](ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE);
	uint arrayIndex = 0u;
	// Sign Character + Whole Digits + Decimal Digits
	float totalCharacters = 1.0 + float(wholeDigits) + float(decimalDigits);

	float charSign = _TextNumericValue >= 0.0 ? ASCII_SPACE : ASCII_NEGATIVE;

	NumericArray[int(arrayIndex)] = charSign;
	arrayIndex++;

	if (wholeDigits > 0u)
	{
		wholeNumber = uint(mod(abs(_TextNumericValue), pow(10.0, float(wholeDigits))));

		int expIndex = -1 * (int(wholeDigits) - 1);
		bool leadingZero = true;
		while (arrayIndex <= wholeDigits)
		{
			// Digits left to right
			int digit = int(floor(mod(float(wholeNumber) * pow(10.0, float(expIndex)), 10.0)));
			NumericArray[int(arrayIndex)] = float(digit) + 48.0;

			// Trim leading zeroes, but leave at least one.
			if (_TextNumericTrimZeroes == 1.0)
			{
				if (digit == 0 && leadingZero && arrayIndex != wholeDigits)
				{
					NumericArray[int(arrayIndex)] = ASCII_SPACE;
				}
				else
				{
					leadingZero = false;
				}
			}
			expIndex++;
			arrayIndex++;
		}
	}

	if (decimalDigits > 0u)
	{
		NumericArray[int(arrayIndex)] = ASCII_PERIOD;
		arrayIndex++;
		totalCharacters += 1.0;

		decimalNumber = uint(fract(abs(_TextNumericValue)) * pow(10.00001, float(decimalDigits)));

		int expIndex = -1 * (int(decimalDigits) - 1);
		while (arrayIndex < uint(totalCharacters))
		{
			int digit = int(floor(mod(float(decimalNumber) * pow(10.0, float(expIndex)), 10.0)));
			NumericArray[int(arrayIndex)] = float(digit) + 48.0;

			expIndex++;
			arrayIndex++;
		}
	}

	float glyphIndex = 0.0;
	if (!GetTextGlyphIndex(uv, _TextNumericOffset, _TextNumericRotation, _TextNumericScale, totalCharacters, glyphIndex))
	{
		return;
	}

	DrawTextGlyph(poiFragData, globalTextEmission, poiMods, uv, NumericArray[int(glyphIndex)], glyphIndex, totalCharacters, _TextNumericPadding, _TextNumericColor, _TextNumericEmissionStrength, _TextNumericColorThemeIndex);
}

void ApplyTextOverlayColor(in PoiCam poiCam, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	vec3 globalTextEmission = vec3(0.0);

	if (_TextFPSEnabled >= 0.5)
	{
		ApplyFPSText(poiFragData, globalTextEmission, poiMesh.uv[int(_TextFPSUV)], poiMods);
	}
	if (_TextPositionEnabled >= 0.5)
	{
		ApplyPositionText(poiCam, poiFragData, globalTextEmission, poiMesh.uv[int(_TextPositionUV)], poiMods);
	}
	if (_TextTimeEnabled >= 0.5)
	{
		ApplyTimeText(poiFragData, globalTextEmission, poiMesh.uv[int(_TextTimeUV)], poiMods);
	}
	if (_TextUTCTimeEnabled >= 0.5)
	{
		ApplyUTCTimeText(poiFragData, globalTextEmission, poiMesh.uv[int(_TextUTCTimeUV)], poiMods);
	}
	if (_TextNumericEnabled >= 0.5)
	{
		ApplyNumericText(poiFragData, globalTextEmission, poiMesh.uv[int(_TextNumericUV)], poiMods);
	}

	poiFragData.emission += globalTextEmission;
}
#endif

#if defined(POI_POSTPROCESS)
vec3 poiPosterize(vec3 color, float steps)
{
	vec3 newColor = RGBtoHSV(color);
	float s = floor(steps);
	newColor.r = floor(newColor.r * s) / s;
	newColor.g = floor(newColor.g * s) / s;
	newColor.b = floor(newColor.b * s) / s;
	return HSVtoRGB(newColor);
}

float oetf_sRGB_scalar(float L)
{
	float V = 1.055 * (pow(L, 1.0 / 2.4)) - 0.055;
	if (L <= 0.0031308)
	{
		V = L * 12.92;
	}
	return V;
}

vec3 oetf_sRGB(vec3 L)
{
	return vec3(oetf_sRGB_scalar(L.r), oetf_sRGB_scalar(L.g), oetf_sRGB_scalar(L.b));
}

float eotf_sRGB_scalar(float V)
{
	float L = pow((V + 0.055) / 1.055, 2.4);
	if (V <= oetf_sRGB_scalar(0.0031308))
	{
		L = V / 12.92;
	}
	return L;
}

vec3 GetHDR(vec3 rgb)
{
	return vec3(eotf_sRGB_scalar(rgb.r), eotf_sRGB_scalar(rgb.g), eotf_sRGB_scalar(rgb.b));
}

vec3 GetContrast(vec3 col, float contrast)
{
	return mix(vec3(0.5, 0.5, 0.5), col, contrast);
}

vec3 GetSaturation(vec3 col, float interpolator)
{
	return mix(vec3(dot(col, vec3(0.3, 0.59, 0.11))), col, interpolator);
}

vec4 poiPostProcessMask(in PoiMesh poiMesh)
{
	vec4 ppMask = vec4(1.0);
#if defined(PROP_PPMASK)
	ppMask = poiTexPan(_PPMask, poiUV(poiMesh.uv[int(_PPMaskUV)], _PPMask_ST), _PPMaskPan);
	ppMask = mix(ppMask, 1.0 - ppMask, _PPMaskInvert);
#endif
	return ppMask;
}

// The source grades the whole lit color. Here it arrives in parts (the lit albedo, the unlit sum, each
// light's added share), each graded on its own: exact for the linear steps, per part for the hue, HDR
// and posterize steps. The constant terms (contrast pivot, lightness) go to the unlit part only.
vec3 poiPostProcessPart(vec3 color, vec4 ppMask, bool carriesConstants)
{
	vec3 col = color;
	col = hueShift(col, _PPHue + _PPHueOffset * ppMask[int(_PPHueOffsetMask)] + fract(POI_TIME.x * _PPHueSpeed), _PPHueShiftColorSpace, _ppHueSelectOrShift);
	col *= _PPTint;
	col *= _PPRGB;
	col = GetSaturation(col, _PPSaturation);
	col = mix(col, GetHDR(col), _PPHDR);
	col = carriesConstants ? GetContrast(col, _PPContrast) : col * _PPContrast;
	col *= _PPBrightness;
	col += carriesConstants ? vec3(_PPLightness) : vec3(0.0);

	col = mix(color, col, ppMask[int(_PPMaskChannel)]);

	if (_PPPosterization >= 0.5)
	{
		col = mix(col, poiPosterize(col, _PPPosterizationAmount), ppMask[int(_PPMaskChannel)]);
	}
	return col;
}

void applyPostProcessing(inout PoiFragData poiFragData, in PoiMesh poiMesh)
{
	vec4 ppMask = poiPostProcessMask(poiMesh);
	poiFragData.baseColor = poiPostProcessPart(poiFragData.baseColor, ppMask, false);
	poiFragData.finalColor = poiPostProcessPart(poiFragData.finalColor, ppMask, true);
}

// Each light's added share (specular, rim and the like) gets the grading the fragment gave the rest;
// the diffuse share is lit from the graded albedo already. Runs after every other light hook.
#define POI_HAS_LIGHT_HOOK_EFFECTS
void applyEffectsLight(inout PoiLight poiLight, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods)
{
	vec4 ppMask = poiPostProcessMask(poiMesh);
	poiLight.finalLightAdd = poiPostProcessPart(poiLight.finalLightAdd, ppMask, false);
}
#endif

#if defined(POI_INTERNALPARALLAX)
vec2 internalParallaxUV(float uvNumber, vec2 position, float rotation, vec2 scale, vec4 sideOffsetIn, in PoiMesh poiMesh)
{
	vec4 sideOffset = vec4(-sideOffsetIn.x, sideOffsetIn.y, -sideOffsetIn.z, sideOffsetIn.w);
	vec2 centerOffset = vec2((sideOffset.x + sideOffset.y) / 2.0, (sideOffset.z + sideOffset.w) / 2.0);
	vec2 uv = poiMesh.uv[int(uvNumber)];
	vec2 center = position + centerOffset;
	float sn = sin(radians(rotation));
	float cs = cos(radians(rotation));
	uv = vec2((uv.x - center.x) * cs - (uv.y - center.y) * sn + center.x, (uv.x - center.x) * sn + (uv.y - center.y) * cs + center.y);
	uv = remap(uv, position - scale / 2.0 + sideOffset.xz, position + scale / 2.0 + sideOffset.yw, vec2(0.0, 0.0), vec2(1.0, 1.0));
	return uv;
}

void applyInternalParallax(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, in PoiMods poiMods)
{
	float parallaxMask = 1.0;
#if defined(PROP_PARALLAXINTERNALMAPMASK)
	parallaxMask = poiTexPan(_ParallaxInternalMapMask, poiUV(poiMesh.uv[int(_ParallaxInternalMapMaskUV)], _ParallaxInternalMapMask_ST), _ParallaxInternalMapMaskPan)[int(_ParallaxInternalMapMaskChannel)];
#endif

	parallaxMask = _ParallaxInternalMapMaskInvert != 0.0 ? 1.0 - parallaxMask : parallaxMask;
	if (_ParallaxInternalMapMaskGlobalMask > 0.0)
	{
		parallaxMask = maskBlend(parallaxMask, poiMods.globalMask[int(_ParallaxInternalMapMaskGlobalMask) - 1], _ParallaxInternalMapMaskGlobalMaskBlendType);
	}

	// Decal style placement of the map in the selected UV set
	vec2 placedUV = internalParallaxUV(_ParallaxInternalMapUV, _ParallaxInternalPosition.xy, _ParallaxInternalRotation, _ParallaxInternalScale.xy, _ParallaxInternalSideOffset, poiMesh);

	if (parallaxMask <= 0.0)
	{
		return;
	}

	vec3 minColor = poiThemeColor(poiMods, _ParallaxInternalMinColor.rgb, _ParallaxInternalMinColorThemeIndex);
	vec3 maxColor = poiThemeColor(poiMods, _ParallaxInternalMaxColor.rgb, _ParallaxInternalMaxColorThemeIndex);
	vec2 panBase = POI_TIME.y * _ParallaxInternalMapPan;
	float hueShiftAmount = _ParallaxInternalHueShift + _ParallaxInternalHueShiftSpeed * POI_TIME.x;

	float minFade = _ParallaxInternalMinFade;
	float maxFade = _ParallaxInternalMaxFade;

	vec2 viewOffset = vec2(0.0);
	vec2 invMapTiling = vec2(1.0);
#if defined(PROP_PARALLAXINTERNALMAP)
	float grazingBias = 0.3;
	float tangentZ = poiCam.tangentViewDir.z < 0.0 ? poiCam.tangentViewDir.z - grazingBias : poiCam.tangentViewDir.z + grazingBias;
	viewOffset = -(poiCam.tangentViewDir.xy * (1.0 + grazingBias) / tangentZ);
	// Layer offsets are authored in tiled map space, this brings them back to placement space
	vec2 mapTiling = vec2(abs(_ParallaxInternalMap_ST.x) < 1e-5 ? 1.0 : _ParallaxInternalMap_ST.x, abs(_ParallaxInternalMap_ST.y) < 1e-5 ? 1.0 : _ParallaxInternalMap_ST.y);
	invMapTiling = 1.0 / mapTiling;
#endif

	float distanceToCamera = distance(poiMesh.objectPosition, poiCam.worldPos);
	float distanceFalloff = clamp((distanceToCamera - _ParallaxInternalIterationsDistance.x) / max(_ParallaxInternalIterationsDistance.y - _ParallaxInternalIterationsDistance.x, 1e-5), 0.0, 1.0);
	float minIterations = min(_ParallaxInternalIterationsMin, _ParallaxInternalIterations);
	int iterations = int(round(mix(_ParallaxInternalIterations, minIterations, distanceFalloff)));
	iterations = max(iterations, 1);

	vec3 parallax = vec3(0.0);
	for (int j = iterations - 1; j >= 0; j--)
	{
		float ratio = clamp(float(j) / float(max(iterations - 1, 1)), 0.0, 1.0);
		vec2 parallaxOffset = panBase + ratio * _ParallaxInternalPerLevelOffset;
		float fade = mix(minFade, maxFade, ratio);
		fade = pow(max(fade, 0.0), 2.2);
#if defined(PROP_PARALLAXINTERNALMAP)
		float depth = mix(_ParallaxInternalMinDepth, _ParallaxInternalMaxDepth, ratio);
		vec2 layerOffset = depth * viewOffset + parallaxOffset;
		vec2 layerUV = placedUV + layerOffset * invMapTiling;
		layerUV = _ParallaxInternalTiled >= 0.5 ? layerUV : clamp(layerUV, 0.0, 1.0);
		vec4 parallaxColor = poiTexLod(_ParallaxInternalMap, layerUV * _ParallaxInternalMap_ST.xy + _ParallaxInternalMap_ST.zw, 0.0);
#else
		vec4 parallaxColor = vec4(0.0);
#endif
		vec3 parallaxTint = mix(minColor, maxColor, ratio);
		float parallaxHeight = 0.0;
		if (_ParallaxInternalHeightFromAlpha >= 0.5)
		{
			parallaxTint *= parallaxColor.rgb;
			parallaxHeight = parallaxColor.a;
		}
		else
		{
			parallaxHeight = parallaxColor.r;
		}
		if (_ParallaxInternalHueShiftEnabled >= 0.5 && _ParallaxInternalHueShiftPerLevel != 0.0)
		{
			parallaxTint = hueShift(parallaxTint, fract(ratio * _ParallaxInternalHueShiftPerLevel), _ParallaxInternalHueShiftColorSpace, _ParallaxInternalHueSelectOrShift);
		}
		vec3 contrib = parallaxTint * fade;
		if (_ParallaxInternalHeightmapMode == 1.0)
		{
			parallax = mix(parallax, contrib, float(parallaxHeight >= 1.0 - ratio));
		}
		else
		{
			contrib *= parallaxHeight;
			if (_ParallaxInternalBlendMode == 0.0)
			{
				parallax += contrib;
			}
			else
			{
				parallax = max(parallax, contrib);
			}
		}
	}
	parallax = hueShift(parallax, fract(hueShiftAmount) * _ParallaxInternalHueShiftEnabled, _ParallaxInternalHueShiftColorSpace, _ParallaxInternalHueSelectOrShift);

	poiFragData.baseColor = customBlend(poiFragData.baseColor, parallax, _ParallaxInternalSurfaceBlendMode, parallaxMask);
}
#endif
