// Shared by the screen filters (HSV, LUT, Threshold, Refract): each reads what is BEHIND its surface
// from hint_screen_texture at SCREEN_UV and writes a changed copy over it.
//
// Every filter is one file. The pass is always the transparent one and the blend is always a plain
// replace, because the colour being blended onto is the very colour the filter already read: additive
// and multiply are done here against that read, not by the blend unit. Culling is a uniform for the
// same reason, both faces are drawn and the culled one discarded. What is behind means the opaque
// scene; transparent surfaces drawn earlier are not in the screen copy. -xlinka

uniform sampler2D screen_behind : hint_screen_texture, filter_linear;

// Set by the material hook. 0 Opaque, 1 Cutout, 2 Alpha, 3 Transparent, 4 Additive, 5 Multiply.
uniform int blend_mode = 0;
// Set by the material hook. 0 culls back faces, 1 front faces, 2 neither.
uniform int cull_mode = 0;

bool filter_culled(bool front_facing) {
	return (cull_mode == 0 && !front_facing) || (cull_mode == 1 && front_facing);
}

// The opaque scene behind carries full alpha, so alpha and premultiplied blending land on the
// filtered colour unchanged.
vec3 filter_blend(vec3 filtered, vec3 behind) {
	if (blend_mode == 4) {
		return behind + filtered;
	}
	if (blend_mode == 5) {
		return behind * filtered;
	}
	return filtered;
}
