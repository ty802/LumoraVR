shader_type spatial;
// Stencil-TESTED text variant of UI_Text: glyphs draw only where the stencil buffer equals the mask
// reference (written first by UI_StencilWrite). That clips text inside a circular/rounded mask to the
// SHAPE instead of squaring it off at the mask's AABB. Identical to UI_Text otherwise. Godot 4.5+
// stencil_mode. -xlinka
render_mode unshaded, blend_mix, depth_draw_never, cull_front;
stencil_mode read, compare_equal, 1;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : filter_linear, repeat_disable;
uniform bool use_albedo_texture = true;
uniform bool use_vertex_color = true;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

uniform float pixel_range = 8.0;
uniform float coverage_gamma : hint_range(0.5, 2.0) = 0.9;
// Same MSDF-vs-coverage branch as UI_Text: an MSDF atlas keeps the shape in RGB with opaque alpha, so reading
// coverage from alpha would fill the whole glyph quad solid. Set from atlas.IsMSDF by the material. -xlinka
uniform bool use_msdf = false;
// SDF styling, same units/semantics as UI_Text.gdshader. -xlinka
uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float outline_thickness = 0.0;
uniform float face_dilate = 0.0;
uniform float face_softness = 0.0;
uniform vec4 underlay_color : source_color = vec4(0.0);
uniform vec2 underlay_offset = vec2(0.0);
uniform float underlay_softness = 0.0;

uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
uniform bool alpha_clip = true;

uniform bool rect_clip = false;
uniform vec4 rect = vec4(0.0);

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

float median3(float a, float b, float c) {
	return max(min(a, b), min(max(a, b), c));
}

float msdf_screen_range(vec2 uv) {
	vec2 unit_range = vec2(pixel_range) / vec2(textureSize(albedo_texture, 0));
	vec2 screen_tex_size = vec2(1.0) / max(fwidth(uv), vec2(1e-6));
	return max(0.5 * dot(unit_range, screen_tex_size), 1.0);
}

void vertex() {
	local_pos = VERTEX.xy;
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	float clip_alpha = rect_clip ? rect_alpha(local_pos, rect) : 1.0;

	vec4 out_color = albedo_color;
	if (use_vertex_color) {
		out_color *= COLOR;
	}

	if (use_msdf) {
		vec3 msd = texture(albedo_texture, UV).rgb;
		float sd = median3(msd.r, msd.g, msd.b);
		float range = msdf_screen_range(UV);
		float spread = 1.0 / max(range, 0.0001) + face_softness;
		float face_edge = 0.5 - face_dilate;
		float face = clamp((sd - face_edge) / spread + 0.5, 0.0, 1.0);
		if (outline_thickness > 0.0) {
			float outer = clamp((sd - (face_edge - outline_thickness)) / spread + 0.5, 0.0, 1.0);
			float face_a = out_color.a * face;
			float ring_a = outline_color.a * outer;
			float comb_a = max(face_a, ring_a);
			out_color = vec4(mix(outline_color.rgb, out_color.rgb, face_a / max(comb_a, 0.0001)), comb_a);
		} else {
			out_color.a *= face;
		}
		if (underlay_color.a > 0.0) {
			vec2 uv2 = UV - underlay_offset / vec2(textureSize(albedo_texture, 0));
			vec3 msd2 = texture(albedo_texture, uv2).rgb;
			float sd2 = median3(msd2.r, msd2.g, msd2.b);
			float u = clamp((sd2 - face_edge) / max(spread + underlay_softness, 0.0001) + 0.5, 0.0, 1.0);
			float u_a = underlay_color.a * u * (1.0 - out_color.a);
			float total = out_color.a + u_a;
			out_color = vec4((out_color.rgb * out_color.a + underlay_color.rgb * u_a) / max(total, 0.0001), total);
		}
	} else {
		float coverage = 1.0;
		if (use_albedo_texture) {
			coverage = texture(albedo_texture, UV).a;
			coverage = pow(clamp(coverage, 0.0, 1.0), coverage_gamma);
		}
		out_color.a *= coverage;

		if (alpha_clip) {
			float alpha_width = max(fwidth(out_color.a), 0.001);
			out_color.a *= smoothstep(alpha_cutoff - alpha_width, alpha_cutoff + alpha_width, out_color.a);
		}
	}

	out_color.a *= clip_alpha;

	if ((color_mask % 2) == 0) out_color.r = 0.0;
	if (((color_mask / 2) % 2) == 0) out_color.g = 0.0;
	if (((color_mask / 4) % 2) == 0) out_color.b = 0.0;
	if (((color_mask / 8) % 2) == 0) out_color.a = 0.0;

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
