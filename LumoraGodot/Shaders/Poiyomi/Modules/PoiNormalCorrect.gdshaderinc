// Ported from Poiyomi Toon Shader 10.0.24 (MIT, Poiyomi Labs); see LICENSE.txt.

// normal correct code from https://github.com/yoship1639/UniToon (MIT)
void applyNormalCorrect(in PoiMesh poiMesh, inout VertexOut i)
{
	vec3 posOS = i.localPos.xyz;
	vec3 normalCorrectObject = posOS - _NormalCorrectOrigin;
	normalCorrectObject.y = 0.0;
	normalCorrectObject = normalize(normalCorrectObject);
	vec3 normalCorrectWorld = mat3(poiMesh.modelMatrix) * normalCorrectObject;
	i.normal.xyz = normalize(mix(i.normal.xyz, normalCorrectWorld, _NormalCorrectAmount));
}
