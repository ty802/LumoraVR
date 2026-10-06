shader_type canvas_item;
// The dash capture over the desktop window, the same wet glass the headset gets on its panel. The capture's
// colour arrives multiplied by its own coverage; what it leaves uncovered shows the frame behind it, softened
// rather than frosted, bent a little by runs of water and the odd bead, and pulled toward a dark tint so text
// on the glass stays readable over a bright sky. The water only ever bends the frame behind, never the UI.
// The window canvas is sRGB, so both sides are taken to linear for the mix and back, or the world comes out
// darker here than in the headset. - xlinka
render_mode unshaded, blend_mix;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;
uniform float blur_radius : hint_range(0.0, 16.0) = 1.6;
// Linear, like the mix it feeds.
uniform vec4 tint_color = vec4(0.0023, 0.0023, 0.004, 1.0);
// How far the room is pulled toward the tint: hardly at all under the thin veil of the empty desk, fully
// under a window, a bar or a card. The pull follows how much of the pixel the dash covers, so the room reads
// plainly between windows and text still sits on dark glass.
uniform float tint_thin : hint_range(0.0, 1.0) = 0.15;
uniform float tint_strength : hint_range(0.0, 1.0) = 0.45;
uniform float thin_below : hint_range(0.0, 1.0) = 0.2;
uniform float dense_from : hint_range(0.0, 1.0) = 0.6;
uniform float glass_from : hint_range(0.01, 1.0) = 0.1;
uniform float run_bend : hint_range(0.0, 16.0) = 2.5;
uniform float bead_bend : hint_range(0.0, 32.0) = 7.0;
uniform float bead_density : hint_range(0.0, 64.0) = 14.0;

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec3 to_srgb(vec3 c) {
	c = clamp(c, 0.0, 1.0);
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

vec2 bead_hash(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// Where the water on the glass sends the eye at this point, in px, and how far into a bead's rim it is.
vec3 water(vec2 uv, float aspect) {
	vec2 bend = vec2(sin(uv.x * 91.0 + sin(uv.y * 5.0) * 2.4), 0.35 * sin(uv.y * 37.0 + uv.x * 11.0)) * run_bend;
	vec2 p = vec2(uv.x * aspect, uv.y) * bead_density;
	vec2 cell = floor(p);
	vec2 h = bead_hash(cell);
	float rim = 0.0;
	if (h.x > 0.66) {
		vec2 centre = cell + 0.3 + 0.4 * bead_hash(cell + 17.0);
		float radius = 0.12 + 0.16 * h.y;
		vec2 d = (p - centre) / radius;
		float r = length(d);
		if (r < 1.0) {
			bend -= d * (1.0 - r * r) * bead_bend;
			rim = smoothstep(0.65, 1.0, r);
		}
	}
	return vec3(bend, rim);
}

vec3 tap(vec2 uv) {
	return to_linear(texture(screen_tex, uv).rgb);
}

vec3 soften(vec2 uv, vec2 px) {
	vec2 o = px * blur_radius;
	vec3 sum = tap(uv) * 4.0;
	sum += (tap(uv + vec2(o.x, 0.0)) + tap(uv - vec2(o.x, 0.0)) + tap(uv + vec2(0.0, o.y)) + tap(uv - vec2(0.0, o.y))) * 2.0;
	sum += tap(uv + o) + tap(uv - o) + tap(uv + vec2(o.x, -o.y)) + tap(uv + vec2(-o.x, o.y));
	return sum / 16.0;
}

void fragment() {
	vec4 dash = texture(TEXTURE, UV);
	if (dash.a <= 0.001) {
		discard;
	}
	float glass = clamp(dash.a / glass_from, 0.0, 1.0);
	float uncovered = 1.0 - dash.a;
	vec3 behind = vec3(0.0);
	if (uncovered > 0.001) {
		vec2 size = vec2(textureSize(TEXTURE, 0));
		vec3 wet = water(UV, size.x / max(size.y, 1.0));
		behind = soften(SCREEN_UV + wet.xy * SCREEN_PIXEL_SIZE, SCREEN_PIXEL_SIZE) * (1.0 - wet.z * 0.12);
		float pull = mix(tint_thin, tint_strength, smoothstep(thin_below, dense_from, dash.a));
		behind = mix(behind, tint_color.rgb, pull);
	}
	float cover = dash.a + glass * uncovered;
	COLOR = vec4(to_srgb((to_linear(dash.rgb) + behind * uncovered * glass) / cover), cover);
}
