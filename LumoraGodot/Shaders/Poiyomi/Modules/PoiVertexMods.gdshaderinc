// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.
// Vertex program helpers: near clip reduction, vertex look-at, depth bulge.

// The source's SampleLevel(_linear_repeat, uv, 0) in Poiyomi's UV space. A macro: a function taking the
// sampler would tie every caller to one filter and repeat setting.
#define POI_VERTEX_TEX_LOD(tex, uv) textureLod(tex, poiUvToTexture(uv), 0.0)

#if !defined(POI_FX_SMOOTHSTEP)
#define POI_FX_SMOOTHSTEP
// smoothstep as the source evaluates it: reversed or equal edges stay defined.
float poiFxSmoothstep(float edge0, float edge1, float x)
{
	float t = clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
	return t * t * (3.0 - 2.0 * t);
}
#endif

void applyReducedRenderClipDistance(in PoiCam poiCam, inout VertexOut o)
{
	if (o.pos.w < poiCam.nearPlane * 1.01 && o.pos.w > 0.0)
	{
		// Reversed depth, as Unity's DirectX path.
		o.pos.z = o.pos.z * 0.0001 + o.pos.w * 0.999;
	}
}

#if defined(POI_VERTEX_LOOKAT)
vec4 ComputeLookAtQuat(
	in PoiMesh poiMesh,
	in PoiCam poiCam,
	float alpha,
	vec3 lookAtOriginOffset,
	vec3 forwardDirectionOS,
	vec3 upDirectionOS,
	float targetUp,
	vec2 fovAngles,
	vec2 distanceFalloff,
	vec2 maxPitchAngle,
	vec2 maxYawAngle,
	vec2 maxRollAngle
)
{
	maxPitchAngle.x = min(maxPitchAngle.x, 0.0);
	maxPitchAngle.y = max(maxPitchAngle.y, 0.0);
	maxYawAngle.x = min(maxYawAngle.x, 0.0);
	maxYawAngle.y = max(maxYawAngle.y, 0.0);
	maxRollAngle.x = min(maxRollAngle.x, 0.0);
	maxRollAngle.y = max(maxRollAngle.y, 0.0);

	forwardDirectionOS = normalize(forwardDirectionOS);
	upDirectionOS = normalize(upDirectionOS);

	vec3 originOS = lookAtOriginOffset;
	vec3 EyeWS = (poiMesh.modelMatrix * vec4(originOS, 1.0)).xyz;

	vec3 camPos = getCameraPosition(poiCam);
	vec3 viewDirWS = normalize(camPos - EyeWS);

	float dotDeg = dotToDegrees(viewDirWS, normalize(mat3(poiMesh.modelMatrix) * forwardDirectionOS));
	alpha *= poiFxSmoothstep(fovAngles.y, fovAngles.x, dotDeg);
	alpha *= poiFxSmoothstep(distanceFalloff.y, distanceFalloff.x, length(camPos - EyeWS));
	if (alpha <= 0.0)
	{
		return vec4(0.0, 0.0, 0.0, 1.0);
	}

	vec3 targetFwdOS = normalize(mat3(poiMesh.invModelMatrix) * viewDirWS);

	vec3 targetUpOS = vec3(0.0, 1.0, 0.0);
	switch (int(targetUp))
	{
		// View Dir Up, 0, World Up, 1, Camera Up, 2, Model Up, 3
		case 0:
		{
			vec3 worldUpOS = normalize(mat3(poiMesh.invModelMatrix) * vec3(0.0, 1.0, 0.0));
			float proj = dot(worldUpOS, targetFwdOS);
			targetUpOS = normalize(worldUpOS - proj * targetFwdOS);
			break;
		}
		case 1:
		{
			targetUpOS = normalize(mat3(poiMesh.invModelMatrix) * vec3(0.0, 1.0, 0.0));
			break;
		}
		case 2:
		{
			// Row 1 of the view matrix: the camera's up axis.
			vec3 viewUp = vec3(poiCam.viewMatrix[0][1], poiCam.viewMatrix[1][1], poiCam.viewMatrix[2][1]);
			targetUpOS = normalize(mat3(poiMesh.invModelMatrix) * normalize(viewUp));
			break;
		}
		case 3:
		{
			targetUpOS = vec3(0.0, 1.0, 0.0);
			break;
		}
		default:
		{
			targetUpOS = vec3(0.0, 1.0, 0.0);
			break;
		}
	}

	vec4 qOriginal = BuildQuatFromForwardUp(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0));
	vec4 qSrc = BuildQuatFromForwardUp(forwardDirectionOS, upDirectionOS);
	vec4 qSrcInv = vec4(-qSrc.x, -qSrc.y, -qSrc.z, qSrc.w);
	vec4 qTgt = BuildQuatFromForwardUp(targetFwdOS, targetUpOS);

	vec4 deltaQ = MulQuat(qTgt, qSrcInv);
	vec4 blendedQ = SlerpQuaternion(qOriginal, deltaQ, alpha);

	vec3 e = QuaternionToEuler(blendedQ);
	e.x = clamp(e.x, maxPitchAngle.x, maxPitchAngle.y);
	e.y = clamp(e.y, maxYawAngle.x, maxYawAngle.y);
	e.z = clamp(e.z, maxRollAngle.x, maxRollAngle.y);

	return EulerToQuaternion(e);
}

void ApplyQuaternionRotation(inout appdata v, vec4 quaternion, vec3 pivotOffset)
{
	v.vertex.xyz -= pivotOffset;
	v.vertex.xyz = RotateByQuaternion(quaternion, v.vertex.xyz);
	v.vertex.xyz += pivotOffset;
	v.normal = RotateByQuaternion(quaternion, normalize(v.normal));
	v.tangent = vec4(RotateByQuaternion(quaternion, normalize(v.tangent.xyz)), v.tangent.w);
}
#endif

#if defined(POI_DEPTHBULGE)
void applyDepthBulgeFX(in PoiMesh poiMesh, in PoiCam poiCam, inout VertexOut o)
{
	vec4 pos = PoiObjectToClipPos(poiMesh, poiCam, o.localPos.xyz);
	vec4 grabPos = PoiComputeScreenPos(poiCam, pos);

	if (!poiDepthTextureExists())
	{
		return;
	}
	float depth = poiSampleScreenDepth(grabPos.xy / grabPos.w);

#if defined(PROP_DEPTHBULGEMASK)
	vec4 depthBulgeMaskTex = POI_VERTEX_TEX_LOD(_DepthBulgeMask, poiUV(vertexUV(o, _DepthBulgeMaskUV), _DepthBulgeMask_ST));
	float depthBulgeMask = depthBulgeMaskTex[int(_DepthBulgeMaskChannel)];
#else
	float depthBulgeMask = 1.0;
#endif

	depth = Linear01Depth(poiCam, depth);
	float bulgeScale = o.normal.w;

	float intersect = 0.0;
	if (depth != 1.0)
	{
		float diff = distance(depth, Linear01Depth(poiCam, pos.z / pos.w));
		if (diff > 0.0)
		{
			// _ProjectionParams.w is 1 / far.
			intersect = 1.0 - poiFxSmoothstep(0.0, (1.0 / poiCam.farPlane) * _DepthBulgeFadeLength * bulgeScale, diff);
		}
	}
	vec4 offset = intersect * _DepthBulgeHeight * bulgeScale * vec4(o.normal.xyz, 0.0);

	offset = IsInMirror(poiCam) ? vec4(0.0) : offset;
	offset *= depthBulgeMask;

	o.worldPos.xyz += offset.xyz;
	o.localPos.xyz += (poiMesh.invModelMatrix * vec4(offset.xyz, 0.0)).xyz;
}
#endif
