shader_type spatial;
// World-space text (nameplates, labels). Same glyph coverage path as
// UI_Text.gdshader but WITH depth testing so text occludes correctly behind
// world geometry - UI_Text is an overlay layer and must not depth test.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

uniform vec4 albedo_color : source_color = vec4(1.0);
// Plain bilinear, no mipmaps, no aniso - see UI_Text.gdshader for why mip
// selection at glancing angles destroys glyph alpha. - xlinka
uniform sampler2D albedo_texture : filter_linear, repeat_disable;
uniform bool use_albedo_texture = true;
uniform bool use_vertex_color = true;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

// Kept for future SDF/MSDF migration. Unused in the coverage path. - xlinka
uniform float pixel_range = 4.0;
uniform float coverage_gamma : hint_range(0.5, 2.0) = 0.9;

// Crisp text at any distance/angle: smoothstep across the half-coverage
// threshold using fwidth so the edge stays ~1px wide in screen space. - xlinka
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
uniform bool alpha_clip = true;

uniform bool rect_clip = false;
uniform vec4 rect = vec4(0.0);

uniform int color_mask = 15;
uniform int stencil_comparison = 8;
uniform int stencil_operation = 0;
uniform int stencil_id = 0;
uniform int stencil_write_mask = 255;
uniform int stencil_read_mask = 255;
uniform int z_write = 0;
uniform int z_test = 4;

varying vec2 local_pos;

float rect_alpha(vec2 pos, vec4 clip_rect) {
	vec2 edge = max(fwidth(pos), vec2(0.0001));
	float left = smoothstep(clip_rect.x - edge.x, clip_rect.x + edge.x, pos.x);
	float bottom = smoothstep(clip_rect.y - edge.y, clip_rect.y + edge.y, pos.y);
	float right = 1.0 - smoothstep(clip_rect.z - edge.x, clip_rect.z + edge.x, pos.x);
	float top = 1.0 - smoothstep(clip_rect.w - edge.y, clip_rect.w + edge.y, pos.y);
	return clamp(left * bottom * right * top, 0.0, 1.0);
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

	out_color.a *= clip_alpha;

	if ((color_mask % 2) == 0) out_color.r = 0.0;
	if (((color_mask / 2) % 2) == 0) out_color.g = 0.0;
	if (((color_mask / 4) % 2) == 0) out_color.b = 0.0;
	if (((color_mask / 8) % 2) == 0) out_color.a = 0.0;

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
