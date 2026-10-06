shader_type spatial;
// A dash capture laid over the room it floats in, like wet glass: the capture was drawn onto a clear
// background, so its colour arrives already multiplied by how much of each pixel it covers, and what it
// leaves uncovered shows the room behind it, softened rather than frosted, bent a little by runs of water
// and the odd bead, and pulled toward a dark tint so text on it stays readable over a bright sky. The water
// is fixed to the glass and only ever bends the room: the UI's own pixels are never moved. Unshaded and past
// the depth buffer: no light in the room reaches the UI and nothing in the room covers it. - xlinka
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D dash_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;
// Spread of the softening, in px: enough to take the edge off, little enough that the room stays the room.
uniform float blur_radius : hint_range(0.0, 16.0) = 1.6;
uniform vec4 tint_color : source_color = vec4(0.03, 0.03, 0.05, 1.0);
// How far the room is pulled toward the tint: hardly at all under the thin veil of the empty desk, fully
// under a window, a bar or a card. The pull follows how much of the pixel the dash covers, so the room reads
// plainly between windows and text still sits on dark glass.
uniform float tint_thin : hint_range(0.0, 1.0) = 0.15;
uniform float tint_strength : hint_range(0.0, 1.0) = 0.45;
uniform float thin_below : hint_range(0.0, 1.0) = 0.2;
uniform float dense_from : hint_range(0.0, 1.0) = 0.6;
// Coverage at which a pixel is all glass. Below it the room shows through untouched, so a panel's rounded
// corners and anything left blank stay clear instead of reading as a dark box around the panel.
uniform float glass_from : hint_range(0.01, 1.0) = 0.1;
// How far the water bends the room, in px: the runs down the glass and the beads on it.
uniform float run_bend : hint_range(0.0, 16.0) = 2.5;
uniform float bead_bend : hint_range(0.0, 32.0) = 7.0;
// Bead cells per height of glass; about a third of them carry a bead.
uniform float bead_density : hint_range(0.0, 64.0) = 14.0;

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

vec3 soften(vec2 uv, vec2 px) {
	vec2 o = px * blur_radius;
	vec3 sum = texture(screen_tex, uv).rgb * 4.0;
	sum += (texture(screen_tex, uv + vec2(o.x, 0.0)).rgb + texture(screen_tex, uv - vec2(o.x, 0.0)).rgb
		+ texture(screen_tex, uv + vec2(0.0, o.y)).rgb + texture(screen_tex, uv - vec2(0.0, o.y)).rgb) * 2.0;
	sum += texture(screen_tex, uv + o).rgb + texture(screen_tex, uv - o).rgb
		+ texture(screen_tex, uv + vec2(o.x, -o.y)).rgb + texture(screen_tex, uv + vec2(-o.x, o.y)).rgb;
	return sum / 16.0;
}

void fragment() {
	vec4 dash = texture(dash_tex, UV);
	if (dash.a <= 0.001) {
		discard;
	}
	// The uncovered part of the pixel is behind glass, the rest of it is the room as it is.
	float glass = clamp(dash.a / glass_from, 0.0, 1.0);
	float uncovered = 1.0 - dash.a;
	vec3 behind = vec3(0.0);
	if (uncovered > 0.001) {
		vec2 px = 1.0 / VIEWPORT_SIZE;
		vec2 size = vec2(textureSize(dash_tex, 0));
		vec3 wet = water(UV, size.x / max(size.y, 1.0));
		behind = soften(SCREEN_UV + wet.xy * px, px) * (1.0 - wet.z * 0.12);
		float pull = mix(tint_thin, tint_strength, smoothstep(thin_below, dense_from, dash.a));
		behind = mix(behind, tint_color.rgb, pull);
	}
	float cover = dash.a + glass * uncovered;
	ALBEDO = (dash.rgb + behind * uncovered * glass) / cover;
	ALPHA = cover;
}
