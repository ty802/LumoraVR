// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Poiyomi's vertex program. vert() is the source's vert with its early outs (a culled vertex leaves as
// NaN so its triangles drop, as the source's POI_NAN return); vertex() feeds it the engine's attributes and
// writes the result back in model space. The engine runs vertex() for shadow maps and the depth prepass too:
// IN_SHADOW_PASS stands in for the source's POI_PASS_SHADOW.

// Uzumore view clip prevention (sigmal00)
float sdPlane(vec3 p, vec3 n, float h)
{
	return dot(p, normalize(n)) + h;
}

vec3 calcIntrudePos(in PoiMesh poiMesh, in PoiCam poiCam, vec3 pos, vec3 normalOS, vec2 uv, vec4 vertexColor)
{
	vec3 wnormal = PoiObjectToWorldNormal(poiMesh, normalOS);
	vec3 wpos = (poiMesh.modelMatrix * vec4(pos, 1.0)).xyz;

	vec3 axisX = poiMesh.modelMatrix[0].xyz;
	vec3 axisY = poiMesh.modelMatrix[1].xyz;
	vec3 axisZ = poiMesh.modelMatrix[2].xyz;
	float objectScale = sqrt((dot(axisX, axisX) + dot(axisY, axisY) + dot(axisZ, axisZ)) / 3.0) * _UzumoreObjectScale;

	// Keep the viewing camera's direction when the current view belongs to a shadow map (vertex() puts
	// the viewing camera into poiCam there).
	vec3 camDir = poiCam.forwardDir;
	vec3 camPos = getCameraPosition(poiCam, false);
	float near = poiCam.nearPlane;

#if defined(PROP_UZUMOREMASK)
	vec4 uzumoreMaskTex = POI_VERTEX_TEX_LOD(_UzumoreMask, uv);
	float uzumoreMask = uzumoreMaskTex[int(_UzumoreMaskChannel)];
#else
	float uzumoreMask = 1.0;
#endif

	float maxAmount = objectScale > 0.0 ? uzumoreMask * (_UzumoreAmount * objectScale + near * (1.0 - objectScale)) : 0.0;
	float maxBias = _UzumoreBias * objectScale;
	float d = sdPlane(wpos - camPos, -camDir, (near + maxBias));
	float intrudeAmount = clamp(d, 0.0, maxAmount);

	if (intrudeAmount > 0.0 && dot(camDir, wnormal) < -0.2)
	{
		float biasRate = min(1.0, intrudeAmount / max(maxAmount, 0.00001));
		float bias = maxBias * biasRate;

		vec3 extrude = (intrudeAmount - bias) * camDir;
		return (poiMesh.invModelMatrix * vec4(wpos + extrude, 1.0)).xyz;
	}

	return pos;
}

#if defined(POI_UDIMDISCARD)
// shouldPerformUDIMDiscard lives in Modules/PoiMain, which is included after vertex(); the same test.
float shouldPerformUDIMDiscardVertex(vec2 udim, vec4 UDIMDiscardRows[4])
{
	float shouldDiscard = 0.0;
	vec4 xMask = vec4(float(udim.x >= 0.0 && udim.x < 1.0),
	float(udim.x >= 1.0 && udim.x < 2.0),
	float(udim.x >= 2.0 && udim.x < 3.0),
	float(udim.x >= 3.0 && udim.x < 4.0));

	shouldDiscard += float(udim.y >= 0.0 && udim.y < 1.0) * dot(UDIMDiscardRows[0], xMask);
	shouldDiscard += float(udim.y >= 1.0 && udim.y < 2.0) * dot(UDIMDiscardRows[1], xMask);
	shouldDiscard += float(udim.y >= 2.0 && udim.y < 3.0) * dot(UDIMDiscardRows[2], xMask);
	shouldDiscard += float(udim.y >= 3.0 && udim.y < 4.0) * dot(UDIMDiscardRows[3], xMask);

	shouldDiscard *= float(udim.y >= 0.0 || udim.y < 4.0 || udim.x >= 0.0 || udim.x < 4.0); // never discard outside 4x4 grid in pos coords

	// Use a threshold so that there's some room for animations to be close to 0, but not exactly 0
	const float threshold = 0.001;
	return threshold - shouldDiscard;
}
#endif

VertexOut VertexOut_nan()
{
	VertexOut r;
	vec4 n = vec4(POI_NAN);
	r.pos = n;
	r.uv[0] = n;
	r.uv[1] = n;
	r.normal = n;
	r.tangent = n;
	r.worldPos = n;
	r.localPos = n;
	r.vertexColor = n;
	r.worldDir = POI_NAN;
	return r;
}

VertexOut vert(inout appdata v, in PoiMesh poiMesh, in PoiCam poiCam, bool inShadowPass)
{
	VertexOut o;
	PoiInitStruct(VertexOut, o)

#if defined(POI_PASS_OUTLINE)
	// SRPDefaultUnlit outline pass: discard when Render Feature mode is active
	if (_EnableOutlinesRenderFeature > 0.5)
	{
		return VertexOut_nan();
	}
	// The source's outline pass has no shadow caster; the engine draws every material of the surface into
	// shadow maps.
	if (inShadowPass)
	{
		return VertexOut_nan();
	}
#endif

	o.uv[0] = vec4(v.uv0.xy, v.uv1.xy);
	o.uv[1] = vec4(v.uv2.xy, v.uv3.xy);

	o.normal.w = length(mat3(poiMesh.modelMatrix) * v.normal);

	if (!inShadowPass)
	{
		if (_RenderingAOBlockerEnabled >= 0.5)
		{
			vec2 blockerUV = vec2(0.0);
			blockerUV += (v.uv0.xy * float(_RenderingAOBlockerUVChannel == 0.0));
			blockerUV += (v.uv1.xy * float(_RenderingAOBlockerUVChannel == 1.0));
			blockerUV += (v.uv2.xy * float(_RenderingAOBlockerUVChannel == 2.0));
			blockerUV += (v.uv3.xy * float(_RenderingAOBlockerUVChannel == 3.0));
			if (blockerUV.x < 0.0 && blockerUV.x > -1.0 && blockerUV.y < 1.0 && blockerUV.y > 0.0)
			{
				return VertexOut_nan();
			}
		}
	}

#if defined(POI_UDIMDISCARD)
	if (_UDIMDiscardMode == 0.0) // Discard Vertices instead of just pixels
	{
		// Branchless (inspired by s-ilent)
		vec2 udim = vec2(0.0);
		// Select UV
		udim += (v.uv0.xy * float(_UDIMDiscardUV == 0.0));
		udim += (v.uv1.xy * float(_UDIMDiscardUV == 1.0));
		udim += (v.uv2.xy * float(_UDIMDiscardUV == 2.0));
		udim += (v.uv3.xy * float(_UDIMDiscardUV == 3.0));

		vec4 UDIMDiscardRows[4];
		UDIMDiscardRows[0] = vec4(_UDIMDiscardRow0_0, _UDIMDiscardRow0_1, _UDIMDiscardRow0_2, _UDIMDiscardRow0_3);
		UDIMDiscardRows[1] = vec4(_UDIMDiscardRow1_0, _UDIMDiscardRow1_1, _UDIMDiscardRow1_2, _UDIMDiscardRow1_3);
		UDIMDiscardRows[2] = vec4(_UDIMDiscardRow2_0, _UDIMDiscardRow2_1, _UDIMDiscardRow2_2, _UDIMDiscardRow2_3);
		UDIMDiscardRows[3] = vec4(_UDIMDiscardRow3_0, _UDIMDiscardRow3_1, _UDIMDiscardRow3_2, _UDIMDiscardRow3_3);

		float shouldDiscard = shouldPerformUDIMDiscardVertex(udim, UDIMDiscardRows);

		if (shouldDiscard < 0.0) // Early Return skips rest of vertex shader
		{
			return VertexOut_nan();
		}
	}
#endif

#if defined(POI_VERTEX_MANIPULATION)
#if defined(PROP_VERTEXBASICSMASK)
	vec4 vertexMaskTex = POI_VERTEX_TEX_LOD(_VertexBasicsMask, poiUV(vertexUV(v, VertexBasicsMaskUV), _VertexBasicsMask_ST) + _VertexBasicsMaskUVPan.xy * POI_TIME.x);
#else
	vec4 vertexMaskTex = vec4(1.0);
#endif

	float vertexEffectsMask[8] = float[8](
		vertexMaskTex.r,
		vertexMaskTex.g,
		vertexMaskTex.b,
		vertexMaskTex.a,
		v.color.r,
		v.color.g,
		v.color.b,
		v.color.a
	);

	// The channel selectors are declared with their mask textures only; without the texture the
	// property default (R) applies.
#if defined(PROP_VERTEXBASICSMASK)
	float basicsMask = vertexEffectsMask[int(_VertexBasicsMaskChannel)];
#else
	float basicsMask = vertexEffectsMask[0];
#endif

	vec4 rotation = vec4(
		mix(vec3(0.0), _VertexManipulationLocalRotation.xyz, basicsMask) +
		vec3(180.0, 0.0, 0.0) +
		mix(vec3(0.0), _VertexManipulationLocalRotationSpeed.xyz, basicsMask) * POI_TIME.x,
		_VertexManipulationLocalRotation.w
	);

	vec4 localTranslation = mix(vec4(0.0), _VertexManipulationLocalTranslation, basicsMask);

	vec4 manualScale = mix(vec4(1.0), _VertexManipulationLocalScale, basicsMask);
	vec4 localScale = manualScale;

	v.normal = rotate_with_quaternion(v.normal, rotation.xyz);
	v.tangent.xyz = rotate_with_quaternion(v.tangent.xyz, rotation.xyz);
	v.vertex = transform(v.vertex, localTranslation, rotation, localScale);
	o.normal.xyz = PoiObjectToWorldNormal(poiMesh, v.normal);

	vec3 heightOffset = vec3(0.0);
	if (_VertexManipulationHeight != 0.0)
	{
#if defined(PROP_VERTEXMANIPULATIONHEIGHTMASK)
		vec4 heightMaskTex = POI_VERTEX_TEX_LOD(_VertexManipulationHeightMask, poiUV(vertexUV(v, _VertexManipulationHeightMaskUV), _VertexManipulationHeightMask_ST) + _VertexManipulationHeightMaskPan * POI_TIME.x);
		heightOffset = (heightMaskTex[int(_VertexManipulationHeightMapChannel)] - _VertexManipulationHeightBias) * _VertexManipulationHeight * o.normal.xyz;
		heightOffset *= vertexEffectsMask[int(_VertexManipulationHeightMaskChannel)] * o.normal.w;
#else
		heightOffset = (1.0 - _VertexManipulationHeightBias) * _VertexManipulationHeight * o.normal.xyz;
		heightOffset *= vertexEffectsMask[0] * o.normal.w;
#endif
	}
	// Fix z fighting issue with projectors maybe probably I hope
	heightOffset -= 1e-05 * o.normal.xyz;

	if (_VertexBarrelMode >= 0.5)
	{
		float barrelMask = vertexEffectsMask[int(_VertexBarrelMaskChannel)];
		v.vertex.xz = mix(v.vertex.xz, normalize(v.vertex.xz) * _VertexBarrelWidth + v.vertex.xz * _VertexBarrelHeight, _VertexBarrelAlpha * barrelMask);
	}

	if (_VertexSphereMode >= 0.5)
	{
		float sphereMask = vertexEffectsMask[int(_VertexSphereMaskChannel)];
		v.vertex.xyz = mix(v.vertex.xyz, normalize(v.vertex.xyz + _VertexSphereCenter.xyz) * _VertexSphereRadius + v.vertex.xyz * _VertexSphereHeight, _VertexSphereAlpha * sphereMask);
	}

	if (_VertexTornadoMode >= 0.5)
	{
		float tornadoMask = vertexEffectsMask[int(_VertexTornadoMaskChannel)];
		float heightStrength = poiFxSmoothstep(_VertexTornadoBaseHeight, _VertexTornadoTopHeight, v.vertex.y);
		float funnelStrength = pow(heightStrength, _VertexTornadoFunnelShape);
		float totalStrength = funnelStrength * tornadoMask;

		if (totalStrength > 0.0)
		{
			float currentRadius = _VertexTornadoRadius * funnelStrength;
			float angle = POI_TIME.y * _VertexTornadoSpeed + v.vertex.y * _VertexTornadoIntensity;
			float s = sin(angle);
			float c = cos(angle);

			vec2 offset = vec2(c, s) * currentRadius;

			vec3 targetPos = v.vertex.xyz;
			targetPos.xz += offset;
			targetPos.y += _VertexTornadoVerticalPull * totalStrength;

			v.vertex.xyz = mix(v.vertex.xyz, targetPos.xyz, totalStrength);
		}
	}

	if (_VertexWindEnabled >= 0.5)
	{
		float windMask = vertexEffectsMask[int(_VertexWindMaskChannel)];
		if (windMask > 0.0)
		{
			vec3 worldPos = (poiMesh.modelMatrix * v.vertex).xyz;

			vec2 noiseUV = worldPos.xz * _VertexWindNoiseScale * 0.1 + POI_TIME.y * _VertexWindNoiseSpeed;
#if defined(PROP_VERTEXBASICSMASK)
			vec4 windNoiseTex = POI_VERTEX_TEX_LOD(_VertexBasicsMask, poiUV(noiseUV, _VertexBasicsMask_ST));
			float noise = windNoiseTex[int(_VertexWindNoiseChannel)] * 2.0 - 1.0;
#else
			float noise = 0.0;
#endif
			float turbulence = mix(1.0, noise, _VertexWindNoiseStrength);

			float primaryWave = sin(POI_TIME.y * _VertexWindPrimarySpeed + dot(worldPos, normalize(_VertexWindPrimaryDirection.xyz)) * _VertexWindPrimaryFrequency) * turbulence;
			float detailWave = sin(POI_TIME.y * _VertexWindDetailSpeed + dot(worldPos, normalize(_VertexWindDetailDirection.xyz)) * _VertexWindDetailFrequency) * turbulence;

			vec3 primaryOffset = primaryWave * normalize(_VertexWindPrimaryDirection.xyz) * _VertexWindPrimaryAmplitude;
			vec3 detailOffset = detailWave * normalize(_VertexWindDetailDirection.xyz) * _VertexWindDetailAmplitude;

			vec3 windOffset = (primaryOffset + detailOffset) * o.normal.w;
			v.vertex.xyz += (poiMesh.invModelMatrix * vec4(windOffset, 0.0)).xyz * windMask;
		}
	}

	vec3 worldTranslation = mix(vec3(0.0), _VertexManipulationWorldTranslation.xyz, basicsMask);
	v.vertex.xyz += (poiMesh.invModelMatrix * vec4(worldTranslation * o.normal.w + heightOffset, 0.0)).xyz;

	if (_VertexRoundingEnabled >= 0.5)
	{
		float roundingMask = vertexEffectsMask[int(_VertexRoundingMaskChannel)];
		if (roundingMask > 0.0)
		{
			vec3 preRoundingVertex = v.vertex.xyz;
			float divisionAmount = max(_VertexRoundingDivision, 0.0000001);
			if (_VertexRoundingSpace == 0)
			{
				vec4 worldPos = poiMesh.modelMatrix * vec4(preRoundingVertex, 1.0);
				vec3 worldRoundPosition = (ceil(worldPos.xyz / divisionAmount) * divisionAmount) - divisionAmount * 0.5;
				v.vertex = poiMesh.invModelMatrix * vec4(worldRoundPosition, worldPos.w);
			}
			else if (_VertexRoundingSpace == 1)
			{
				v.vertex.xyz = (ceil(preRoundingVertex / divisionAmount) * divisionAmount) - divisionAmount * 0.5;
			}
			v.vertex.xyz = mix(preRoundingVertex, v.vertex.xyz, roundingMask);
		}
	}
#endif

#if defined(POI_VERTEX_LOOKAT)
	vec4 alpha = vec4(1.0);
	vec4 lookAtMask = vec4(1.0);

	if (_LookAtVisibilityEnabled >= 0.5)
	{
		float notVisible = 0.0;
		if (_LookAtVisibilityMode == 1.0) // per-view flags
		{
			// No mirror or camera mode globals here: a mirror view (oblique projection) counts as both mirror
			// kinds, and the handheld and photo camera modes never match.
			bool inMirror = IsInMirror(poiCam);

			notVisible += float(_LookAtVisibilityVRCRegular < 0.5 && !inMirror);
			notVisible += float(_LookAtVisibilityVRCMirrorVR < 0.5 && inMirror);
			notVisible += float(_LookAtVisibilityVRCMirrorDesktop < 0.5 && inMirror);
		}
		else if (_LookAtVisibilityMirror != 0.0) // plain mirror flag
		{
			notVisible += float((_LookAtVisibilityMirror == 1.0) != IsInMirror(poiCam));
		}

		if (notVisible != 0.0)
		{
			alpha = vec4(0.0);
		}
	}

#if defined(PROP_LOOKATMASK)
	lookAtMask = POI_VERTEX_TEX_LOD(_LookAtMask, poiUV(vertexUV(v, _LookAtMaskUV), _LookAtMask_ST) + POI_TIME.x * _LookAtMaskPan);
#endif
	lookAtMask *= alpha;

	vec4 qFinal = vec4(0.0, 0.0, 0.0, 1.0);
	float lookAtAlphaMask = 1.0;

	lookAtAlphaMask = _LookAtRedAlpha * lookAtMask.r;
	if (_LookAtRedMaskEnabled >= 0.5 && lookAtAlphaMask > 0.0)
	{
		qFinal = ComputeLookAtQuat(
			poiMesh,
			poiCam,
			lookAtAlphaMask,
			_LookAtRedOriginOffset.xyz,
			_LookAtRedForwardDirection.xyz,
			_LookAtRedUpDirection.xyz,
			_LookAtRedTargetUp,
			_LookAtRedFoV.xy,
			_LookAtRedDistanceFalloff.xy,
			_LookAtRedMaxPitchAngle.xy,
			_LookAtRedMaxYawAngle.xy,
			_LookAtRedMaxRollAngle.xy
		);

		ApplyQuaternionRotation(v, qFinal, _LookAtRedPivotOffset.xyz);
	}

	lookAtAlphaMask = _LookAtGreenAlpha * lookAtMask.g;
	if (_LookAtGreenMaskEnabled >= 0.5 && lookAtAlphaMask > 0.0)
	{
		vec4 q = ComputeLookAtQuat(
			poiMesh,
			poiCam,
			lookAtAlphaMask,
			RotateByQuaternion(qFinal, _LookAtGreenOriginOffset.xyz),
			RotateByQuaternion(qFinal, _LookAtGreenForwardDirection.xyz),
			RotateByQuaternion(qFinal, _LookAtGreenUpDirection.xyz),
			_LookAtGreenTargetUp,
			_LookAtGreenFoV.xy,
			_LookAtGreenDistanceFalloff.xy,
			_LookAtGreenMaxPitchAngle.xy,
			_LookAtGreenMaxYawAngle.xy,
			_LookAtGreenMaxRollAngle.xy
		);

		ApplyQuaternionRotation(v, q, RotateByQuaternion(qFinal, _LookAtGreenPivotOffset.xyz));
		qFinal = MulQuat(q, qFinal);
	}

	lookAtAlphaMask = _LookAtBlueAlpha * lookAtMask.b;
	if (_LookAtBlueMaskEnabled >= 0.5 && lookAtAlphaMask > 0.0)
	{
		vec4 q = ComputeLookAtQuat(
			poiMesh,
			poiCam,
			lookAtAlphaMask,
			RotateByQuaternion(qFinal, _LookAtBlueOriginOffset.xyz),
			RotateByQuaternion(qFinal, _LookAtBlueForwardDirection.xyz),
			RotateByQuaternion(qFinal, _LookAtBlueUpDirection.xyz),
			_LookAtBlueTargetUp,
			_LookAtBlueFoV.xy,
			_LookAtBlueDistanceFalloff.xy,
			_LookAtBlueMaxPitchAngle.xy,
			_LookAtBlueMaxYawAngle.xy,
			_LookAtBlueMaxRollAngle.xy
		);

		ApplyQuaternionRotation(v, q, RotateByQuaternion(qFinal, _LookAtBluePivotOffset.xyz));
		qFinal = MulQuat(q, qFinal);
	}

	lookAtAlphaMask = _LookAtAlphaAlpha * lookAtMask.a;
	if (_LookAtAlphaMaskEnabled >= 0.5 && lookAtAlphaMask > 0.0)
	{
		vec4 q = ComputeLookAtQuat(
			poiMesh,
			poiCam,
			lookAtAlphaMask,
			RotateByQuaternion(qFinal, _LookAtAlphaOriginOffset.xyz),
			RotateByQuaternion(qFinal, _LookAtAlphaForwardDirection.xyz),
			RotateByQuaternion(qFinal, _LookAtAlphaUpDirection.xyz),
			_LookAtAlphaTargetUp,
			_LookAtAlphaFoV.xy,
			_LookAtAlphaDistanceFalloff.xy,
			_LookAtAlphaMaxPitchAngle.xy,
			_LookAtAlphaMaxYawAngle.xy,
			_LookAtAlphaMaxRollAngle.xy
		);

		ApplyQuaternionRotation(v, q, RotateByQuaternion(qFinal, _LookAtAlphaPivotOffset.xyz));
	}
#endif

#if defined(POI_DISSOLVE)
	if (_UVTileDissolveEnabled >= 0.5 && _UVTileDissolveDiscardAtMax >= 0.5)
	{
		vec2 udim = v.uv0.xy * float(_UVTileDissolveUV == 0.0)
		+ v.uv1.xy * float(_UVTileDissolveUV == 1.0)
		+ v.uv2.xy * float(_UVTileDissolveUV == 2.0)
		+ v.uv3.xy * float(_UVTileDissolveUV == 3.0);
		vec4 xMask = vec4(
			float(udim.x >= 0.0 && udim.x < 1.0),
			float(udim.x >= 1.0 && udim.x < 2.0),
			float(udim.x >= 2.0 && udim.x < 3.0),
			float(udim.x >= 3.0 && udim.x < 4.0)
		);
		float tileAlpha = float(udim.y >= 0.0 && udim.y < 1.0) * dot(vec4(_UVTileDissolveAlpha_Row0_0, _UVTileDissolveAlpha_Row0_1, _UVTileDissolveAlpha_Row0_2, _UVTileDissolveAlpha_Row0_3), xMask)
		+ float(udim.y >= 1.0 && udim.y < 2.0) * dot(vec4(_UVTileDissolveAlpha_Row1_0, _UVTileDissolveAlpha_Row1_1, _UVTileDissolveAlpha_Row1_2, _UVTileDissolveAlpha_Row1_3), xMask)
		+ float(udim.y >= 2.0 && udim.y < 3.0) * dot(vec4(_UVTileDissolveAlpha_Row2_0, _UVTileDissolveAlpha_Row2_1, _UVTileDissolveAlpha_Row2_2, _UVTileDissolveAlpha_Row2_3), xMask)
		+ float(udim.y >= 3.0 && udim.y < 4.0) * dot(vec4(_UVTileDissolveAlpha_Row3_0, _UVTileDissolveAlpha_Row3_1, _UVTileDissolveAlpha_Row3_2, _UVTileDissolveAlpha_Row3_3), xMask);
		tileAlpha *= float(udim.x >= 0.0 && udim.x < 4.0 && udim.y >= 0.0 && udim.y < 4.0);
		if (tileAlpha > 0.999)
		{
			return VertexOut_nan();
		}
	}

	if (_DissolveVertexHeightEnabled >= 0.5)
	{
		vec2 vertUV = v.uv0.xy * float(_DissolveNoiseTextureUV == 0.0) + v.uv1.xy * float(_DissolveNoiseTextureUV == 1.0) + v.uv2.xy * float(_DissolveNoiseTextureUV == 2.0) + v.uv3.xy * float(_DissolveNoiseTextureUV == 3.0);
		vec2 vertP2PUV = v.uv0.xy * float(_DissolveP2PUV == 0.0) + v.uv1.xy * float(_DissolveP2PUV == 1.0) + v.uv2.xy * float(_DissolveP2PUV == 2.0) + v.uv3.xy * float(_DissolveP2PUV == 3.0);
		vec2 vertUVTileUV = v.uv0.xy * float(_UVTileDissolveUV == 0.0) + v.uv1.xy * float(_UVTileDissolveUV == 1.0) + v.uv2.xy * float(_UVTileDissolveUV == 2.0) + v.uv3.xy * float(_UVTileDissolveUV == 3.0);
		vec2 vertDetailUV = v.uv0.xy * float(_DissolveDetailNoiseUV == 0.0) + v.uv1.xy * float(_DissolveDetailNoiseUV == 1.0) + v.uv2.xy * float(_DissolveDetailNoiseUV == 2.0) + v.uv3.xy * float(_DissolveDetailNoiseUV == 3.0);

		vec3 vertLocalPos = v.vertex.xyz;
		vec3 vertWorldPos = (poiMesh.modelMatrix * v.vertex).xyz;

		float noiseTextureSample = 1.0;
#if defined(PROP_DISSOLVENOISETEXTURE)
		noiseTextureSample = POI_VERTEX_TEX_LOD(_DissolveNoiseTexture, poiUV(vertUV, _DissolveNoiseTexture_ST) + _DissolveNoiseTexturePan * POI_TIME.x).r;
#endif

		float vertGradient = getDissolveGradient(vertLocalPos, vertWorldPos, v.color.rgb, vertP2PUV, noiseTextureSample);
		float vertAmount = getDissolveAmount(vertUVTileUV, 1.0);
		float vertDetailNoise = 0.5;
		float vertDetailStrength = 0.0;
		if (_DissolveDetailNoiseEnabled >= 0.5)
		{
			vertDetailNoise = getDissolveDetailNoise(vertWorldPos, vertDetailUV);
			vertDetailStrength = _DissolveVertexHeightDetailStrength;
		}

		float vertSoftness = _DissolveSoftness * 0.5;
		float vertEdgeWidth = _DissolveEdgeEnabled != 0.0 ? _DissolveEdgeWidth : 0.0;
		float vertDetailPadding = 0.5 * vertDetailStrength * (1.0 - _DissolveDetailEdgeMask);
		float vertTotalPadding = max(2.0 * vertSoftness + vertEdgeWidth + vertDetailPadding, 0.01);
		vertAmount = (_DissolveWaveMode >= 0.5 && _DissolveWaveBounds == 2.0)
		? vertAmount
		: mix(-vertTotalPadding, 1.0 + vertTotalPadding, vertAmount);

		DissolveEdgeInput edgeInput;
		edgeInput.gradient = vertGradient;
		edgeInput.dissolveAmount = vertAmount;
		edgeInput.detailNoise = vertDetailNoise;
		edgeInput.detailStrength = vertDetailStrength;
		edgeInput.fwidthGradient = 0.0;
		edgeInput.heightOffset = _DissolveVertexHeightOffset;

		DissolveEdgeOutput edgeOutput = getDissolveEdge(edgeInput);
		float heightAlpha = edgeOutput.dissolveAlpha;
		float softness = max(_DissolveSoftness * 0.5, 0.001);
		switch (int(_DissolveVertexHeightEasing))
		{
			case 0:
				break;
			case 1:
				heightAlpha = clamp((softness - edgeOutput.distFromEdge) / (2.0 * softness), 0.0, 1.0);
				break;
			case 2:
				heightAlpha = 1.0 - (1.0 - heightAlpha) * (1.0 - heightAlpha);
				break;
			case 3:
				heightAlpha = heightAlpha * heightAlpha;
				break;
		}

		if (_DissolveVertexHeightContinue >= 0.5)
		{
			float pastPeak = max(0.0, -edgeOutput.distFromEdge - softness) / softness;
			heightAlpha += pastPeak;
		}

		vec3 customDir = normalize(_DissolveVertexHeightDirection);
		if (_DissolveVertexHeightDirectionSpace == 1.0)
		{
			customDir = normalize(mat3(poiMesh.invModelMatrix) * customDir);
		}
		vec3 displaceDir = normalize(mix(v.normal, customDir, _DissolveVertexHeightDirectionBlend));
		float dotMask = mix(1.0, clamp(dot(v.normal, displaceDir) * 0.5 + 0.5, 0.0, 1.0), _DissolveVertexHeightDotMask);

		v.vertex.xyz += displaceDir * heightAlpha * _DissolveVertexHeight * dotMask;
	}
#endif

#if defined(POI_MIRROR)
	float notVisible = 0.0;

	if (_VisibilityMode == 1.0) // per-view flags
	{
		// No mirror or camera mode globals here: a mirror view (oblique projection) counts as both mirror
		// kinds, and the handheld and photo camera modes never match.
		bool inMirror = IsInMirror(poiCam);

		notVisible += float(_VisibilityVRCRegular < 0.5 && !inMirror);
		notVisible += float(_VisibilityVRCMirrorVR < 0.5 && inMirror);
		notVisible += float(_VisibilityVRCMirrorDesktop < 0.5 && inMirror);
	}
	else if (_Mirror != 0.0) // plain mirror flag
	{
		notVisible += float((_Mirror == 1.0) != IsInMirror(poiCam));
	}

	if (notVisible != 0.0) // Early Return skips rest of vertex shader
	{
		return VertexOut_nan();
	}
#endif

	if (_UzumoreCategoryToggle >= 0.5 && _UzumoreEnabled >= 0.5)
	{
		vec2 maskUV = vec2(0.0);
#if defined(PROP_UZUMOREMASK)
		maskUV = poiUV(vertexUV(v, _UzumoreMaskUV), _UzumoreMask_ST);
#endif
		v.vertex.xyz = calcIntrudePos(poiMesh, poiCam, v.vertex.xyz, v.normal.xyz, maskUV, v.color);
	}

	o.normal.xyz = PoiObjectToWorldNormal(poiMesh, v.normal);
	o.tangent.xyz = normalize(mat3(poiMesh.modelMatrix) * v.tangent.xyz);
	o.tangent.w = v.tangent.w;
	o.vertexColor = v.color;

	o.localPos = v.vertex;
	o.worldPos = poiMesh.modelMatrix * o.localPos;

	vec3 localOffset = vec3(0.0, 0.0, 0.0);
	vec3 worldOffset = vec3(0.0, 0.0, 0.0);

#if defined(POI_PASS_OUTLINE)
#if defined(PROP_OUTLINEMASK)
	vec4 outlineMaskTex = POI_VERTEX_TEX_LOD(_OutlineMask, poiUV(vertexUV(v, _OutlineMaskUV), _OutlineMask_ST) + POI_TIME.x * _OutlineMaskPan);
	float outlineMask = outlineMaskTex[int(_OutlineMaskChannel)];
	float outLineZOffset = abs(mix(1.0, outlineMaskTex[int(_OutlineZOffsetChannel)], _OutlineZOffsetMaskStrength) - _OutlineZOffsetInvertMaskChannel);
#else
	float outlineMask = 1.0;
	float outLineZOffset = 1.0;
#endif
	if (_OutlineVertexColorMask > 0.0)
	{
		outlineMask *= mix(1.0, v.color[int(_OutlineVertexColorMask) - 1], _OutlineVertexColorMaskStrength);
	}
	if (_OutlineZOffsetVertexColor != 0.0)
	{
		outLineZOffset *= mix(1.0, v.color[int(_OutlineZOffsetVertexColor) - 1], _OutlineZOffsetVertexColorStrength);
	}

	vec3 outlineNormal = _OutlineSpace != 0.0 ? o.normal.xyz : v.normal;
	if (_OutlineUseVertexColorNormals >= 0.5)
	{
		vec3 outlineTangent = vec3(0.0);
		vec3 outlineBinormal = vec3(0.0);
		if (_OutlineSpace != 0.0) // 0 Local, 1 World
		{
			outlineTangent = o.tangent.xyz;
			outlineBinormal = cross(o.normal.xyz, o.tangent.xyz) * (v.tangent.w * poiMesh.worldTransformSign);
		}
		else
		{
			outlineTangent = v.tangent.xyz;
			outlineBinormal = normalize(cross(outlineNormal, outlineTangent)) * (v.tangent.w * length(outlineNormal));
		}
		vec3 outlineVectorTS = v.color.rgb * 2.0 - 1.0;
		outlineNormal = outlineVectorTS.x * outlineTangent + outlineVectorTS.y * outlineBinormal + outlineVectorTS.z * outlineNormal;
	}

	float offsetMultiplier = 1.0;
	float distanceOffset = 1.0;
	if (_OutlineFixedSize >= 0.5)
	{
		distanceOffset *= mix(1.0, clamp((distance(getCameraPosition(poiCam), (poiMesh.modelMatrix * o.localPos).xyz)) / o.normal.w, 0.0, _OutlinesMaxDistance), _OutlineFixWidth);
	}

	float lineWidth = _LineWidth;

	vec3 offset = outlineNormal * (lineWidth * _EnableOutlines / 100.0) * outlineMask * distanceOffset;

	if (_OutlineExpansionMode == 2.0)
	{
		// The source takes the main light plus the ambient's dominant direction. vertex() sees no light,
		// so the ambient's usual dominant direction, straight up, stands in.
		vec3 lightDirection = vec3(0.0, 1.0, 0.0);
		offsetMultiplier = clamp(dot(lightDirection, outlineNormal), 0.0, 1.0);
		offset *= offsetMultiplier;
		offset *= distanceOffset;
	}
	else if (_OutlineExpansionMode == 3.0)
	{
		vec3 viewNormal = mat3(poiCam.viewMatrix) * outlineNormal;
		offsetMultiplier = clamp(dot(viewNormal.xy, normalize(_OutlinePersonaDirection.xy)), 0.0, 1.0);

		offset *= offsetMultiplier;
		offset *= distanceOffset;
	}
	else if (_OutlineExpansionMode == 4.0)
	{
		offset = transpose(mat3(poiCam.viewMatrix)) * _OutlineDropShadowOffset.xyz;
		offset *= distanceOffset;
	}
	if (_OutlineSpace == 0.0)
	{
		localOffset += offset;
		worldOffset += mat3(poiMesh.modelMatrix) * offset;
	}
	else
	{
		offset *= o.normal.w;
		localOffset += mat3(poiMesh.invModelMatrix) * offset;
		worldOffset += offset;
	}
#endif

#if defined(POI_VERTEX_GLITCHING)
	bool canGlitch = true;
	if (_VertexGlitchMirrorEnable >= 0.5 && _VertexGlitchMirror > 0.0)
	{
		bool inMirror = IsInMirror(poiCam);
		if (_VertexGlitchMirror == 1.0 && !inMirror) canGlitch = false;
		if (_VertexGlitchMirror == 2.0 && inMirror) canGlitch = false;
	}
	if (canGlitch)
	{
		vec3 forward = getCameraPosition(poiCam) - (poiMesh.modelMatrix * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		forward.y = 0.0;
		forward = normalize(forward);
		vec3 glitchDirection = normalize(cross(vec3(0.0, 1.0, 0.0), forward));

		float glitchAmount = 0.0;

		if (_VertexGlitchUseMap >= 0.5)
		{
			float uvl = o.worldPos.y * _VertexGlitchDensity + POI_TIME.x * _VertexGlitchMapPanSpeed;
			float uvr = o.worldPos.y * _VertexGlitchDensity - POI_TIME.x * _VertexGlitchMapPanSpeed;

			vec3 glitchTextureL = POI_VERTEX_TEX_LOD(_VertexGlitchMap, vec2(uvl, uvl)).rgb;
			vec3 glitchTextureR = POI_VERTEX_TEX_LOD(_VertexGlitchMap, vec2(uvr, uvr)).rgb;

			glitchAmount += (glitchTextureL.r - 0.5) * 2.0;
			glitchAmount += -(glitchTextureR.r - 0.5) * 2.0;

			glitchAmount += (glitchTextureL.g - 0.5) * 2.0;
			glitchAmount += -(glitchTextureR.b - 0.5) * 2.0;
		}
		else
		{
			glitchAmount += fract(sin(dot(POI_TIME.xy + o.worldPos.y, vec2(12.9898, 78.233))) * 43758.5453123) * 2.0 - 1.0;
		}

		float time = POI_TIME.y * _VertexGlitchFrequency;

		float randomGlitch = (sin(time) + sin(2.2 * time + 5.52) + sin(2.9 * time + 0.93) + sin(4.6 * time + 8.94)) / 4.0;
		vec3 glitchOffset = vec3(0.0);

		glitchOffset = glitchAmount * glitchDirection * (_VertexGlitchStrength * 0.01) * step(_VertexGlitchThreshold, randomGlitch);

		float glitchMask = POI_VERTEX_TEX_LOD(_VertexGlitchingMask, poiUV(vertexUV(v, _VertexGlitchingMaskUV), _VertexGlitchingMask_ST)).r;
		glitchOffset *= glitchMask;

		localOffset += glitchOffset;
		worldOffset += mat3(poiMesh.modelMatrix) * glitchOffset;
	}
#endif

	o.localPos.xyz += localOffset;
	o.worldPos.xyz += worldOffset;

#if defined(POI_DEPTHBULGE)
	if (!inShadowPass)
	{
		applyDepthBulgeFX(poiMesh, poiCam, o);
	}
#endif

	o.pos = PoiObjectToClipPos(poiMesh, poiCam, o.localPos.xyz);

#if defined(POI_PASS_OUTLINE)
	// Reversed depth, as Unity's DirectX path.
	o.pos.z += _Offset_Z * outLineZOffset * -0.0001;
#endif

	o.worldDir = dot(o.pos, CalculateFrustumCorrection(poiCam));

	if (_RenderingReduceClipDistance >= 0.5)
	{
		applyReducedRenderClipDistance(poiCam, o);
	}

	return o;
}

void vertex()
{
	appdata v;
	POI_APPDATA_READ(v)
	PoiMesh poiMesh;
	PoiInitStruct(PoiMesh, poiMesh)
	POI_CAPTURE_OBJECT(poiMesh)
	PoiCam poiCam;
	PoiInitStruct(PoiCam, poiCam)
	POI_CAPTURE_CAMERA(poiCam)
	if (IN_SHADOW_PASS)
	{
		// A shadow map's view and projection are the light's, but the source still sees the viewing
		// camera's position and axes there (_WorldSpaceCameraPos, unity_WorldToCamera).
		poiCam.invViewMatrix = MAIN_CAM_INV_VIEW_MATRIX;
		poiCam.worldPos = MAIN_CAM_INV_VIEW_MATRIX[3].xyz;
		poiCam.centerEyePos = MAIN_CAM_INV_VIEW_MATRIX[3].xyz;
		poiCam.forwardDir = normalize(-MAIN_CAM_INV_VIEW_MATRIX[2].xyz);
	}

	VertexOut o = vert(v, poiMesh, poiCam, IN_SHADOW_PASS);

	// The engine carries these to the fragment the way the source built o: the normal by the normal matrix,
	// the tangent by the model matrix, the position as o.localPos. BINORMAL only carries the handedness.
	VERTEX = o.localPos.xyz;
	NORMAL = v.normal;
	TANGENT = v.tangent.xyz;
	BINORMAL = cross(v.normal, v.tangent.xyz) * v.tangent.w;
	// Every path writes POSITION once vertex() writes it at all.
	POSITION = o.pos;
	POI_VERTEX_OUT_WRITE(o)
}
