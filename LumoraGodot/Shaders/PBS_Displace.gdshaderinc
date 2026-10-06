// PBS moved by maps. Three of them, each with its own scale, offset, magnitude and bias:
//
//   the vertex map pushes every vertex along its normal by the map's red channel,
//   the UV map shifts where the surface samples its textures by the map's red and green,
//   the world map shifts where the vertex map is read, from the world position of the object's origin, or of
//   each vertex with WorldspaceOffsetPerVertex. A floor that ripples as you walk across it is this one.
//
// A bias with no map still offsets: a missing map reads as black.
//
// Package space has X mirrored and texture rows running bottom-up, so a position becomes (-x, z) and the V of
// both a lookup and a shift flips; an imported ripple runs the way its author saw it. -xlinka
#define PBS_DISPLACE

uniform sampler2D vertex_displace_map : filter_linear_mipmap, repeat_enable;
uniform bool use_vertex_displace_map = false;
uniform float vertex_displace_magnitude = 1.0;
uniform float vertex_displace_bias = 0.0;
uniform vec2 vertex_displace_map_scale = vec2(1.0);
uniform vec2 vertex_displace_map_offset = vec2(0.0);

uniform sampler2D uv_displace_map : filter_linear_mipmap, repeat_enable;
uniform bool use_uv_displace_map = false;
uniform float uv_displace_magnitude = 1.0;
uniform float uv_displace_bias = 0.0;
uniform vec2 uv_displace_map_scale = vec2(1.0);
uniform vec2 uv_displace_map_offset = vec2(0.0);

uniform sampler2D worldspace_vertex_offset_map : filter_linear_mipmap, repeat_enable;
uniform bool use_worldspace_vertex_offset_map = false;
uniform vec2 worldspace_offset_magnitude = vec2(1.0);
uniform vec2 worldspace_vertex_offset_map_scale = vec2(1.0);
uniform vec2 worldspace_vertex_offset_map_offset = vec2(0.0);
uniform bool worldspace_offset_per_vertex = false;

// Distance along the normal, from the raw mesh UV.
float displace_vertex_offset(vec2 uv, vec3 vertex, mat4 model) {
	if (use_worldspace_vertex_offset_map) {
		vec3 anchor = worldspace_offset_per_vertex ? (model * vec4(vertex, 1.0)).xyz : model[3].xyz;
		vec2 lookup = vec2(-anchor.x, anchor.z) * worldspace_vertex_offset_map_scale + worldspace_vertex_offset_map_offset;
		lookup.y = 1.0 - lookup.y;
		vec2 shift = textureLod(worldspace_vertex_offset_map, lookup, 0.0).rg * worldspace_offset_magnitude;
		uv += vec2(shift.x, -shift.y);
	}
	float height = 0.0;
	if (use_vertex_displace_map) {
		height = textureLod(vertex_displace_map, uv * vertex_displace_map_scale + vertex_displace_map_offset, 0.0).r;
	}
	return height * vertex_displace_magnitude + vertex_displace_bias;
}

// Added to the transformed main UV, so every map of the surface moves together.
vec2 displace_uv_offset(vec2 uv) {
	vec2 shift = vec2(0.0);
	if (use_uv_displace_map) {
		shift = texture(uv_displace_map, uv * uv_displace_map_scale + uv_displace_map_offset).rg;
	}
	shift = shift * uv_displace_magnitude + uv_displace_bias;
	return vec2(shift.x, -shift.y);
}

#include "res://Shaders/PBS_Effect_Core.gdshaderinc"
