// Where a vertex colour is blended across its triangle. 0 is linear, the plain interpolation. 1 is sRGB:
// the colour is gamma encoded at the vertex, interpolated, and decoded again per pixel, so a gradient
// between two vertices steps evenly to the eye, the way a colour picker's wheel does. The colour that
// reaches each vertex is the same either way; only the in-between changes. -xlinka
uniform int vertex_color_interpolation_space = 0;

vec3 encode_srgb(vec3 c) {
	c = max(c, vec3(0.0));
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

vec3 decode_srgb(vec3 c) {
	c = max(c, vec3(0.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

// In vertex(), before the colour is interpolated.
vec4 vertex_color_encode(vec4 c) {
	return vertex_color_interpolation_space == 1 ? vec4(encode_srgb(c.rgb), c.a) : c;
}

// In fragment(), on the interpolated colour.
vec4 vertex_color_decode(vec4 c) {
	return vertex_color_interpolation_space == 1 ? vec4(decode_srgb(c.rgb), c.a) : c;
}
