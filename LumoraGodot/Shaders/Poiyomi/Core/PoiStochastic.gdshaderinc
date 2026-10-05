// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Stochastic texture sampling (POI_STOCHASTIC_DELIOT_HEITZ or POI_STOCHASTIC_HEXTILE) behind
// poiTexStochastic / poiTexPanStochastic / poiTexPanGradStochastic. useStochastic is the texture's
// *Stochastic toggle; with no mode defined they are plain poiTex fetches. The fetches are macros over
// sampler-free math: a function may only take textures that share one filter and repeat setting, and
// these read every kind. Matrices hold the source's rows as columns, so M[i] is row i.

// Classic Magic Numbers fracsin
#if !defined(POI_STOCHASTIC_NONE)
vec2 StochasticHash2D2D(vec2 s)
{
	return fract(sin(vec2(dot(s, vec2(127.1, 311.7)), dot(s, vec2(269.5, 183.3)))) * 43758.5453);
}
#endif

// Deliot, Heitz 2019 - Fast, but non-histogram-preserving (ends up looking a bit blurry and lower contrast)
// https://eheitzresearch.wordpress.com/738-2/
#if defined(POI_STOCHASTIC_DELIOT_HEITZ)
// UV Offsets and blend weights
// UVBW[0...2].xy = UV Offsets
// UVBW[0...2].z = Blend Weights
mat3 DeliotHeitzStochasticUVBW(vec2 uv)
{
	// UV transformed into triangular grid space with UV scaled by approximation of 2*sqrt(3)
	mat2 stochasticSkewedGrid = mat2(vec2(1.0, 0.0), vec2(-0.57735027, 1.15470054));
	vec2 skewUV = stochasticSkewedGrid * (uv * 3.4641 * _StochasticDeliotHeitzDensity);

	// Vertex IDs and barycentric coords
	vec2 vxID = floor(skewUV);
	vec3 bary = vec3(fract(skewUV), 0.0);
	bary.z = 1.0 - bary.x - bary.y;

	mat3 pos = mat3(
	vec3(vxID, bary.z),
	vec3(vxID + vec2(0.0, 1.0), bary.y),
	vec3(vxID + vec2(1.0, 0.0), bary.x)
	);

	mat3 neg = mat3(
	vec3(vxID + vec2(1.0, 1.0), -bary.z),
	vec3(vxID + vec2(1.0, 0.0), 1.0 - bary.y),
	vec3(vxID + vec2(0.0, 1.0), 1.0 - bary.x)
	);

	return (bary.z > 0.0) ? pos : neg;
}

// Offset UV and blend weight of sample k (0..2).
vec2 DeliotHeitzSampleUV(vec2 uv, int k)
{
	mat3 UVBW = DeliotHeitzStochasticUVBW(uv);
	return uv + StochasticHash2D2D(UVBW[k].xy);
}

float DeliotHeitzSampleWeight(vec2 uv, int k)
{
	mat3 UVBW = DeliotHeitzStochasticUVBW(uv);
	return UVBW[k].z;
}

//blend samples with calculated weights
#define DeliotHeitzSampleTextureGrad(tex, uv, dx, dy) (poiTexGrad(tex, DeliotHeitzSampleUV(uv, 0), dx, dy) * DeliotHeitzSampleWeight(uv, 0) + \
	poiTexGrad(tex, DeliotHeitzSampleUV(uv, 1), dx, dy) * DeliotHeitzSampleWeight(uv, 1) + \
	poiTexGrad(tex, DeliotHeitzSampleUV(uv, 2), dx, dy) * DeliotHeitzSampleWeight(uv, 2))

#define DeliotHeitzSampleTexture(tex, uv) DeliotHeitzSampleTextureGrad(tex, uv, dFdx(uv), dFdy(uv))
#endif

#if defined(POI_STOCHASTIC_HEXTILE)
// HexTiling: Slower, but histogram-preserving
// SPDX-License-Idenfitier: MIT
// Copyright (c) 2022 mmikk
// https://github.com/mmikk/hextile-demo
vec2 HextileMakeCenUV(vec2 vertex)
{
	// 0.288675 ~= 1/(2*sqrt(3))
	mat2 stochasticInverseSkewedGrid = mat2(vec2(1.0, 0.0), vec2(0.5, 1.0 / 1.15470054));
	return (stochasticInverseSkewedGrid * vertex) * 0.288675;
}

// Rows (cs, -si) and (si, cs); applied to row vectors as R * v.
mat2 HextileLoadRot2x2(vec2 idx, float rotStrength)
{
	float angle = abs(idx.x * idx.y) + abs(idx.x + idx.y) + PI;

	// remap to +/-pi
	angle = glsl_mod(angle, 2.0 * PI);
	if (angle < 0.0) angle += 2.0 * PI;
	if (angle > PI) angle -= 2.0 * PI;

	angle *= rotStrength;

	float si = sin(angle);
	float cs = cos(angle);
	return mat2(vec2(cs, -si), vec2(si, cs));
}

// UV Offsets and base blend weights
// UVBWR[0...2].xy = UV Offsets
// UVBWR[0...2].zw = rotation costh/sinth -> reconstruct rotation matrix with rows (UVBWR[n].z, -UVBWR[n].w), (UVBWR[n].w, UVBWR[n].z)
// UVBWR[3].xyz = Blend Weights (w unused) - needs luminance weighting
mat4 HextileUVBWR(vec2 uv)
{
	// Create Triangle Grid
	// Skew input space into simplex triangle grid (3.4641 ~= 2*sqrt(3))
	mat2 stochasticSkewedGrid = mat2(vec2(1.0, 0.0), vec2(-0.57735027, 1.15470054));
	vec2 skewedCoord = stochasticSkewedGrid * (uv * 3.4641 * _StochasticHexGridDensity);

	vec2 baseId = vec2(floor(skewedCoord));
	vec3 temp = vec3(fract(skewedCoord), 0.0);
	temp.z = 1.0 - temp.x - temp.y;

	float s = step(0.0, -temp.z);
	float s2 = 2.0 * s - 1.0;

	vec3 weights = vec3(-temp.z * s2, s - temp.y * s2, s - temp.x * s2);

	vec2 vertex0 = baseId + vec2(s, s);
	vec2 vertex1 = baseId + vec2(s, 1.0 - s);
	vec2 vertex2 = baseId + vec2(1.0 - s, s);

	vec2 cen0 = HextileMakeCenUV(vertex0);
	vec2 cen1 = HextileMakeCenUV(vertex1);
	vec2 cen2 = HextileMakeCenUV(vertex2);
	mat2 rot0 = mat2(1.0);
	mat2 rot1 = mat2(1.0);
	mat2 rot2 = mat2(1.0);

	if (_StochasticHexRotationStrength > 0.0)
	{
		rot0 = HextileLoadRot2x2(vertex0, _StochasticHexRotationStrength);
		rot1 = HextileLoadRot2x2(vertex1, _StochasticHexRotationStrength);
		rot2 = HextileLoadRot2x2(vertex2, _StochasticHexRotationStrength);
	}

	return mat4(
	vec4(rot0 * (uv - cen0) + cen0 + StochasticHash2D2D(vertex0), rot0[0].x, -rot0[0].y),
	vec4(rot1 * (uv - cen1) + cen1 + StochasticHash2D2D(vertex1), rot1[0].x, -rot1[0].y),
	vec4(rot2 * (uv - cen2) + cen2 + StochasticHash2D2D(vertex2), rot2[0].x, -rot2[0].y),
	vec4(weights, 0.0)
	);
}

// Offset UV of sample k (0..2).
vec2 HextileSampleUV(vec2 uv, int k)
{
	mat4 UVBWR = HextileUVBWR(uv);
	return UVBWR[k].xy;
}

// 2D Rotation Matrices for dUVdx/dy
mat2 HextileSampleRot(vec2 uv, int k)
{
	mat2 rot = mat2(1.0);
	if (_StochasticHexRotationStrength > 0.0)
	{
		mat4 UVBWR = HextileUVBWR(uv);
		rot = mat2(vec2(UVBWR[k].z, -UVBWR[k].w), vec2(UVBWR[k].w, UVBWR[k].z));
	}
	return rot;
}

vec4 HextileBlendSamples(vec2 uv, vec4 c0, vec4 c1, vec4 c2)
{
	mat4 UVBWR = HextileUVBWR(uv);

	// Weights
	vec3 W = UVBWR[3].xyz;

	// Blend samples using luminance
	// This is technically incorrect for normal maps, but produces very similar
	// results to blending using normal map gradients (steepness)
	const vec3 Lw = vec3(0.299, 0.587, 0.114);
	vec3 Dw = vec3(dot(c0.xyz, Lw), dot(c1.xyz, Lw), dot(c2.xyz, Lw));

	Dw = mix(vec3(1.0), Dw, _StochasticHexFallOffContrast);
	W = Dw * pow(W, vec3(_StochasticHexFallOffPower));
	// In the original hextiling there's a Gain3 step here, but it seems to slow things down
	// and cause the UVs to break, so I've omitted it. Looks fine without

	W /= (W.x + W.y + W.z);
	return W.x * c0 + W.y * c1 + W.z * c2;
}

#define HextileSampleTextureGrad(tex, uv, isNormalMap, dUVdx, dUVdy) HextileBlendSamples(uv, \
	poiTexGrad(tex, HextileSampleUV(uv, 0), HextileSampleRot(uv, 0) * (dUVdx), HextileSampleRot(uv, 0) * (dUVdy)), \
	poiTexGrad(tex, HextileSampleUV(uv, 1), HextileSampleRot(uv, 1) * (dUVdx), HextileSampleRot(uv, 1) * (dUVdy)), \
	poiTexGrad(tex, HextileSampleUV(uv, 2), HextileSampleRot(uv, 2) * (dUVdx), HextileSampleRot(uv, 2) * (dUVdy)))

#define HextileSampleTexture(tex, uv, isNormalMap) HextileSampleTextureGrad(tex, uv, isNormalMap, dFdx(uv), dFdy(uv))
#endif

#if defined(POI_STOCHASTIC_DELIOT_HEITZ)
#define poiTexStochastic(tex, uv, useStochastic) ((useStochastic) != 0.0 ? DeliotHeitzSampleTexture(tex, uv) : poiTex(tex, uv))
#define poiTexPanStochastic(tex, uv, pan, useStochastic) ((useStochastic) != 0.0 ? DeliotHeitzSampleTexture(tex, POI_PAN_UV(uv, pan)) : poiTexPan(tex, uv, pan))
#define poiTexPanGradStochastic(tex, uv, pan, dx, dy, useStochastic) ((useStochastic) != 0.0 ? DeliotHeitzSampleTextureGrad(tex, POI_PAN_UV(uv, pan), dx, dy) : poiTexPanGrad(tex, uv, pan, dx, dy))
#elif defined(POI_STOCHASTIC_HEXTILE)
#define poiTexStochastic(tex, uv, useStochastic) ((useStochastic) != 0.0 ? HextileSampleTexture(tex, uv, false) : poiTex(tex, uv))
#define poiTexPanStochastic(tex, uv, pan, useStochastic) ((useStochastic) != 0.0 ? HextileSampleTexture(tex, POI_PAN_UV(uv, pan), false) : poiTexPan(tex, uv, pan))
#define poiTexPanGradStochastic(tex, uv, pan, dx, dy, useStochastic) ((useStochastic) != 0.0 ? HextileSampleTextureGrad(tex, POI_PAN_UV(uv, pan), false, dx, dy) : poiTexPanGrad(tex, uv, pan, dx, dy))
#else
#define poiTexStochastic(tex, uv, useStochastic) (poiTex(tex, uv))
#define poiTexPanStochastic(tex, uv, pan, useStochastic) (poiTexPan(tex, uv, pan))
#define poiTexPanGradStochastic(tex, uv, pan, dx, dy, useStochastic) (poiTexPanGrad(tex, uv, pan, dx, dy))
#endif

// The source's macros; texSampler has no counterpart (each texture carries its sampler).
#define POI2D_SAMPLER_STOCHASTIC(tex, texSampler, uv, useStochastic) poiTexStochastic(tex, uv, useStochastic)
#define POI2D_SAMPLER_PAN_STOCHASTIC(tex, texSampler, uv, pan, useStochastic) poiTexPanStochastic(tex, uv, pan, useStochastic)
#define POI2D_SAMPLER_PANGRAD_STOCHASTIC(tex, texSampler, uv, pan, dx, dy, useStochastic) poiTexPanGradStochastic(tex, uv, pan, dx, dy, useStochastic)
