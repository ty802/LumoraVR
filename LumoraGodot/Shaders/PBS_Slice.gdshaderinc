// PBS cut away by up to eight planes. Whatever lies on the negative side of any plane is discarded, and the
// surface within the transition band of the nearest plane blends to the edge colour and edge emission, which
// is what makes a cut read as a glowing seam rather than a hole.
//
// A plane arrives as (normal, -dot(normal, point)), so a fragment's signed distance is one dot product plus w.
// The normal is not normalised: its length scales the distance, and imported planes count on that.
// The list ends at the first zero normal, and a material with no planes slices nothing. -xlinka
#define PBS_SLICE

uniform vec4 edge_color : source_color = vec4(1.0);
uniform vec4 edge_emissive_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float edge_transition_start = 0.0;
uniform float edge_transition_end = 0.01;
// Planes in the mesh's own space instead of world space.
uniform bool local_space = false;

uniform vec4 slicer0 = vec4(0.0);
uniform vec4 slicer1 = vec4(0.0);
uniform vec4 slicer2 = vec4(0.0);
uniform vec4 slicer3 = vec4(0.0);
uniform vec4 slicer4 = vec4(0.0);
uniform vec4 slicer5 = vec4(0.0);
uniform vec4 slicer6 = vec4(0.0);
uniform vec4 slicer7 = vec4(0.0);

uniform sampler2D detail_albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_detail_albedo_texture = false;
uniform sampler2D detail_normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_detail_normal_map = false;
uniform float detail_normal_scale = 1.0;
uniform vec2 detail_texture_scale = vec2(1.0);
uniform vec2 detail_texture_offset = vec2(0.0);

varying vec3 slice_position;

// Negative when the point is cut away, otherwise how far into the edge band it sits: 1 on the plane, 0 past
// the end of the transition.
float slice_edge(vec3 p) {
	vec4 planes[8] = vec4[8](slicer0, slicer1, slicer2, slicer3, slicer4, slicer5, slicer6, slicer7);
	float nearest = 60000.0;
	for (int i = 0; i < 8; i++) {
		if (all(equal(planes[i].xyz, vec3(0.0)))) {
			break;
		}
		nearest = min(nearest, dot(p, planes[i].xyz) + planes[i].w);
	}
	if (nearest < 0.0) {
		return -1.0;
	}
	float span = edge_transition_end - edge_transition_start;
	if (abs(span) < 1e-6) {
		span = 1e-6;
	}
	return 1.0 - clamp((nearest - edge_transition_start) / span, 0.0, 1.0);
}

#include "res://Shaders/PBS_Effect_Core.gdshaderinc"
