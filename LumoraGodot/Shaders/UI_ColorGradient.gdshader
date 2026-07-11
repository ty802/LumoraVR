shader_type spatial;
// Color-picker gradient surface. Same UI layer conventions as UI_Unlit (depth-tested but never
// depth-written, ordering by render_priority, rect_clip + clip_offset so it composes inside Helio
// panels) - only the fragment body swaps the albedo texture for a procedurally generated gradient
// picked by gradient_mode. HSV->RGB is done in-shader so a picker never needs a baked ramp. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, cull_front;

// 0 = saturation/value square (x=sat 0->1 L->R, y=val 1->0 T->B) for the hue below.
// 1 = full HSV rainbow strip along the axis picked by `vertical`.
// 2 = two-color lerp color_a->color_b along the axis.
// 3 = color_a alpha 0->1 over a procedural checkerboard (transparency preview).
uniform int gradient_mode = 0;
// Hue in DEGREES [0,360) that the saturation/value square (mode 0) is drawn for.
uniform float hue : hint_range(0.0, 360.0) = 0.0;
// Axis select for the strip/lerp/ramp modes: false = horizontal (UV.x), true = vertical (UV.y).
uniform bool vertical = false;
uniform vec4 color_a : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 color_b : source_color = vec4(1.0, 1.0, 1.0, 1.0);
// Checkerboard cell size in canvas pixels for the alpha-ramp background (mode 3).
uniform float checker_px : hint_range(1.0, 128.0) = 8.0;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

uniform bool rect_clip = false;
uniform vec4 rect = vec4(0.0);
// Added to the vertex position before the rect test, same as UI_Unlit: for scrolled content whose
// chunk is moved by a transform the clip window stays pinned to the viewport. Zero for static UI. -xlinka
uniform vec2 clip_offset = vec2(0.0);

uniform int color_mask = 15;

varying vec2 local_pos;

float rect_alpha(vec2 pos, vec4 clip_rect) {
	vec2 edge = max(fwidth(pos), vec2(0.0001));
	float left = smoothstep(clip_rect.x - edge.x, clip_rect.x + edge.x, pos.x);
	float bottom = smoothstep(clip_rect.y - edge.y, clip_rect.y + edge.y, pos.y);
	float right = 1.0 - smoothstep(clip_rect.z - edge.x, clip_rect.z + edge.x, pos.x);
	float top = 1.0 - smoothstep(clip_rect.w - edge.y, clip_rect.w + edge.y, pos.y);
	return clamp(left * bottom * right * top, 0.0, 1.0);
}

// Standard branchless HSV->RGB. h,s,v all in [0,1].
vec3 hsv2rgb(vec3 c) {
	vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
	vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
	return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

void vertex() {
	VERTEX.xy += clip_offset;
	local_pos = VERTEX.xy;
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	float clip_alpha = rect_clip ? rect_alpha(local_pos, rect) : 1.0;

	// Axis parameter for the 1D modes (strip / lerp / ramp).
	float axis = vertical ? UV.y : UV.x;

	vec4 out_color = vec4(1.0);

	if (gradient_mode == 0) {
		// Saturation across X, value down Y, for the fixed hue.
		float sat = UV.x;
		float val = 1.0 - UV.y;
		out_color = vec4(hsv2rgb(vec3(hue / 360.0, sat, val)), 1.0);
	} else if (gradient_mode == 1) {
		out_color = vec4(hsv2rgb(vec3(axis, 1.0, 1.0)), 1.0);
	} else if (gradient_mode == 2) {
		out_color = mix(color_a, color_b, axis);
	} else {
		// Alpha ramp of color_a composited over a procedural checkerboard background.
		vec2 cell = floor(local_pos / max(checker_px, 1.0));
		float checker = mod(cell.x + cell.y, 2.0);
		vec3 bg = mix(vec3(0.4), vec3(0.6), checker);
		float a = color_a.a * axis;
		out_color = vec4(mix(bg, color_a.rgb, a), 1.0);
	}

	out_color.a *= clip_alpha;

	if ((color_mask % 2) == 0) out_color.r = 0.0;
	if (((color_mask / 2) % 2) == 0) out_color.g = 0.0;
	if (((color_mask / 4) % 2) == 0) out_color.b = 0.0;
	if (((color_mask / 8) % 2) == 0) out_color.a = 0.0;

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
