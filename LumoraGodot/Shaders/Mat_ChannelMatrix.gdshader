shader_type spatial;
// A colour matrix applied to whatever is BEHIND this surface. Tinted visors, night-vision lenses,
// camera filters.
//
// The pixels behind come from hint_screen_texture read at SCREEN_UV, the fragment's own screen
// position, with no explicit grab pass.
//
//     rgb = mat3(levels_r.xyz, levels_g.xyz, levels_b.xyz) * rgb + vec3(r.w, g.w, b.w)
//     rgb = clamp(rgb, clamp_min, clamp_max)
//
// MIND THE MATRIX ORDER. levels_r, levels_g and levels_b are ROWS, one per output channel; GLSL's
// mat3(a, b, c) takes them as COLUMNS, and a matrix built column-wise from the same three vectors is
// the transpose. Written out as three dots instead, so each channel is its own row dotted with the
// input, whatever a mat3 constructor means.
//
// Unlit on purpose: the result IS the answer, not a surface to be lit. -xlinka
render_mode blend_mix, unshaded, cull_back, depth_draw_never, depth_test_disabled;

uniform sampler2D screen_behind : hint_screen_texture, filter_linear_mipmap;

// Row of the matrix per output channel: xyz weights the incoming rgb, w is a flat offset added after.
uniform vec4 levels_r = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 levels_g = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 levels_b = vec4(0.0, 0.0, 1.0, 0.0);

uniform vec3 clamp_min = vec3(0.0);
uniform vec3 clamp_max = vec3(2.0);

// The lens can still carry its own tint and fade over the filtered result.
uniform vec4 tint : source_color = vec4(1.0);

void fragment() {
	vec3 c = texture(screen_behind, SCREEN_UV).rgb;

	c = vec3(
		dot(levels_r.xyz, c) + levels_r.w,
		dot(levels_g.xyz, c) + levels_g.w,
		dot(levels_b.xyz, c) + levels_b.w);

	c = clamp(c, clamp_min, clamp_max);

	ALBEDO = c * tint.rgb;
	ALPHA = tint.a;
}
