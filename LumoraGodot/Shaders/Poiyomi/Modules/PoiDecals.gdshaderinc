// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Decals 0-3. Nothing here reads the light (nDotVCentered is view only), so decals run whole in fragment()
// and have no light hook. The video layer keeps its options but has no video source: it never samples.

#if defined(POI_DECAL_0) || defined(POI_DECAL_1) || defined(POI_DECAL_2) || defined(POI_DECAL_3)
#define POI_DECAL_SAMPLE_BOUND 2

vec2 decalUV(float uvNumber, vec2 position, float rotation, float rotationSpeed, vec2 scale, vec4 scaleOffset, float depth, in float symmetryMode, in float mirroredUVMode, in PoiMesh poiMesh, in PoiCam poiCam)
{
	scaleOffset = vec4(-scaleOffset.x, scaleOffset.y, -scaleOffset.z, scaleOffset.w);
	vec2 centerOffset = vec2((scaleOffset.x + scaleOffset.y) / 2.0, (scaleOffset.z + scaleOffset.w) / 2.0);
	vec2 uv = poiMesh.uv[int(uvNumber)];
	// Copy
	if (symmetryMode == 1.0) uv.x = abs(uv.x - 0.5) + 0.5;
	// Flip
	if (symmetryMode == 2.0 && uv.x < 0.5) uv.x = uv.x + 0.5;
	if ((mirroredUVMode == 1.0 || mirroredUVMode == 4.0) && poiMesh.isRightHand != 0u) uv.x = 1.0 - uv.x;
	// Hide
	if (mirroredUVMode == 2.0 && poiMesh.isRightHand != 0u) uv.x = -1.0;
	if ((mirroredUVMode == 3.0 || mirroredUVMode == 4.0) && poiMesh.isRightHand == 0u) uv.x = -1.0;

	uv += calcParallax(depth + 1.0, poiCam);
	vec2 decalCenter = position + centerOffset;
	float theta = radians(rotation + POI_TIME.z * rotationSpeed);
	float sn = sin(theta);
	float cs = cos(theta);
	uv = vec2((uv.x - decalCenter.x) * cs - (uv.y - decalCenter.y) * sn + decalCenter.x, (uv.x - decalCenter.x) * sn + (uv.y - decalCenter.y) * cs + decalCenter.y);
	uv = remap(uv, vec2(0.0) - scale / 2.0 + position + scaleOffset.xz, scale / 2.0 + position + scaleOffset.yw, vec2(0.0), vec2(1.0));
	return uv;
}

vec3 decalHueShift(float enabled, vec3 color, float shift, float shiftSpeed, float colorSpace, float selectOrShift)
{
	if (enabled != 0.0)
	{
		color = hueShift(color, shift + POI_TIME.x * shiftSpeed, colorSpace, selectOrShift);
	}
	return color;
}

vec3 decalColorAdjust(float enabled, vec3 color, float saturation, float chromatize, float brightness, float gamma)
{
	if (enabled == 0.0)
	{
		return color;
	}
	if (gamma != 1.0)
	color = sign(color) * pow(abs(color), vec3(gamma));
	if (saturation != 0.0)
	color = mix(color, vec3(dot(color, vec3(0.3, 0.59, 0.11))), -saturation);
	if (chromatize != 0.0)
	{
		vec3 chromaLinear = color;
		chromaLinear = PoiApplyChromatize(chromaLinear, chromatize);
		color = clamp(chromaLinear, 0.0, 1.0);
	}
	if (brightness != 0.0)
	color = color * (brightness + 1.0);
	color = clamp(color, 0.0, 1.0);
	return color;
}

float applyTilingClipping(float enabled, vec2 uv)
{
	float ret = 1.0;
	if (enabled == 0.0)
	{
		if (uv.x > 1.0 || uv.y > 1.0 || uv.x < 0.0 || uv.y < 0.0)
		{
			ret = 0.0;
		}
	}
	return ret;
}

struct PoiDecal
{
	float m_DecalFaceMask;
	float m_DecalMaskChannel;
	float m_DecalGlobalMask;
	float m_DecalGlobalMaskBlendType;
	float m_DecalApplyGlobalMaskIndex;
	float m_DecalApplyGlobalMaskBlendType;
	vec4 m_DecalTexture_ST;
	vec2 m_DecalTexturePan;
	float m_DecalTextureUV;
	vec4 m_DecalColor;
	float m_DecalColorThemeIndex;
	float m_DecalTiled;
	float m_DecalBlendType;
	float m_DecalRotation;
	vec3 m_DecalScale;
	vec4 m_DecalSideOffset;
	vec2 m_DecalPosition;
	float m_DecalRotationSpeed;
	float m_DecalEmissionStrength;
	float m_DecalBlendAlpha;
	float m_DecalAlphaIntensity;
	float m_DecalAlphaBlendMode;
	float m_DecalHueShiftColorSpace;
	float m_DecalHueShiftSelectOrShift;
	float m_DecalHueShiftEnabled;
	float m_DecalHueShift;
	float m_DecalHueShiftSpeed;
	float m_DecalColorAdjustEnabled;
	float m_DecalSaturation;
	float m_DecalChromatize;
	float m_DecalBrightness;
	float m_DecalGamma;
	float m_DecalDepth;
	float m_DecalHueAngleStrength;
	float m_DecalChannelSeparationEnable;
	float m_DecalChannelSeparation;
	float m_DecalChannelSeparationPremultiply;
	float m_DecalChannelSeparationHue;
	float m_DecalChannelSeparationVertical;
	float m_DecalChannelSeparationAngleStrength;
	float m_DecalOverrideAlphaMode;
	float m_DecalOverrideAlpha;
	float m_DecalSymmetryMode;
	float m_DecalMirroredUVMode;
	float m_DecalAspectRatioMode;

	vec4 decalColor;
	vec2 decalScale;
	float decalRotation;
	vec2 uv;
	vec4 dduv;
	vec4 sideMod;
	float decalChannelOffset;
	vec4 decalMask;
	vec3 screenEmission;
	float useScreenEffect;
};

PoiDecal PoiDecal_zero()
{
	PoiDecal r;
	r.m_DecalFaceMask = 0.0;
	r.m_DecalMaskChannel = 0.0;
	r.m_DecalGlobalMask = 0.0;
	r.m_DecalGlobalMaskBlendType = 0.0;
	r.m_DecalApplyGlobalMaskIndex = 0.0;
	r.m_DecalApplyGlobalMaskBlendType = 0.0;
	r.m_DecalTexture_ST = vec4(0.0);
	r.m_DecalTexturePan = vec2(0.0);
	r.m_DecalTextureUV = 0.0;
	r.m_DecalColor = vec4(0.0);
	r.m_DecalColorThemeIndex = 0.0;
	r.m_DecalTiled = 0.0;
	r.m_DecalBlendType = 0.0;
	r.m_DecalRotation = 0.0;
	r.m_DecalScale = vec3(0.0);
	r.m_DecalSideOffset = vec4(0.0);
	r.m_DecalPosition = vec2(0.0);
	r.m_DecalRotationSpeed = 0.0;
	r.m_DecalEmissionStrength = 0.0;
	r.m_DecalBlendAlpha = 0.0;
	r.m_DecalAlphaIntensity = 0.0;
	r.m_DecalAlphaBlendMode = 0.0;
	r.m_DecalHueShiftColorSpace = 0.0;
	r.m_DecalHueShiftSelectOrShift = 0.0;
	r.m_DecalHueShiftEnabled = 0.0;
	r.m_DecalHueShift = 0.0;
	r.m_DecalHueShiftSpeed = 0.0;
	r.m_DecalColorAdjustEnabled = 0.0;
	r.m_DecalSaturation = 0.0;
	r.m_DecalChromatize = 0.0;
	r.m_DecalBrightness = 0.0;
	r.m_DecalGamma = 0.0;
	r.m_DecalDepth = 0.0;
	r.m_DecalHueAngleStrength = 0.0;
	r.m_DecalChannelSeparationEnable = 0.0;
	r.m_DecalChannelSeparation = 0.0;
	r.m_DecalChannelSeparationPremultiply = 0.0;
	r.m_DecalChannelSeparationHue = 0.0;
	r.m_DecalChannelSeparationVertical = 0.0;
	r.m_DecalChannelSeparationAngleStrength = 0.0;
	r.m_DecalOverrideAlphaMode = 0.0;
	r.m_DecalOverrideAlpha = 0.0;
	r.m_DecalSymmetryMode = 0.0;
	r.m_DecalMirroredUVMode = 0.0;
	r.m_DecalAspectRatioMode = 0.0;
	r.decalColor = vec4(0.0);
	r.decalScale = vec2(0.0);
	r.decalRotation = 0.0;
	r.uv = vec2(0.0);
	r.dduv = vec4(0.0);
	r.sideMod = vec4(0.0);
	r.decalChannelOffset = 0.0;
	r.decalMask = vec4(0.0);
	r.screenEmission = vec3(0.0);
	r.useScreenEffect = 0.0;
	return r;
}

// PoiDecal's member functions in the source, taking the decal as their first parameter.
void PoiDecal_Init(inout PoiDecal d, in vec4 DecalMask)
{
	// Only the first four decals map onto the shared RGBA mask; the rest have no channel of
	// their own and are left unmasked rather than falling back onto someone else's.
	d.decalMask = d.m_DecalMaskChannel > 3.0 ? vec4(1.0) : DecalMask;
	d.m_DecalMaskChannel = min(d.m_DecalMaskChannel, 3.0);
	d.decalScale = d.m_DecalScale.xy;
}

void PoiDecal_ApplyAspectRatio(inout PoiDecal d, vec2 texDimensions)
{
	if (d.m_DecalAspectRatioMode == 1.0) // Shrink to Fit
	{
		if (texDimensions.x > texDimensions.y)
		d.decalScale *= vec2(1.0, texDimensions.y / texDimensions.x);
		else
		d.decalScale *= vec2(texDimensions.x / texDimensions.y, 1.0);
	}
	else if (d.m_DecalAspectRatioMode == 2.0) // Grow to Fit
	{
		if (texDimensions.x > texDimensions.y)
		d.decalScale *= vec2(1.0 / (texDimensions.y / texDimensions.x), 1.0);
		else
		d.decalScale *= vec2(1.0, 1.0 / (texDimensions.x / texDimensions.y));
	}
}

// The decal's own texture by slot: a function may not take the texture itself, every sampler a shader passes
// to one function must share its filter and repeat settings.
vec4 poiDecalTexGrad(int decalIndex, vec2 uv, vec2 dx, vec2 dy)
{
#if defined(POI_DECAL_0)
	if (decalIndex == 0) return poiTexGrad(_DecalTexture, uv, dx, dy);
#endif
#if defined(POI_DECAL_1)
	if (decalIndex == 1) return poiTexGrad(_DecalTexture1, uv, dx, dy);
#endif
#if defined(POI_DECAL_2)
	if (decalIndex == 2) return poiTexGrad(_DecalTexture2, uv, dx, dy);
#endif
#if defined(POI_DECAL_3)
	if (decalIndex == 3) return poiTexGrad(_DecalTexture3, uv, dx, dy);
#endif
	return vec4(1.0);
}

void PoiDecal_SampleDecalLayers(inout PoiDecal d, int decalIndex, bool videoEnabled, bool videoAvailable, bool onlyVideo, bool keepDecalAlpha, bool channelSeparation, bool localTextureAvailable, vec2 videoAspectRatio, in PoiMods poiMods, in PoiLight poiLight, in PoiMesh poiMesh, in PoiCam poiCam)
{
	bool separateChannels = !videoEnabled && channelSeparation && localTextureAvailable;
	vec2 positionOffset = vec2(0.0);
	if (separateChannels)
	{
		d.decalChannelOffset += d.m_DecalChannelSeparation + d.m_DecalChannelSeparationAngleStrength * (d.m_DecalChannelSeparationAngleStrength > 0.0 ? (1.0 - poiLight.nDotVCentered) : poiLight.nDotVCentered);
		positionOffset = d.decalChannelOffset * 0.01 * (d.decalScale.x + d.decalScale.y) * vec2(cos(d.m_DecalChannelSeparationVertical), sin(d.m_DecalChannelSeparationVertical));
	}
	// Share UV and color processing across local/video layers and separated channels.
	for (int layer = 0; layer < 2; ++layer)
	{
		bool videoLayer = layer == 1;
		bool sampleLayer = videoLayer ? (videoEnabled && videoAvailable) : (!videoEnabled || !onlyVideo || (videoAvailable && keepDecalAlpha));
		if (sampleLayer)
		{
			bool separatedLayer = !videoLayer && separateChannels;
			int sampleCount = separatedLayer ? 2 : 1;
			vec4 samples[2] = vec4[2](vec4(0.0), vec4(0.0));
			float clips[2] = float[2](0.0, 0.0);
			float previousAlpha = d.decalColor.a;
			for (int sampleIndex = 0; sampleIndex < POI_DECAL_SAMPLE_BOUND; ++sampleIndex)
			{
				if (sampleIndex >= sampleCount) break;
				vec2 samplePosition = d.m_DecalPosition + (separatedLayer ? (sampleIndex == 0 ? positionOffset : -positionOffset) : vec2(0.0));
				vec2 sampleScale = d.decalScale * (videoLayer ? videoAspectRatio : vec2(1.0));
				vec2 sampleUV = decalUV(d.m_DecalTextureUV, samplePosition, d.m_DecalRotation + d.decalRotation, d.m_DecalRotationSpeed, sampleScale, d.m_DecalSideOffset + d.sideMod, d.m_DecalDepth, d.m_DecalSymmetryMode, d.m_DecalMirroredUVMode, poiMesh, poiCam);
				vec4 decalSampleColor = vec4(poiThemeColor(poiMods, d.m_DecalColor.rgb, d.m_DecalColorThemeIndex), d.m_DecalColor.a);
				if (videoLayer || videoEnabled || localTextureAvailable)
				{
					vec4 derivatives = any(greaterThan(fwidth(sampleUV), vec2(0.5))) ? vec4(0.001) : vec4(dFdx(sampleUV) * d.m_DecalTexture_ST.xy, dFdy(sampleUV) * d.m_DecalTexture_ST.xy);
					vec2 textureUV = poiUV(sampleUV, d.m_DecalTexture_ST) + d.m_DecalTexturePan * POI_TIME.x;
					// The video layer has no source to fetch.
					if (!videoLayer)
					decalSampleColor *= poiDecalTexGrad(decalIndex, textureUV, derivatives.xy, derivatives.zw);
				}
				decalSampleColor.rgb = decalHueShift(d.m_DecalHueShiftEnabled, decalSampleColor.rgb, d.m_DecalHueShift + poiLight.nDotVCentered * d.m_DecalHueAngleStrength, d.m_DecalHueShiftSpeed, d.m_DecalHueShiftColorSpace, d.m_DecalHueShiftSelectOrShift);
				decalSampleColor.rgb = decalColorAdjust(d.m_DecalColorAdjustEnabled, decalSampleColor.rgb, d.m_DecalSaturation, d.m_DecalChromatize, d.m_DecalBrightness, d.m_DecalGamma);
				samples[sampleIndex] = decalSampleColor;
				clips[sampleIndex] = applyTilingClipping(d.m_DecalTiled, sampleUV);
				// Preserve the original channel-separation path's handling of the stored UV.
				if (!separatedLayer) d.uv = sampleUV;
			}
			if (separatedLayer)
			{
				vec3 channelSeparationColor = HUEtoRGB(fract(d.m_DecalChannelSeparationHue));
				if (d.m_DecalChannelSeparationPremultiply != 0.0)
				d.decalColor.rgb = mix(samples[0].rgb * samples[0].a, samples[1].rgb * samples[1].a, channelSeparationColor);
				else
				d.decalColor.rgb = mix(samples[0].rgb, samples[1].rgb, channelSeparationColor);
				d.decalColor.a = 0.5 * (samples[0].a + samples[1].a);
				d.decalColor.a *= d.decalMask[int(d.m_DecalMaskChannel)] * max(clips[0], clips[1]);
			}
			else
			{
				d.decalColor = samples[0];
				if (videoLayer && keepDecalAlpha) d.decalColor.a = previousAlpha;
				d.decalColor.a *= d.decalMask[int(d.m_DecalMaskChannel)] * clips[0];
			}
		}
	}
}

void PoiDecal_Apply(inout PoiDecal d, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, in PoiLight poiLight)
{
	if (d.m_DecalGlobalMask > 0.0)
	{
		d.decalColor.a = maskBlend(d.decalColor.a, poiMods.globalMask[int(d.m_DecalGlobalMask) - 1], d.m_DecalGlobalMaskBlendType);
	}

	// Hide
	if (d.m_DecalMirroredUVMode == 2.0 && poiMesh.isRightHand != 0u) d.decalColor.a = 0.0;
	if ((d.m_DecalMirroredUVMode == 3.0 || d.m_DecalMirroredUVMode == 4.0) && poiMesh.isRightHand == 0u) d.decalColor.a = 0.0;

	bool faceMaskedOut = false;
	if (d.m_DecalFaceMask > 0.0)
	{
		if (d.m_DecalFaceMask == 1.0 && poiMesh.isFrontFace == 0.0)
		{
			d.decalColor.a *= 0.0;
			faceMaskedOut = true;
		}
		else if (d.m_DecalFaceMask == 2.0 && poiMesh.isFrontFace != 0.0)
		{
			d.decalColor.a *= 0.0;
			faceMaskedOut = true;
		}
	}

	float decalAlphaMixed = d.decalColor.a * clamp(d.m_DecalBlendAlpha, 0.0, 1.0);

	if (d.m_DecalOverrideAlpha != 0.0 && !faceMaskedOut)
	{
		float finalAlpha = decalAlphaMixed;
		float maskWeight = d.decalMask[int(d.m_DecalMaskChannel)];

		// Apply global mask if set
		if (d.m_DecalGlobalMask > 0.0)
		{
			maskWeight = maskBlend(maskWeight, poiMods.globalMask[int(d.m_DecalGlobalMask) - 1], d.m_DecalGlobalMaskBlendType);
		}

		// Mode 0: Everywhere, Mode 1: Bounds, Mode 2: Everywhere Masked, Mode 3: Bounds Masked
		bool applyMask = (d.m_DecalOverrideAlphaMode == 2.0 || d.m_DecalOverrideAlphaMode == 3.0);
		bool checkBounds = (d.m_DecalOverrideAlphaMode == 1.0 || d.m_DecalOverrideAlphaMode == 3.0) && d.m_DecalTiled == 0.0;

		if (!checkBounds || (d.uv.x >= 0.0 && d.uv.x <= 1.0 && d.uv.y >= 0.0 && d.uv.y <= 1.0))
		{
			float weight = applyMask ? maskWeight : 1.0;

			if (d.m_DecalOverrideAlpha == 1.0) poiFragData.alpha = mix(poiFragData.alpha, finalAlpha, weight);
			if (d.m_DecalOverrideAlpha == 2.0) poiFragData.alpha = mix(poiFragData.alpha, clamp(poiFragData.alpha * finalAlpha, 0.0, 1.0), weight);
			if (d.m_DecalOverrideAlpha == 3.0) poiFragData.alpha = mix(poiFragData.alpha, clamp(poiFragData.alpha + finalAlpha, 0.0, 1.0), weight);
			if (d.m_DecalOverrideAlpha == 4.0) poiFragData.alpha = mix(poiFragData.alpha, clamp(poiFragData.alpha - finalAlpha, 0.0, 1.0), weight);
			if (d.m_DecalOverrideAlpha == 5.0) poiFragData.alpha = mix(poiFragData.alpha, min(poiFragData.alpha, finalAlpha), weight);
			if (d.m_DecalOverrideAlpha == 6.0) poiFragData.alpha = mix(poiFragData.alpha, max(poiFragData.alpha, finalAlpha), weight);
		}
	}

	decalAlphaMixed = clamp(d.m_DecalAlphaIntensity * decalAlphaMixed, 0.0, 1.0);

	if (d.m_DecalApplyGlobalMaskIndex > 0.0)
	{
		applyToGlobalMask(poiMods, int(d.m_DecalApplyGlobalMaskIndex) - 1, int(d.m_DecalApplyGlobalMaskBlendType), decalAlphaMixed);
	}

	poiFragData.baseColor.rgb = mix(poiFragData.baseColor.rgb, customBlend(poiFragData.baseColor.rgb, d.decalColor.rgb, d.m_DecalBlendType, 1.0), decalAlphaMixed);
	if (d.useScreenEffect != 0.0)
	{
		// Opaque screen decal occludes emission already on the surface (the main Video Effects glow of
		// the texture below) so it can't bleed through. Then contributes its own screen glow.
		poiFragData.emission = mix(poiFragData.emission, d.screenEmission, decalAlphaMixed);
	}
	else
	{
		// Mix the incoming emission so it doesn't bleed through
		poiFragData.emission *= (1.0 - decalAlphaMixed);
		poiFragData.emission += d.decalColor.rgb * d.decalColor.a * max(d.m_DecalEmissionStrength, 0.0);
	}
}

void PoiDecal_ApplyVideoEffects(inout PoiDecal d, float useVideoEffects, in PoiCam poiCam, in PoiMesh poiMesh)
{
#if defined(POI_VIDEO_EFFECTS) && defined(POI_PASS_BASE)
	if (useVideoEffects != 0.0 && _VideoEffectsEnable >= 0.5)
	{
		PoiScreenEffect decalSE;
		decalSE.type = int(_VideoType);
		decalSE.backlight = _VideoBacklight;
		decalSE.contrast = _VideoContrast;
		decalSE.saturation = _VideoSaturation;
		decalSE.resolution = _VideoResolution;
		decalSE.crtRefreshRate = _VideoCRTRefreshRate;
		decalSE.crtPixelEnergizedTime = _VideoCRTPixelEnergizedTime;

		vec3 decalScreenPixels = vec3(1.0);
#if defined(PROP_VIDEOPIXELTEXTURE)
		decalScreenPixels = poiTex(_VideoPixelTexture, poiUV(poiMesh.uv[int(_VideoPixelTextureUV)], _VideoPixelTexture_ST) * decalSE.resolution).rgb;
#endif

		// Drive the screen UV from the mesh's main-tex UV (scale-independent), matching the main effect.
		vec2 screenUV = poiMesh.uv[int(_MainTexUV)];
		if (_VideoPixelateToResolution >= 0.5)
		{
			screenUV = poiSnapUVToGrid(screenUV, _VideoResolution);
		}
		screenUV = poiUV(screenUV, _MainTex_ST) + POI_TIME.x * _MainTexPan;

		poiApplyScreenEffect(d.decalColor, decalSE, decalScreenPixels, screenUV, poiCam, poiMesh);

		d.useScreenEffect = 1.0;
		d.screenEmission = _VideoEmissionEnabled != 0.0 ? d.decalColor.rgb : vec3(0.0);
	}
#endif
}
#endif

#if defined(POI_DECAL_0)
void applyDecals(in vec4 decalMask, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, in PoiLight poiLight)
{
	bool videoTexAvailable = false;
	vec2 videoAspectRatio = vec2(1.0);

	PoiDecal decalData;
	PoiInitStruct(PoiDecal, decalData)
	decalData.m_DecalFaceMask = _Decal0FaceMask;
	decalData.m_DecalMaskChannel = _Decal0MaskChannel;
	decalData.m_DecalGlobalMask = _Decal0GlobalMask;
	decalData.m_DecalGlobalMaskBlendType = _Decal0GlobalMaskBlendType;
	decalData.m_DecalApplyGlobalMaskIndex = _Decal0ApplyGlobalMaskIndex;
	decalData.m_DecalApplyGlobalMaskBlendType = _Decal0ApplyGlobalMaskBlendType;
	decalData.m_DecalTexture_ST = _DecalTexture_ST;
	decalData.m_DecalTexturePan = _DecalTexturePan;
	decalData.m_DecalTextureUV = _DecalTextureUV;
	decalData.m_DecalColor = _DecalColor;
	decalData.m_DecalColorThemeIndex = _DecalColorThemeIndex;
	decalData.m_DecalTiled = _DecalTiled;
	decalData.m_DecalBlendType = _DecalBlendType;
	decalData.m_DecalRotation = _DecalRotation;
	decalData.m_DecalScale = _DecalScale;
	decalData.m_DecalSideOffset = _DecalSideOffset;
	decalData.m_DecalPosition = _DecalPosition;
	decalData.m_DecalRotationSpeed = _DecalRotationSpeed;
	decalData.m_DecalEmissionStrength = _DecalEmissionStrength;
	decalData.m_DecalBlendAlpha = _DecalBlendAlpha;
	decalData.m_DecalAlphaIntensity = _DecalAlphaIntensity;
	decalData.m_DecalOverrideAlpha = _DecalOverrideAlpha;
	decalData.m_DecalHueShiftEnabled = _DecalHueShiftEnabled;
	decalData.m_DecalHueShiftColorSpace = _DecalHueShiftColorSpace;
	decalData.m_DecalHueShiftSelectOrShift = _DecalHueShiftSelectOrShift;
	decalData.m_DecalHueShift = _DecalHueShift;
	decalData.m_DecalHueShiftSpeed = _DecalHueShiftSpeed;
	decalData.m_DecalColorAdjustEnabled = _DecalColorAdjustEnabled;
	decalData.m_DecalSaturation = _DecalSaturation;
	decalData.m_DecalChromatize = _DecalChromatize;
	decalData.m_DecalBrightness = _DecalBrightness;
	decalData.m_DecalGamma = _DecalGamma;
	decalData.m_DecalDepth = _Decal0Depth;
	decalData.m_DecalHueAngleStrength = _Decal0HueAngleStrength;
	decalData.m_DecalChannelSeparationEnable = _Decal0ChannelSeparationEnable;
	decalData.m_DecalChannelSeparation = _Decal0ChannelSeparation;
	decalData.m_DecalChannelSeparationPremultiply = _Decal0ChannelSeparationPremultiply;
	decalData.m_DecalChannelSeparationHue = _Decal0ChannelSeparationHue;
	decalData.m_DecalChannelSeparationVertical = _Decal0ChannelSeparationVertical;
	decalData.m_DecalChannelSeparationAngleStrength = _Decal0ChannelSeparationAngleStrength;
	decalData.m_DecalOverrideAlphaMode = _Decal0OverrideAlphaMode;
	decalData.m_DecalMirroredUVMode = _DecalMirroredUVMode;
	decalData.m_DecalSymmetryMode = _DecalSymmetryMode;
	decalData.m_DecalAspectRatioMode = _Decal0AspectRatioMode;

	PoiDecal_Init(decalData, decalMask);
#if defined(PROP_DECALTEXTURE)
	PoiDecal_ApplyAspectRatio(decalData, poiTexelSize(_DecalTexture).zw);
#endif

	bool decalApply = true;
#if defined(POI_PASS_OUTLINE)
	decalApply = float(_Decal0OnOutlines) >= 0.5;
#endif
	bool videoEnabled = !(_Decal0VideoEnabled < 0.5);
	bool localTextureAvailable = false;
#if defined(PROP_DECALTEXTURE)
	localTextureAvailable = true;
#endif
	if (videoEnabled)
	{
		if (videoTexAvailable)
		decalData.m_DecalEmissionStrength += _Decal0VideoEmissionStrength;
		else if (_Decal0OnlyVideo >= 0.5)
		decalApply = false;
	}
	PoiDecal_SampleDecalLayers(decalData, 0, videoEnabled, videoTexAvailable, _Decal0OnlyVideo >= 0.5, _Decal0UseDecalAlpha >= 0.5, _Decal0ChannelSeparationEnable >= 0.5, localTextureAvailable, videoAspectRatio, poiMods, poiLight, poiMesh, poiCam);

	if (decalApply)
	{
		PoiDecal_ApplyVideoEffects(decalData, _Decal0UseVideoEffects, poiCam, poiMesh);
		PoiDecal_Apply(decalData, poiFragData, poiMesh, poiCam, poiMods, poiLight);
	}
}
#endif

#if defined(POI_DECAL_1)
void applyDecals__1(in vec4 decalMask__1, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, in PoiLight poiLight)
{
	bool videoTexAvailable__1 = false;
	vec2 videoAspectRatio__1 = vec2(1.0);

	PoiDecal decalData__1;
	PoiInitStruct(PoiDecal, decalData__1)
	decalData__1.m_DecalFaceMask = _Decal1FaceMask;
	decalData__1.m_DecalMaskChannel = _Decal1MaskChannel;
	decalData__1.m_DecalGlobalMask = _Decal1GlobalMask;
	decalData__1.m_DecalGlobalMaskBlendType = _Decal1GlobalMaskBlendType;
	decalData__1.m_DecalApplyGlobalMaskIndex = _Decal1ApplyGlobalMaskIndex;
	decalData__1.m_DecalApplyGlobalMaskBlendType = _Decal1ApplyGlobalMaskBlendType;
	decalData__1.m_DecalTexture_ST = _DecalTexture1_ST;
	decalData__1.m_DecalTexturePan = _DecalTexture1Pan;
	decalData__1.m_DecalTextureUV = _DecalTexture1UV;
	decalData__1.m_DecalColor = _DecalColor1;
	decalData__1.m_DecalColorThemeIndex = _DecalColor1ThemeIndex;
	decalData__1.m_DecalTiled = _DecalTiled1;
	decalData__1.m_DecalBlendType = _DecalBlendType1;
	decalData__1.m_DecalRotation = _DecalRotation1;
	decalData__1.m_DecalScale = _DecalScale1;
	decalData__1.m_DecalSideOffset = _DecalSideOffset1;
	decalData__1.m_DecalPosition = _DecalPosition1;
	decalData__1.m_DecalRotationSpeed = _DecalRotationSpeed1;
	decalData__1.m_DecalEmissionStrength = _DecalEmissionStrength1;
	decalData__1.m_DecalBlendAlpha = _DecalBlendAlpha1;
	decalData__1.m_DecalAlphaIntensity = _DecalAlphaIntensity1;
	decalData__1.m_DecalOverrideAlpha = _DecalOverrideAlpha1;
	decalData__1.m_DecalHueShiftEnabled = _DecalHueShiftEnabled1;
	decalData__1.m_DecalHueShiftColorSpace = _DecalHueShiftColorSpace1;
	decalData__1.m_DecalHueShiftSelectOrShift = _DecalHueShiftSelectOrShift1;
	decalData__1.m_DecalHueShift = _DecalHueShift1;
	decalData__1.m_DecalHueShiftSpeed = _DecalHueShiftSpeed1;
	decalData__1.m_DecalColorAdjustEnabled = _DecalColorAdjustEnabled1;
	decalData__1.m_DecalSaturation = _DecalSaturation1;
	decalData__1.m_DecalChromatize = _DecalChromatize1;
	decalData__1.m_DecalBrightness = _DecalBrightness1;
	decalData__1.m_DecalGamma = _DecalGamma1;
	decalData__1.m_DecalDepth = _Decal1Depth;
	decalData__1.m_DecalHueAngleStrength = _Decal1HueAngleStrength;
	decalData__1.m_DecalChannelSeparationEnable = _Decal1ChannelSeparationEnable;
	decalData__1.m_DecalChannelSeparation = _Decal1ChannelSeparation;
	decalData__1.m_DecalChannelSeparationPremultiply = _Decal1ChannelSeparationPremultiply;
	decalData__1.m_DecalChannelSeparationHue = _Decal1ChannelSeparationHue;
	decalData__1.m_DecalChannelSeparationVertical = _Decal1ChannelSeparationVertical;
	decalData__1.m_DecalChannelSeparationAngleStrength = _Decal1ChannelSeparationAngleStrength;
	decalData__1.m_DecalOverrideAlphaMode = _Decal1OverrideAlphaMode;
	decalData__1.m_DecalMirroredUVMode = _DecalMirroredUVMode1;
	decalData__1.m_DecalSymmetryMode = _DecalSymmetryMode1;
	decalData__1.m_DecalAspectRatioMode = _Decal1AspectRatioMode;

	PoiDecal_Init(decalData__1, decalMask__1);
#if defined(PROP_DECALTEXTURE1)
	PoiDecal_ApplyAspectRatio(decalData__1, poiTexelSize(_DecalTexture1).zw);
#endif

	bool decalApply__1 = true;
#if defined(POI_PASS_OUTLINE)
	decalApply__1 = float(_Decal1OnOutlines) >= 0.5;
#endif
	bool videoEnabled__1 = !(_Decal1VideoEnabled < 0.5);
	bool localTextureAvailable__1 = false;
#if defined(PROP_DECALTEXTURE1)
	localTextureAvailable__1 = true;
#endif
	if (videoEnabled__1)
	{
		if (videoTexAvailable__1)
		decalData__1.m_DecalEmissionStrength += _Decal1VideoEmissionStrength;
		else if (_Decal1OnlyVideo >= 0.5)
		decalApply__1 = false;
	}
	PoiDecal_SampleDecalLayers(decalData__1, 1, videoEnabled__1, videoTexAvailable__1, _Decal1OnlyVideo >= 0.5, _Decal1UseDecalAlpha >= 0.5, _Decal1ChannelSeparationEnable >= 0.5, localTextureAvailable__1, videoAspectRatio__1, poiMods, poiLight, poiMesh, poiCam);

	if (decalApply__1)
	{
		PoiDecal_ApplyVideoEffects(decalData__1, _Decal1UseVideoEffects, poiCam, poiMesh);
		PoiDecal_Apply(decalData__1, poiFragData, poiMesh, poiCam, poiMods, poiLight);
	}
}
#endif

#if defined(POI_DECAL_2)
void applyDecals__2(in vec4 decalMask__2, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, in PoiLight poiLight)
{
	bool videoTexAvailable__2 = false;
	vec2 videoAspectRatio__2 = vec2(1.0);

	PoiDecal decalData__2;
	PoiInitStruct(PoiDecal, decalData__2)
	decalData__2.m_DecalFaceMask = _Decal2FaceMask;
	decalData__2.m_DecalMaskChannel = _Decal2MaskChannel;
	decalData__2.m_DecalGlobalMask = _Decal2GlobalMask;
	decalData__2.m_DecalGlobalMaskBlendType = _Decal2GlobalMaskBlendType;
	decalData__2.m_DecalApplyGlobalMaskIndex = _Decal2ApplyGlobalMaskIndex;
	decalData__2.m_DecalApplyGlobalMaskBlendType = _Decal2ApplyGlobalMaskBlendType;
	decalData__2.m_DecalTexture_ST = _DecalTexture2_ST;
	decalData__2.m_DecalTexturePan = _DecalTexture2Pan;
	decalData__2.m_DecalTextureUV = _DecalTexture2UV;
	decalData__2.m_DecalColor = _DecalColor2;
	decalData__2.m_DecalColorThemeIndex = _DecalColor2ThemeIndex;
	decalData__2.m_DecalTiled = _DecalTiled2;
	decalData__2.m_DecalBlendType = _DecalBlendType2;
	decalData__2.m_DecalRotation = _DecalRotation2;
	decalData__2.m_DecalScale = _DecalScale2;
	decalData__2.m_DecalSideOffset = _DecalSideOffset2;
	decalData__2.m_DecalPosition = _DecalPosition2;
	decalData__2.m_DecalRotationSpeed = _DecalRotationSpeed2;
	decalData__2.m_DecalEmissionStrength = _DecalEmissionStrength2;
	decalData__2.m_DecalBlendAlpha = _DecalBlendAlpha2;
	decalData__2.m_DecalAlphaIntensity = _DecalAlphaIntensity2;
	decalData__2.m_DecalOverrideAlpha = _DecalOverrideAlpha2;
	decalData__2.m_DecalHueShiftEnabled = _DecalHueShiftEnabled2;
	decalData__2.m_DecalHueShiftColorSpace = _DecalHueShiftColorSpace2;
	decalData__2.m_DecalHueShiftSelectOrShift = _DecalHueShiftSelectOrShift2;
	decalData__2.m_DecalHueShift = _DecalHueShift2;
	decalData__2.m_DecalHueShiftSpeed = _DecalHueShiftSpeed2;
	decalData__2.m_DecalColorAdjustEnabled = _DecalColorAdjustEnabled2;
	decalData__2.m_DecalSaturation = _DecalSaturation2;
	decalData__2.m_DecalChromatize = _DecalChromatize2;
	decalData__2.m_DecalBrightness = _DecalBrightness2;
	decalData__2.m_DecalGamma = _DecalGamma2;
	decalData__2.m_DecalDepth = _Decal2Depth;
	decalData__2.m_DecalHueAngleStrength = _Decal2HueAngleStrength;
	decalData__2.m_DecalChannelSeparationEnable = _Decal2ChannelSeparationEnable;
	decalData__2.m_DecalChannelSeparation = _Decal2ChannelSeparation;
	decalData__2.m_DecalChannelSeparationPremultiply = _Decal2ChannelSeparationPremultiply;
	decalData__2.m_DecalChannelSeparationHue = _Decal2ChannelSeparationHue;
	decalData__2.m_DecalChannelSeparationVertical = _Decal2ChannelSeparationVertical;
	decalData__2.m_DecalChannelSeparationAngleStrength = _Decal2ChannelSeparationAngleStrength;
	decalData__2.m_DecalOverrideAlphaMode = _Decal2OverrideAlphaMode;
	decalData__2.m_DecalMirroredUVMode = _DecalMirroredUVMode2;
	decalData__2.m_DecalSymmetryMode = _DecalSymmetryMode2;
	decalData__2.m_DecalAspectRatioMode = _Decal2AspectRatioMode;

	PoiDecal_Init(decalData__2, decalMask__2);
#if defined(PROP_DECALTEXTURE2)
	PoiDecal_ApplyAspectRatio(decalData__2, poiTexelSize(_DecalTexture2).zw);
#endif

	bool decalApply__2 = true;
#if defined(POI_PASS_OUTLINE)
	decalApply__2 = float(_Decal2OnOutlines) >= 0.5;
#endif
	bool videoEnabled__2 = !(_Decal2VideoEnabled < 0.5);
	bool localTextureAvailable__2 = false;
#if defined(PROP_DECALTEXTURE2)
	localTextureAvailable__2 = true;
#endif
	if (videoEnabled__2)
	{
		if (videoTexAvailable__2)
		decalData__2.m_DecalEmissionStrength += _Decal2VideoEmissionStrength;
		else if (_Decal2OnlyVideo >= 0.5)
		decalApply__2 = false;
	}
	PoiDecal_SampleDecalLayers(decalData__2, 2, videoEnabled__2, videoTexAvailable__2, _Decal2OnlyVideo >= 0.5, _Decal2UseDecalAlpha >= 0.5, _Decal2ChannelSeparationEnable >= 0.5, localTextureAvailable__2, videoAspectRatio__2, poiMods, poiLight, poiMesh, poiCam);

	if (decalApply__2)
	{
		PoiDecal_ApplyVideoEffects(decalData__2, _Decal2UseVideoEffects, poiCam, poiMesh);
		PoiDecal_Apply(decalData__2, poiFragData, poiMesh, poiCam, poiMods, poiLight);
	}
}
#endif

#if defined(POI_DECAL_3)
void applyDecals__3(in vec4 decalMask__3, inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiCam poiCam, inout PoiMods poiMods, in PoiLight poiLight)
{
	bool videoTexAvailable__3 = false;
	vec2 videoAspectRatio__3 = vec2(1.0);

	PoiDecal decalData__3;
	PoiInitStruct(PoiDecal, decalData__3)
	decalData__3.m_DecalFaceMask = _Decal3FaceMask;
	decalData__3.m_DecalMaskChannel = _Decal3MaskChannel;
	decalData__3.m_DecalGlobalMask = _Decal3GlobalMask;
	decalData__3.m_DecalGlobalMaskBlendType = _Decal3GlobalMaskBlendType;
	decalData__3.m_DecalApplyGlobalMaskIndex = _Decal3ApplyGlobalMaskIndex;
	decalData__3.m_DecalApplyGlobalMaskBlendType = _Decal3ApplyGlobalMaskBlendType;
	decalData__3.m_DecalTexture_ST = _DecalTexture3_ST;
	decalData__3.m_DecalTexturePan = _DecalTexture3Pan;
	decalData__3.m_DecalTextureUV = _DecalTexture3UV;
	decalData__3.m_DecalColor = _DecalColor3;
	decalData__3.m_DecalColorThemeIndex = _DecalColor3ThemeIndex;
	decalData__3.m_DecalTiled = _DecalTiled3;
	decalData__3.m_DecalBlendType = _DecalBlendType3;
	decalData__3.m_DecalRotation = _DecalRotation3;
	decalData__3.m_DecalScale = _DecalScale3;
	decalData__3.m_DecalSideOffset = _DecalSideOffset3;
	decalData__3.m_DecalPosition = _DecalPosition3;
	decalData__3.m_DecalRotationSpeed = _DecalRotationSpeed3;
	decalData__3.m_DecalEmissionStrength = _DecalEmissionStrength3;
	decalData__3.m_DecalBlendAlpha = _DecalBlendAlpha3;
	decalData__3.m_DecalAlphaIntensity = _DecalAlphaIntensity3;
	decalData__3.m_DecalOverrideAlpha = _DecalOverrideAlpha3;
	decalData__3.m_DecalHueShiftEnabled = _DecalHueShiftEnabled3;
	decalData__3.m_DecalHueShiftColorSpace = _DecalHueShiftColorSpace3;
	decalData__3.m_DecalHueShiftSelectOrShift = _DecalHueShiftSelectOrShift3;
	decalData__3.m_DecalHueShift = _DecalHueShift3;
	decalData__3.m_DecalHueShiftSpeed = _DecalHueShiftSpeed3;
	decalData__3.m_DecalColorAdjustEnabled = _DecalColorAdjustEnabled3;
	decalData__3.m_DecalSaturation = _DecalSaturation3;
	decalData__3.m_DecalChromatize = _DecalChromatize3;
	decalData__3.m_DecalBrightness = _DecalBrightness3;
	decalData__3.m_DecalGamma = _DecalGamma3;
	decalData__3.m_DecalDepth = _Decal3Depth;
	decalData__3.m_DecalHueAngleStrength = _Decal3HueAngleStrength;
	decalData__3.m_DecalChannelSeparationEnable = _Decal3ChannelSeparationEnable;
	decalData__3.m_DecalChannelSeparation = _Decal3ChannelSeparation;
	decalData__3.m_DecalChannelSeparationPremultiply = _Decal3ChannelSeparationPremultiply;
	decalData__3.m_DecalChannelSeparationHue = _Decal3ChannelSeparationHue;
	decalData__3.m_DecalChannelSeparationVertical = _Decal3ChannelSeparationVertical;
	decalData__3.m_DecalChannelSeparationAngleStrength = _Decal3ChannelSeparationAngleStrength;
	decalData__3.m_DecalOverrideAlphaMode = _Decal3OverrideAlphaMode;
	decalData__3.m_DecalMirroredUVMode = _DecalMirroredUVMode3;
	decalData__3.m_DecalSymmetryMode = _DecalSymmetryMode3;
	decalData__3.m_DecalAspectRatioMode = _Decal3AspectRatioMode;

	PoiDecal_Init(decalData__3, decalMask__3);
#if defined(PROP_DECALTEXTURE3)
	PoiDecal_ApplyAspectRatio(decalData__3, poiTexelSize(_DecalTexture3).zw);
#endif

	bool decalApply__3 = true;
#if defined(POI_PASS_OUTLINE)
	decalApply__3 = float(_Decal3OnOutlines) >= 0.5;
#endif
	bool videoEnabled__3 = !(_Decal3VideoEnabled < 0.5);
	bool localTextureAvailable__3 = false;
#if defined(PROP_DECALTEXTURE3)
	localTextureAvailable__3 = true;
#endif
	if (videoEnabled__3)
	{
		if (videoTexAvailable__3)
		decalData__3.m_DecalEmissionStrength += _Decal3VideoEmissionStrength;
		else if (_Decal3OnlyVideo >= 0.5)
		decalApply__3 = false;
	}
	PoiDecal_SampleDecalLayers(decalData__3, 3, videoEnabled__3, videoTexAvailable__3, _Decal3OnlyVideo >= 0.5, _Decal3UseDecalAlpha >= 0.5, _Decal3ChannelSeparationEnable >= 0.5, localTextureAvailable__3, videoAspectRatio__3, poiMods, poiLight, poiMesh, poiCam);

	if (decalApply__3)
	{
		PoiDecal_ApplyVideoEffects(decalData__3, _Decal3UseVideoEffects, poiCam, poiMesh);
		PoiDecal_Apply(decalData__3, poiFragData, poiMesh, poiCam, poiMods, poiLight);
	}
}
#endif
