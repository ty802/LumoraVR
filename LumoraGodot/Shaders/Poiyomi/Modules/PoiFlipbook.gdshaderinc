// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.

#if defined(POI_FLIPBOOK_0)
void applyFlipbook(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
#if defined(PROP_FLIPBOOKTEXARRAY)
	vec4 flipBookPixel = vec4(0.0);
#if defined(PROP_FLIPBOOKMASK)
	float flipBookMask = poiTexPan(_FlipbookMask, poiUV(poiMesh.uv[int(_FlipbookMaskUV)], _FlipbookMask_ST), _FlipbookMaskPan)[int(_FlipbookMaskChannel)];
#else
	float flipBookMask = 1.0;
#endif
	if (_FlipbookMaskGlobalMask > 0.0)
	{
		flipBookMask = maskBlend(flipBookMask, poiMods.globalMask[int(_FlipbookMaskGlobalMask) - 1], _FlipbookMaskGlobalMaskBlendType);
	}

	vec2 flipbookScale = _FlipbookScale.xy;

	// Aspect ratio correction
	if (_FlipbookAspectRatioMode > 0.0)
	{
		vec3 texDims = vec3(textureSize(_FlipbookTexArray, 0));
		float texWidth = texDims.x;
		float texHeight = texDims.y;

		if (_FlipbookAspectRatioMode == 1.0) // Shrink to Fit
		{
			if (texWidth > texHeight)
				flipbookScale *= vec2(1.0, texHeight / texWidth);
			else
				flipbookScale *= vec2(texWidth / texHeight, 1.0);
		}
		else // Grow to Fit
		{
			if (texWidth > texHeight)
				flipbookScale *= vec2(texWidth / texHeight, 1.0);
			else
				flipbookScale *= vec2(1.0, texHeight / texWidth);
		}
	}

	vec4 sideOffset = vec4(-_FlipbookSideOffset.x, _FlipbookSideOffset.y, -_FlipbookSideOffset.z, _FlipbookSideOffset.w);
	vec2 centerOffset = vec2((sideOffset.x + sideOffset.y) / 2.0, (sideOffset.z + sideOffset.w) / 2.0);
	vec2 uv = poiMesh.uv[int(_FlipbookTexArrayUV)];

	// Symmetry mode
	if (_FlipbookSymmetryMode == 1.0) uv.x = abs(uv.x - 0.5) + 0.5;
	if (_FlipbookSymmetryMode == 2.0 && uv.x < 0.5) uv.x = uv.x + 0.5;

	// Mirrored UV mode
	if ((_FlipbookMirroredUVMode == 1.0 || _FlipbookMirroredUVMode == 4.0) && poiMesh.isRightHand != 0u) uv.x = 1.0 - uv.x;
	if (_FlipbookMirroredUVMode == 2.0 && poiMesh.isRightHand != 0u) uv.x = -1.0;
	if ((_FlipbookMirroredUVMode == 3.0 || _FlipbookMirroredUVMode == 4.0) && poiMesh.isRightHand == 0u) uv.x = -1.0;

	vec2 flipbookCenter = _FlipbookPosition + centerOffset;
	float theta = radians(_FlipbookRotation + POI_TIME.z * _FlipbookRotationSpeed);
	float cs = cos(theta);
	float sn = sin(theta);
	uv = vec2((uv.x - flipbookCenter.x) * cs - (uv.y - flipbookCenter.y) * sn + flipbookCenter.x, (uv.x - flipbookCenter.x) * sn + (uv.y - flipbookCenter.y) * cs + flipbookCenter.y);
	vec2 newUV = remap(uv, vec2(0.0) - flipbookScale / 2.0 + _FlipbookPosition + sideOffset.xz, flipbookScale / 2.0 + _FlipbookPosition + sideOffset.yw, vec2(0.0), vec2(1.0));

	float edgeAlpha = 1.0;

	if (_FlipbookTiled == 0.0)
	{
		vec2 pixelWidth = fwidth(newUV) + 1.0e-7;
		vec2 edgeFactor = (1.0 - abs(newUV * 2.0 - 1.0)) / pixelWidth;
		edgeAlpha = clamp(min(edgeFactor.x, edgeFactor.y), 0.0, 1.0);
	}
	else if (_FlipbookTiled == 2.0)
	{
		vec2 tiledUVs = fract(newUV);
		vec2 pixelWidth = fwidth(newUV) + 1.0e-7;
		vec2 edgeFactor = (1.0 - abs(tiledUVs * 2.0 - 1.0)) / pixelWidth;
		edgeAlpha = clamp(min(edgeFactor.x, edgeFactor.y), 0.0, 1.0);
	}

	float currentFrame = 0.0;
	float totalFrames = float(textureSize(_FlipbookTexArray, 0).z);

	if (_FlipbookStartAndEnd >= 0.5)
	{
		totalFrames -= (totalFrames - min(max(_FlipbookStartFrame, _FlipbookEndFrame), totalFrames));
		totalFrames -= max(0.0, _FlipbookStartFrame);
	}
	if (_FlipbookManualFrameControl < 0.5)
	{
		if (_FlipbookFPS != 0.0)
		{
			float framePosition = (POI_TIME.y / (1.0 / _FlipbookFPS)) + _FlipbookFrameOffset;
			currentFrame = poiFmod(framePosition, totalFrames);
			if (_FlipbookStartAndEnd >= 0.5)
			{
				currentFrame += _FlipbookStartFrame;
			}
		}
	}
	else
	{
		currentFrame = poiFmod(_FlipbookCurrentFrame, totalFrames);
	}

	vec2 finalUV = poiUV(newUV, _FlipbookTexArray_ST) + POI_TIME.x * _FlipbookTexArrayPan;
	vec2 ddxUV = dFdx(finalUV);
	vec2 ddyUV = dFdy(finalUV);
	flipBookPixel = poiTexArrayGrad(_FlipbookTexArray, vec3(finalUV, floor(currentFrame)), ddxUV, ddyUV);

	if (_FlipbookCrossfadeEnabled >= 0.5)
	{
		float totalFramesCF = totalFrames;
		if (_FlipbookStartAndEnd >= 0.5)
		{
			totalFramesCF += max(0.0, _FlipbookStartFrame);
		}
		float nextFrame = currentFrame + 1.0;
		vec4 flipbookNextPixel = poiTexArrayGrad(_FlipbookTexArray, vec3(finalUV, floor(poiFmod(nextFrame, totalFramesCF))), ddxUV, ddyUV);
		flipBookPixel = mix(flipBookPixel, flipbookNextPixel, smoothstep(_FlipbookCrossfadeRange.x, _FlipbookCrossfadeRange.y, fract(currentFrame)));
	}

	if (_FlipbookIntensityControlsAlpha >= 0.5)
	{
		flipBookPixel.a = poiMax(flipBookPixel.rgb) * edgeAlpha;
	}
	else
	{
		flipBookPixel.a *= edgeAlpha;
	}
	if (_FlipbookColorReplaces >= 0.5)
	{
		flipBookPixel.rgb = poiThemeColor(poiMods, _FlipbookColor.rgb, _FlipbookColorThemeIndex);
	}
	else
	{
		flipBookPixel.rgb *= poiThemeColor(poiMods, _FlipbookColor.rgb, _FlipbookColorThemeIndex);
	}

	if (_FlipbookHueShiftEnabled >= 0.5)
	{
		flipBookPixel.rgb = hueShift(flipBookPixel.rgb, _FlipbookHueShift + POI_TIME.x * _FlipbookHueShiftSpeed, _FlipbookHueShiftColorSpace, _FlipbookHueSelectOrShift);
	}
	float flipbookAlpha = _FlipbookAlpha;
	flipbookAlpha = clamp(flipbookAlpha, 0.0, 1.0);

#if !defined(POI_PASS_OUTLINE)
	float finalFlipbookAlpha = clamp(flipBookPixel.a * _FlipbookColor.a * flipbookAlpha * flipBookMask, 0.0, 1.0);

	if (_FlipbookAlphaControlsFinalAlpha == 2.0)
	{
		poiFragData.baseColor = flipBookPixel.rgb;
		poiFragData.alpha = finalFlipbookAlpha;
	}
	else
	{
		poiFragData.baseColor = mix(poiFragData.baseColor.rgb, customBlend(poiFragData.baseColor.rgb, flipBookPixel.rgb, _FlipbookBlendType), finalFlipbookAlpha);
	}

	float flipbookEmissionStrength = _FlipbookEmissionStrength;
	poiFragData.emission += mix(vec3(0.0), flipBookPixel.rgb * flipbookEmissionStrength, finalFlipbookAlpha);
#endif

	if (_FlipbookAlphaControlsFinalAlpha > 0.0 && _FlipbookAlphaControlsFinalAlpha != 2.0)
	{
		poiFragData.alpha = mix(poiFragData.alpha, clamp(flipBookPixel.a * _FlipbookColor.a * flipbookAlpha, 0.0, 1.0), flipBookMask);
	}
#endif
}
#endif

#if defined(POI_FLIPBOOK_1)
void applyFlipbook1(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
#if defined(PROP_FLIPBOOKTEXARRAY1)
	vec4 flipBookPixel__1 = vec4(0.0);
#if defined(PROP_FLIPBOOKMASK1)
	float flipBookMask__1 = poiTexPan(_FlipbookMask1, poiUV(poiMesh.uv[int(_FlipbookMaskUV1)], _FlipbookMask1_ST), _FlipbookMaskPan1)[int(_FlipbookMaskChannel1)];
#else
	float flipBookMask__1 = 1.0;
#endif
	if (_FlipbookMaskGlobalMask1 > 0.0)
	{
		flipBookMask__1 = maskBlend(flipBookMask__1, poiMods.globalMask[int(_FlipbookMaskGlobalMask1) - 1], _FlipbookMaskGlobalMaskBlendType1);
	}

	vec2 flipbookScale__1 = _FlipbookScale1.xy;

	// Aspect ratio correction
	if (_FlipbookAspectRatioMode1 > 0.0)
	{
		vec3 texDims__1 = vec3(textureSize(_FlipbookTexArray1, 0));
		float texWidth__1 = texDims__1.x;
		float texHeight__1 = texDims__1.y;

		if (_FlipbookAspectRatioMode1 == 1.0) // Shrink to Fit
		{
			if (texWidth__1 > texHeight__1)
				flipbookScale__1 *= vec2(1.0, texHeight__1 / texWidth__1);
			else
				flipbookScale__1 *= vec2(texWidth__1 / texHeight__1, 1.0);
		}
		else // Grow to Fit
		{
			if (texWidth__1 > texHeight__1)
				flipbookScale__1 *= vec2(texWidth__1 / texHeight__1, 1.0);
			else
				flipbookScale__1 *= vec2(1.0, texHeight__1 / texWidth__1);
		}
	}

	vec4 sideOffset__1 = vec4(-_FlipbookSideOffset1.x, _FlipbookSideOffset1.y, -_FlipbookSideOffset1.z, _FlipbookSideOffset1.w);
	vec2 centerOffset__1 = vec2((sideOffset__1.x + sideOffset__1.y) / 2.0, (sideOffset__1.z + sideOffset__1.w) / 2.0);
	vec2 uv__1 = poiMesh.uv[int(_FlipbookTexArrayUV1)];

	// Symmetry mode
	if (_FlipbookSymmetryMode1 == 1.0) uv__1.x = abs(uv__1.x - 0.5) + 0.5;
	if (_FlipbookSymmetryMode1 == 2.0 && uv__1.x < 0.5) uv__1.x = uv__1.x + 0.5;

	// Mirrored UV mode
	if ((_FlipbookMirroredUVMode1 == 1.0 || _FlipbookMirroredUVMode1 == 4.0) && poiMesh.isRightHand != 0u) uv__1.x = 1.0 - uv__1.x;
	if (_FlipbookMirroredUVMode1 == 2.0 && poiMesh.isRightHand != 0u) uv__1.x = -1.0;
	if ((_FlipbookMirroredUVMode1 == 3.0 || _FlipbookMirroredUVMode1 == 4.0) && poiMesh.isRightHand == 0u) uv__1.x = -1.0;

	vec2 flipbookCenter__1 = _FlipbookPosition1 + centerOffset__1;
	float theta__1 = radians(_FlipbookRotation1 + POI_TIME.z * _FlipbookRotationSpeed1);
	float cs__1 = cos(theta__1);
	float sn__1 = sin(theta__1);
	uv__1 = vec2((uv__1.x - flipbookCenter__1.x) * cs__1 - (uv__1.y - flipbookCenter__1.y) * sn__1 + flipbookCenter__1.x, (uv__1.x - flipbookCenter__1.x) * sn__1 + (uv__1.y - flipbookCenter__1.y) * cs__1 + flipbookCenter__1.y);
	vec2 newUV__1 = remap(uv__1, vec2(0.0) - flipbookScale__1 / 2.0 + _FlipbookPosition1 + sideOffset__1.xz, flipbookScale__1 / 2.0 + _FlipbookPosition1 + sideOffset__1.yw, vec2(0.0), vec2(1.0));

	float edgeAlpha__1 = 1.0;

	if (_FlipbookTiled1 == 0.0)
	{
		vec2 pixelWidth__1 = fwidth(newUV__1) + 1.0e-7;
		vec2 edgeFactor__1 = (1.0 - abs(newUV__1 * 2.0 - 1.0)) / pixelWidth__1;
		edgeAlpha__1 = clamp(min(edgeFactor__1.x, edgeFactor__1.y), 0.0, 1.0);
	}
	else if (_FlipbookTiled1 == 2.0)
	{
		vec2 tiledUVs__1 = fract(newUV__1);
		vec2 pixelWidth__1 = fwidth(newUV__1) + 1.0e-7;
		vec2 edgeFactor__1 = (1.0 - abs(tiledUVs__1 * 2.0 - 1.0)) / pixelWidth__1;
		edgeAlpha__1 = clamp(min(edgeFactor__1.x, edgeFactor__1.y), 0.0, 1.0);
	}

	float currentFrame__1 = 0.0;
	float totalFrames__1 = float(textureSize(_FlipbookTexArray1, 0).z);

	if (_FlipbookStartAndEnd1 >= 0.5)
	{
		totalFrames__1 -= (totalFrames__1 - min(max(_FlipbookStartFrame1, _FlipbookEndFrame1), totalFrames__1));
		totalFrames__1 -= max(0.0, _FlipbookStartFrame1);
	}
	if (_FlipbookManualFrameControl1 < 0.5)
	{
		if (_FlipbookFPS1 != 0.0)
		{
			float framePosition__1 = (POI_TIME.y / (1.0 / _FlipbookFPS1)) + _FlipbookFrameOffset1;
			currentFrame__1 = poiFmod(framePosition__1, totalFrames__1);
			if (_FlipbookStartAndEnd1 >= 0.5)
			{
				currentFrame__1 += _FlipbookStartFrame1;
			}
		}
	}
	else
	{
		currentFrame__1 = poiFmod(_FlipbookCurrentFrame1, totalFrames__1);
	}

	vec2 finalUV__1 = poiUV(newUV__1, _FlipbookTexArray1_ST) + POI_TIME.x * _FlipbookTexArrayPan1;
	vec2 ddxUV__1 = dFdx(finalUV__1);
	vec2 ddyUV__1 = dFdy(finalUV__1);
	flipBookPixel__1 = poiTexArrayGrad(_FlipbookTexArray1, vec3(finalUV__1, floor(currentFrame__1)), ddxUV__1, ddyUV__1);

	if (_FlipbookCrossfadeEnabled1 >= 0.5)
	{
		float totalFramesCF__1 = totalFrames__1;
		if (_FlipbookStartAndEnd1 >= 0.5)
		{
			totalFramesCF__1 += max(0.0, _FlipbookStartFrame1);
		}
		float nextFrame__1 = currentFrame__1 + 1.0;
		vec4 flipbookNextPixel__1 = poiTexArrayGrad(_FlipbookTexArray1, vec3(finalUV__1, floor(poiFmod(nextFrame__1, totalFramesCF__1))), ddxUV__1, ddyUV__1);
		flipBookPixel__1 = mix(flipBookPixel__1, flipbookNextPixel__1, smoothstep(_FlipbookCrossfadeRange1.x, _FlipbookCrossfadeRange1.y, fract(currentFrame__1)));
	}

	if (_FlipbookIntensityControlsAlpha1 >= 0.5)
	{
		flipBookPixel__1.a = poiMax(flipBookPixel__1.rgb) * edgeAlpha__1;
	}
	else
	{
		flipBookPixel__1.a *= edgeAlpha__1;
	}
	if (_FlipbookColorReplaces1 >= 0.5)
	{
		flipBookPixel__1.rgb = poiThemeColor(poiMods, _FlipbookColor1.rgb, _FlipbookColorThemeIndex1);
	}
	else
	{
		flipBookPixel__1.rgb *= poiThemeColor(poiMods, _FlipbookColor1.rgb, _FlipbookColorThemeIndex1);
	}

	if (_FlipbookHueShiftEnabled1 >= 0.5)
	{
		flipBookPixel__1.rgb = hueShift(flipBookPixel__1.rgb, _FlipbookHueShift1 + POI_TIME.x * _FlipbookHueShiftSpeed1, _FlipbookHueShiftColorSpace1, _FlipbookHueSelectOrShift1);
	}
	float flipbookAlpha__1 = _FlipbookAlpha1;
	flipbookAlpha__1 = clamp(flipbookAlpha__1, 0.0, 1.0);

#if !defined(POI_PASS_OUTLINE)
	float finalFlipbookAlpha__1 = clamp(flipBookPixel__1.a * _FlipbookColor1.a * flipbookAlpha__1 * flipBookMask__1, 0.0, 1.0);

	if (_FlipbookAlphaControlsFinalAlpha1 == 2.0)
	{
		poiFragData.baseColor = flipBookPixel__1.rgb;
		poiFragData.alpha = finalFlipbookAlpha__1;
	}
	else
	{
		poiFragData.baseColor = mix(poiFragData.baseColor.rgb, customBlend(poiFragData.baseColor.rgb, flipBookPixel__1.rgb, _FlipbookBlendType1), finalFlipbookAlpha__1);
	}

	float flipbookEmissionStrength__1 = _FlipbookEmissionStrength1;
	poiFragData.emission += mix(vec3(0.0), flipBookPixel__1.rgb * flipbookEmissionStrength__1, finalFlipbookAlpha__1);
#endif

	if (_FlipbookAlphaControlsFinalAlpha1 > 0.0 && _FlipbookAlphaControlsFinalAlpha1 != 2.0)
	{
		poiFragData.alpha = mix(poiFragData.alpha, clamp(flipBookPixel__1.a * _FlipbookColor1.a * flipbookAlpha__1, 0.0, 1.0), flipBookMask__1);
	}
#endif
}
#endif
