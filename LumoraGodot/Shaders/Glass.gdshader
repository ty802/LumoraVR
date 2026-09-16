shader_type spatial;
// Frosted glass for a panel's backing: the screen behind is blurred and washed toward a tint, and
// unlike the modal blur this one keeps the depth test, so a hand or a laser in front of the panel
// stays in front of it. The shape comes from the mesh; corners are the mesh's corners. - xlinka
render_mode unshaded, blend_mix, depth_draw_opaque, cull_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;

// Per-tap sample spread in px for the 5x5 gaussian.
uniform float blur_radius : hint_range(0.0, 64.0) = 8.0;
// The wash over the blurred result; alpha is how far to mix toward it.
uniform vec4 tint_color : source_color = vec4(0.15, 0.14, 0.22, 0.6);
// Overall opacity; below 1.0 the unblurred scene bleeds through a little, which reads as glass.
uniform float opacity : hint_range(0.0, 1.0) = 0.92;

vec3 gaussian(vec2 uv, vec2 px) {
	vec3 sum = vec3(0.0);
	float total = 0.0;
	for (int x = -2; x <= 2; x++) {
		for (int y = -2; y <= 2; y++) {
			vec2 off = vec2(float(x), float(y)) * px * blur_radius;
			float w = exp(-float(x * x + y * y) * 0.5);
			sum += texture(screen_tex, uv + off).rgb * w;
			total += w;
		}
	}
	return sum / max(total, 0.0001);
}

void fragment() {
	vec2 px = 1.0 / VIEWPORT_SIZE;
	vec3 base = gaussian(SCREEN_UV, px);
	ALBEDO = mix(base, tint_color.rgb, tint_color.a);
	ALPHA = opacity;
}
