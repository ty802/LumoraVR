// PBS pushed and lit by how close it is to up to sixteen points. Every point adds a displacement that runs
// from DisplaceMagnitudeFrom at DisplaceFrom metres to DisplaceMagnitudeTo at DisplaceTo, and an emission that
// runs from EmissionColorFrom to EmissionColorTo over its own pair of distances, tinted by the point's colour.
// The defaults push out and glow as a point comes within a metre.
//
// A non-zero grid snaps the position being measured to cells of that size per axis, so the surface moves and
// lights in blocks; a zero axis is not snapped. Displacement runs along the vertex normal, or along
// override_displacement_direction in the mesh's own space when that is not zero. -xlinka
#define PBS_DISTANCE_LERP

uniform vec3 grid_size = vec3(0.0);
uniform vec3 grid_offset = vec3(0.0);

uniform float displace_from = 1.0;
uniform float displace_to = 0.0;
uniform float displace_magnitude_from = 0.0;
uniform float displace_magnitude_to = 0.25;

uniform float emission_from = 1.0;
uniform float emission_to = 0.0;
uniform vec4 emission_color_from : source_color = vec4(0.0);
uniform vec4 emission_color_to : source_color = vec4(1.5, 1.5, 1.5, 0.0);

uniform vec3 override_displacement_direction = vec3(0.0);
// Points in the mesh's own space instead of world space.
uniform bool local_space = false;

uniform int point_count = 0;
uniform vec3 point0;
uniform vec3 point1;
uniform vec3 point2;
uniform vec3 point3;
uniform vec3 point4;
uniform vec3 point5;
uniform vec3 point6;
uniform vec3 point7;
uniform vec3 point8;
uniform vec3 point9;
uniform vec3 point10;
uniform vec3 point11;
uniform vec3 point12;
uniform vec3 point13;
uniform vec3 point14;
uniform vec3 point15;
uniform vec4 point_tint0 : source_color = vec4(1.0);
uniform vec4 point_tint1 : source_color = vec4(1.0);
uniform vec4 point_tint2 : source_color = vec4(1.0);
uniform vec4 point_tint3 : source_color = vec4(1.0);
uniform vec4 point_tint4 : source_color = vec4(1.0);
uniform vec4 point_tint5 : source_color = vec4(1.0);
uniform vec4 point_tint6 : source_color = vec4(1.0);
uniform vec4 point_tint7 : source_color = vec4(1.0);
uniform vec4 point_tint8 : source_color = vec4(1.0);
uniform vec4 point_tint9 : source_color = vec4(1.0);
uniform vec4 point_tint10 : source_color = vec4(1.0);
uniform vec4 point_tint11 : source_color = vec4(1.0);
uniform vec4 point_tint12 : source_color = vec4(1.0);
uniform vec4 point_tint13 : source_color = vec4(1.0);
uniform vec4 point_tint14 : source_color = vec4(1.0);
uniform vec4 point_tint15 : source_color = vec4(1.0);

varying vec3 distance_emission;

// A from equal to its to is a step, not a division by zero.
float distance_span(float from, float to) {
	float span = to - from;
	return abs(span) < 1e-6 ? 1e-6 : span;
}

// x is the displacement, yzw the added emission.
vec4 distance_lerp(vec3 p) {
	vec3 snapped = round((p + grid_offset) / grid_size) * grid_size;
	p = mix(snapped, p, equal(grid_size, vec3(0.0)));

	vec3 points[16] = vec3[16](point0, point1, point2, point3, point4, point5, point6, point7,
		point8, point9, point10, point11, point12, point13, point14, point15);
	vec4 tints[16] = vec4[16](point_tint0, point_tint1, point_tint2, point_tint3, point_tint4, point_tint5,
		point_tint6, point_tint7, point_tint8, point_tint9, point_tint10, point_tint11, point_tint12,
		point_tint13, point_tint14, point_tint15);

	float displace_span = distance_span(displace_from, displace_to);
	float emission_span = distance_span(emission_from, emission_to);
	float displace = 0.0;
	vec3 glow = vec3(0.0);
	int count = min(point_count, 16);
	for (int i = 0; i < count; i++) {
		float d = distance(p, points[i]);
		float displace_t = clamp((d - displace_from) / displace_span, 0.0, 1.0);
		float emission_t = clamp((d - emission_from) / emission_span, 0.0, 1.0);
		displace += mix(displace_magnitude_from, displace_magnitude_to, displace_t);
		glow += tints[i].rgb * mix(emission_color_from.rgb, emission_color_to.rgb, emission_t);
	}
	return vec4(displace, glow);
}

#include "res://Shaders/PBS_Effect_Core.gdshaderinc"
