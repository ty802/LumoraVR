shader_type spatial;
// Gradient sky material. Each fragment shows the sky in the world direction from the eye through it,
// worked exactly as GradientSkybox works it, so a mesh wearing this is a window onto that same sky.
// Unshaded and opaque: writes no ALPHA, so it stays in the OPAQUE pass. -xlinka
render_mode unshaded, fog_disabled, blend_mix, cull_back, depth_draw_opaque;

uniform vec4 top_color : source_color = vec4(0.06, 0.16, 0.32, 1.0);
uniform vec4 horizon_color : source_color = vec4(0.38, 0.74, 0.90, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.015, 0.045, 0.08, 1.0);
uniform vec4 sun_color : source_color = vec4(1.0, 0.82, 0.48, 1.0);
uniform vec3 sun_direction = vec3(-0.38, 0.58, -0.72);
uniform float sun_size : hint_range(0.0, 0.5) = 0.032;
uniform float sun_intensity : hint_range(0.0, 16.0) = 1.8;
uniform float sun_glow_power : hint_range(1.0, 512.0) = 96.0;

void fragment() {
	// VERTEX sits in the shared view space and EYE_OFFSET is the eye drawing it, so each eye looks
	// through the surface along its own line and the sky stays at infinity in stereo.
	vec3 view_dir = normalize(mat3(INV_VIEW_MATRIX) * (VERTEX - EYE_OFFSET));
	vec3 sun_dir = normalize(sun_direction);
	float height = clamp(view_dir.y * 0.5 + 0.5, 0.0, 1.0);

	vec3 lower = mix(bottom_color.rgb, horizon_color.rgb, smoothstep(0.0, 0.58, height));
	vec3 sky_col = mix(lower, top_color.rgb, smoothstep(0.42, 1.0, height));

	float sun_dot = clamp(dot(view_dir, sun_dir), 0.0, 1.0);
	// A size of 0 would make both smoothstep edges 1, which is undefined; the sky hook floors it alike.
	float disc = smoothstep(cos(max(sun_size, 0.001)), 1.0, sun_dot);
	float glow = pow(sun_dot, sun_glow_power * 0.22) * 0.18;

	ALBEDO = sky_col + sun_color.rgb * (disc + glow) * sun_intensity;
}
