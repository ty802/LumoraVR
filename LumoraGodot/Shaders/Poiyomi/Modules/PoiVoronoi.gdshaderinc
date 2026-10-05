// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Voronoi and truchet patterns.

#if !defined(POI_FX_SMOOTHSTEP)
#define POI_FX_SMOOTHSTEP
// smoothstep as the source evaluates it: reversed or equal edges stay defined.
float poiFxSmoothstep(float edge0, float edge1, float x)
{
	float t = clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
	return t * t * (3.0 - 2.0 * t);
}
#endif

#if defined(POI_VORONOI)
// 1/7
const float VoronoiK = 0.142857142857;
// 3/7
const float VoronoiKo = 0.428571428571;

// Permutation polynomial: (34x^2 + x) mod 289
vec3 Permutation(vec3 x)
{
	return mod((34.0 * x + 1.0) * x, 289.0);
}

vec3 inoise(vec3 Pin, float jitter, out vec3 randomPoint)
{
	// Scale adjustment
	vec3 P = Pin * 0.7;
	vec3 Pi = mod(floor(P), 289.0);
	vec3 Pf = fract(P);
	vec3 oi = vec3(-1.0, 0.0, 1.0);
	vec3 of = vec3(-0.5, 0.5, 1.5);
	vec3 px = Permutation(Pi.x + oi);
	vec3 py = Permutation(Pi.y + oi);
	vec3 pz = Permutation(Pi.z + oi);

	vec3 F = vec3(1e6);

	randomPoint = vec3(0.0);

	for (int i = 0; i < 3; i++)
	{
		for (int j = 0; j < 3; j++)
		{
			for (int k = 0; k < 3; k++)
			{
				// pij1, pij2, pij3
				vec3 p = Permutation(px[i] + py[j] + pz[k] + oi);

				vec3 ox = fract(p * VoronoiK) - VoronoiKo;
				vec3 oy = mod(floor(p * VoronoiK), 7.0) * VoronoiK - VoronoiKo;

				p = Permutation(p);
				vec3 oz = fract(p * VoronoiK) - VoronoiKo;

				vec3 dx = Pf.x - of[i] + jitter * ox;
				vec3 dy = Pf.y - of[j] + jitter * oy;
				vec3 dz = Pf.z - of[k] + jitter * oz;

				// dij1, dij2 and dij3, squared
				vec3 d = dx * dx + dy * dy + dz * dz;

				// Lowest and second lowest distances
				for (int n = 0; n < 3; n++)
				{
					bool newMin = d[n] < F.x;
					F.y = min(newMin ? F.x : d[n], F.y);
					randomPoint = newMin ? p : randomPoint;
					F.x = min(F.x, d[n]);
				}
			}
		}
	}

	return F;
}

float voronoi2D(in vec2 xIn, float scale, vec2 speed, out vec2 randomPoint)
{
	vec2 x = xIn * scale;
	x += speed * POI_TIME.x;
	vec2 n = floor(x);
	vec2 f = fract(x);

	randomPoint = vec2(0.0);

	// first pass: regular voronoi
	vec2 mg = vec2(0.0);
	vec2 mr = vec2(0.0);
	float md = 8.0;
	for (int j = -1; j <= 1; j++)
	{
		for (int i = -1; i <= 1; i++)
		{
			vec2 g = vec2(float(i), float(j));
			vec2 o = poiRand2(n + g);
			vec2 currentPoint = o;

			vec2 r = g + o - f;
			float d = dot(r, r);

			if (d < md)
			{
				md = d;
				mr = r;
				mg = g;
				randomPoint = currentPoint;
			}
		}
	}

	// second pass: distance to borders
	md = 8.0;
	for (int row = -2; row <= 2; row++)
	{
		for (int q = -2; q <= 2; q++)
		{
			vec2 g = mg + vec2(float(q), float(row));
			vec2 o = poiRand2(n + g);

			vec2 r = g + o - f;

			if (dot(mr - r, mr - r) > 0.00001)
			{
				md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
			}
		}
	}
	return md;
}

float voronoi3D(in vec3 xIn, float scale, vec3 speed, out vec3 randomPoint)
{
	vec3 x = xIn * scale;
	x += speed * POI_TIME.x;
	vec3 n = floor(x);
	vec3 f = fract(x);

	randomPoint = vec3(0.0);

	// first pass: regular voronoi
	vec3 mg = vec3(0.0);
	vec3 mr = vec3(0.0);
	float md = 8.0;
	for (int j = -1; j <= 1; j++)
	{
		for (int i = -1; i <= 1; i++)
		{
			for (int h = -1; h <= 1; h++)
			{
				vec3 g = vec3(float(h), float(i), float(j));
				vec3 o = poiRand3(n + g);
				vec3 currentPoint = o;

				vec3 r = g + o - f;
				float d = dot(r, r);

				if (d < md)
				{
					md = d;
					mr = r;
					mg = g;
					randomPoint = currentPoint;
				}
			}
		}
	}

	// second pass: distance to borders
	md = 8.0;
	for (int row = -2; row <= 2; row++)
	{
		for (int q = -2; q <= 2; q++)
		{
			for (int p = -2; p <= 2; p++)
			{
				vec3 g = mg + vec3(float(p), float(q), float(row));
				vec3 o = poiRand3(n + g);

				vec3 r = g + o - f;

				if (dot(mr - r, mr - r) > 0.00001)
				{
					md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
				}
			}
		}
	}
	return md;
}

// fracal sum, range -1.0 - 1.0. One octave, as in the source.
float VoronoiNoise_Octaves(vec3 p, float scale, vec3 speed, int octaveNumber, float octaveScale, float octaveAttenuation, float jitter, float time, out vec3 randomPoint)
{
	float freq = scale;
	float weight = 1.0;
	float sum = 0.0;
	vec3 F = inoise(p * freq + time * speed, jitter, randomPoint) * weight;

	sum += sqrt(F.x);

	return sum;
}

float VoronoiNoiseDiff_Octaves(vec3 p, float scale, vec3 speed, int octaveNumber, float octaveScale, float octaveAttenuation, float jitter, float time, out vec3 randomPoint)
{
	float freq = scale;
	float weight = 1.0;
	float sum = 0.0;
	vec3 F = inoise(p * freq + time * speed, jitter, randomPoint) * weight;

	sum += sqrt(F.y) - sqrt(F.x);

	return sum;
}

void ApplyVoronoi(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	float voronoiOctaveNumber = 1.0;
	float voronoiOctaveScale = 1.0;
	float voronoiOctaveAttenuation = 1.0;
	vec3 randomPoint = vec3(0.0);

	float voronoi = 0.0;

	vec3 position = vec3(0.0);

	if (_VoronoiSpace == 0)
	{
		position = poiMesh.localPos;
	}
	else if (_VoronoiSpace == 1)
	{
		position = poiMesh.worldPos;
	}
	else if (_VoronoiSpace == 2)
	{
		vec2 voronoiUV = poiMesh.uv[int(_VoronoiUV)];
		position = vec3(voronoiUV.xy, 0.0);
	}
#if defined(PROP_VORONOIMASK)
	float mask = poiTexPan(_VoronoiMask, poiUV(poiMesh.uv[int(_VoronoiMaskUV)], _VoronoiMask_ST), _VoronoiMaskPan)[_VoronoiMaskChannel];
#else
	float mask = 1.0;
#endif

	if (_VoronoiGlobalMask > 0.0)
	{
		mask = maskBlend(mask, poiMods.globalMask[int(_VoronoiGlobalMask) - 1], _VoronoiGlobalMaskBlendType);
	}

	if (mask <= 0.001)
	{
		return;
	}

#if defined(PROP_VORONOINOISE)
	float edgeNoise = poiTexPan(_VoronoiNoise, poiUV(poiMesh.uv[int(_VoronoiNoiseUV)], _VoronoiNoise_ST), _VoronoiNoisePan)[_VoronoiNoiseChannel];
#else
	float edgeNoise = 0.0;
#endif
	edgeNoise *= _VoronoiNoiseIntensity;

	vec3 voronoiSpeed = _VoronoiSpeed * 10.0;

	if (_VoronoiType == 0)
	{
		vec2 randomPoint2 = vec2(0.0);
		voronoi = voronoi2D(position.xy, _VoronoiScale, voronoiSpeed.xy, randomPoint2);
		randomPoint.xy = randomPoint2;
		// Range adjustment
		voronoi *= 1.55;
	}
	else if (_VoronoiType == 1)
	{
		voronoi = VoronoiNoiseDiff_Octaves(position, _VoronoiScale, voronoiSpeed, int(voronoiOctaveNumber), voronoiOctaveScale, voronoiOctaveAttenuation, 1.0, POI_TIME.x, randomPoint);
	}
	else if (_VoronoiType == 2)
	{
		voronoi = voronoi3D(position, _VoronoiScale, voronoiSpeed, randomPoint);
		// Range adjustment
		voronoi *= 1.8;
	}

	vec4 outerColor = _VoronoiOuterColor;
	outerColor.rgb = poiThemeColor(poiMods, outerColor.rgb, _VoronoiOuterColorThemeIndex);
	vec4 innerColor = _VoronoiInnerColor;
	innerColor.rgb = poiThemeColor(poiMods, innerColor.rgb, _VoronoiInnerColorThemeIndex);

	if (_VoronoiEnableRandomCellColor >= 0.5)
	{
		vec3 rando = poiRand3(randomPoint);
		float hue = rando.x;
		float saturation = mix(_VoronoiRandomMinMaxSaturation.x, _VoronoiRandomMinMaxSaturation.y, rando.y);
		float value = mix(_VoronoiRandomMinMaxBrightness.x, _VoronoiRandomMinMaxBrightness.y, rando.z);
		vec3 hsv = vec3(hue, saturation, value);
		innerColor.rgb = HSVtoRGB(hsv);
	}
	voronoi = pow(voronoi, _VoronoiPower);
	vec2 voronoiGradient = _VoronoiGradient.xy + edgeNoise;
	float ramp = poiFxSmoothstep(voronoiGradient.x, voronoiGradient.y, voronoi);

	if (_VoronoiBlend == 0)
	{
		vec4 voronoiColor = mix(outerColor, innerColor, ramp);
		poiFragData.baseColor = mix(poiFragData.baseColor, voronoiColor.rgb, min(mask * voronoiColor.a, 0.99999));
		if (_VoronoiAffectsMaterialAlpha >= 1)
		{
			poiFragData.alpha = mix(poiFragData.alpha, voronoiColor.a, min(mask, 0.99999));
		}
	}
	float outerEmissionStrength = _VoronoiOuterEmissionStrength;
	float innerEmissionStrength = _VoronoiInnerEmissionStrength;
	vec4 voronoiEmissionColor = mix(outerColor, innerColor, ramp);
	voronoiEmissionColor.rgb *= mix(outerEmissionStrength, innerEmissionStrength, ramp);
	poiFragData.emission += voronoiEmissionColor.rgb * mask * voronoiEmissionColor.a;
}
#endif

#if defined(POI_TRUCHET)
float Hash21(vec2 valueIn)
{
	vec2 value = fract(valueIn * vec2(923.34, 567.345));
	value += dot(value, value + 34.23);
	return fract(value.x * value.y);
}

void applyTruchet(inout PoiFragData poiFragData, in PoiMesh poiMesh, in PoiMods poiMods)
{
	vec2 uv = poiMesh.uv[int(_TruchetUV)] * _TruchetDensity + _TruchetPanOffset.xy * POI_TIME.x + _TruchetPanOffset.zw;
	vec2 gridUV = fract(uv) - 0.5;
	vec2 id = floor(uv);
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);

	// Random # 0-1
	float randomID = Hash21(id);

	if (randomID < 0.5)
	{
		gridUV.x *= -1.0;
	}
	float mask = 0.0;
	float pathDistance = 0.0;
	float side = sign(gridUV.x + gridUV.y + 0.001);
	float checker = mod(id.x + id.y, 2.0) * 2.0 - 1.0;
	vec2 pathUV = vec2(0.0, 0.0);

	pathUV = gridUV - side * 0.5;
	pathDistance = length(pathUV);

	// Epsilon prevents division by zero
	float fwidthDistance = fwidth(abs(pathDistance - 0.5)) + 0.0001;
	mask = clamp((_TruchetThiccness - abs(pathDistance - 0.5)) / fwidthDistance, 0.0, 1.0);
	mask *= poiFxSmoothstep(0.0, 1.0 - _TruchetEdgeSharpness, (_TruchetThiccness - abs(pathDistance - 0.5)));

#if defined(PROP_TRUCHETMASK)
	float maskTex = poiTex(_TruchetMask, poiUV(poiMesh.uv[int(_TruchetMaskUV)], _TruchetMask_ST) + POI_TIME.x * _TruchetMaskPan.xy)[int(_TruchetMaskChannel)];
#else
	float maskTex = 1.0;
#endif

	maskTex = clamp(maskTex * _TruchetMaskIntensity, 0.0, 1.0);

	if (_TruchetMaskInvert >= 0.5)
	{
		maskTex = 1.0 - maskTex;
	}

	if (_TruchetMaskGlobalMask > 0.0)
	{
		maskTex = maskBlend(maskTex, poiMods.globalMask[int(_TruchetMaskGlobalMask) - 1], _TruchetMaskGlobalMaskBlendType);
	}
	mask *= maskTex;

	// -pi to pi
	float angle = atan(pathUV.x, pathUV.y);

	pathUV.x = checker * angle / 1.57;
	pathUV.y = (pathDistance - (0.5 - _TruchetThiccness)) / (2.0 * _TruchetThiccness);
	if ((randomID < 0.5) != (checker > 0.0))
	{
		pathUV.y = 1.0 - pathUV.y;
	}
	float emission = _TruchetEmissionStrength;
	vec2 uvOffset = vec2(0.0, 0.0);

#if defined(PROP_TRUCHETTEX)
	vec4 TruchetTex = poiTexPanGrad(_TruchetTex, poiUV(pathUV, _TruchetTex_ST) + uvOffset, _TruchetTexPan.xy, dx, dy);
#else
	vec4 TruchetTex = vec4(1.0);
#endif
	vec3 truchetColor = poiThemeColor(poiMods, _TruchetColor.rgb, _TruchetColorThemeIndex);
	poiFragData.emission += mix(vec3(0.0), TruchetTex.rgb * truchetColor, min(mask, TruchetTex.a) * emission * _TruchetAlpha);
	poiFragData.baseColor = mix(poiFragData.baseColor, customBlend(poiFragData.baseColor, TruchetTex.rgb * truchetColor, _TruchetBlendType), min(mask, TruchetTex.a) * _TruchetAlpha);
	poiFragData.alpha *= mix(1.0, min(mask, TruchetTex.a) * _TruchetAlpha, _TruchetOverrideAlpha);
}
#endif
