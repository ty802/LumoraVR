shader_type spatial;
// Frosted backdrop for modal overlays: samples the screen behind it and either blurs
// (gaussian) or quantizes (pixelate) it, then dims toward tint_color. Same UI layer
// conventions as UI_Unlit - depth ignored, ordering by render_priority. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;

// 0 = Gaussian, 1 = Pixelate.
uniform int blur_mode = 0;
// Gaussian: per-tap sample spread in px. Pixelate: block size in px.
uniform float blur_radius : hint_range(0.0, 64.0) = 6.0;
// Dim/wash applied over the blurred result; alpha = how far to mix toward the color.
uniform vec4 tint_color : source_color = vec4(0.03, 0.03, 0.06, 0.55);
// Overall opacity of the backdrop. < 1.0 lets the content behind show through (dimmed) - important
// because the dashboard UI is transparent and is NOT in the screen copy the blur samples, so the
// frost alone would just hide it. Keeping this translucent makes the panel read as a modal. - xlinka
uniform float opacity : hint_range(0.0, 1.0) = 0.85;

uniform bool rect_clip = false;
uniform vec4 rect = vec4(0.0);

varying vec2 local_pos;

float rect_alpha(vec2 pos, vec4 clip_rect) {
	vec2 edge = max(fwidth(pos), vec2(0.0001));
	float left = smoothstep(clip_rect.x - edge.x, clip_rect.x + edge.x, pos.x);
	float bottom = smoothstep(clip_rect.y - edge.y, clip_rect.y + edge.y, pos.y);
	float right = 1.0 - smoothstep(clip_rect.z - edge.x, clip_rect.z + edge.x, pos.x);
	float top = 1.0 - smoothstep(clip_rect.w - edge.y, clip_rect.w + edge.y, pos.y);
	return clamp(left * bottom * right * top, 0.0, 1.0);
}

// 5x5 weighted gaussian in a single pass. Cheap enough for a full-screen backdrop that
// only renders while a dialog is open. blur_radius scales the tap spacing. - xlinka
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

// Quantize the screen UV into blur_radius-sized blocks and sample the block center.
vec3 pixelate(vec2 uv, vec2 px) {
	vec2 block = px * max(blur_radius, 1.0);
	vec2 snapped = (floor(uv / block) + 0.5) * block;
	return texture(screen_tex, snapped).rgb;
}

void vertex() {
	local_pos = VERTEX.xy;
}

void fragment() {
	vec2 px = 1.0 / VIEWPORT_SIZE;
	vec3 base = (blur_mode == 1) ? pixelate(SCREEN_UV, px) : gaussian(SCREEN_UV, px);
	vec3 col = mix(base, tint_color.rgb, tint_color.a);

	ALBEDO = col;
	ALPHA = opacity * (rect_clip ? rect_alpha(local_pos, rect) : 1.0);
}
