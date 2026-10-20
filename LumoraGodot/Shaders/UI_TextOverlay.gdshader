shader_type spatial;
// OVERLAY variant of UI_Text: depth test DISABLED so glyphs always draw above world geometry -
// context menu labels and other overlay UI whose text must never be cut into by session content.
// Swapped in by the material hook when a UI text material asks for ZTest Always/Disabled; keep the
// uniforms in lockstep with UI_Text.gdshader or values silently stop applying after a swap. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_front;

uniform vec4 albedo_color : source_color = vec4(1.0);
// Sample MIP 0 only (no mipmaps) - see UI_Text.gdshader for the full story: MSDF is resolution
// independent and mipmaps pre-average the distance field into blur. - xlinka
uniform sampler2D albedo_texture : filter_linear, repeat_disable;
uniform bool use_albedo_texture = true;
uniform bool use_vertex_color = true;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

uniform float pixel_range = 8.0;
uniform float coverage_gamma : hint_range(0.5, 2.0) = 0.9;
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
uniform vec2 clip_offset = vec2(0.0);

uniform int color_mask = 15;
uniform int stencil_comparison = 8;
uniform int stencil_operation = 0;
uniform int stencil_id = 0;
uniform int stencil_write_mask = 255;
uniform int stencil_read_mask = 255;
uniform int z_write = 0;
uniform int z_test = 4;

varying vec2 local_pos;
// Per-glyph edge push for bold letters, from UV2.x; 0 for regular text.
varying float glyph_dilate;

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

// Four samples once a pixel spans more than two atlas texels, same as UI_Text.gdshader. -xlinka
float msdf_face(vec2 uv, float edge, float aa, float softness) {
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	vec2 atlas = vec2(textureSize(albedo_texture, 0));
	if (max(length(dx * atlas), length(dy * atlas)) < 2.0) {
		vec3 m = textureLod(albedo_texture, uv, 0.0).rgb;
		return clamp((median3(m.r, m.g, m.b) - edge) / (aa + softness) + 0.5, 0.0, 1.0);
	}
	float spread = aa * 0.5 + softness;
	vec3 a = textureLod(albedo_texture, uv + dx * 0.125 + dy * 0.375, 0.0).rgb;
	vec3 b = textureLod(albedo_texture, uv - dx * 0.125 - dy * 0.375, 0.0).rgb;
	vec3 c = textureLod(albedo_texture, uv + dx * 0.375 - dy * 0.125, 0.0).rgb;
	vec3 d = textureLod(albedo_texture, uv - dx * 0.375 + dy * 0.125, 0.0).rgb;
	return 0.25 * (clamp((median3(a.r, a.g, a.b) - edge) / spread + 0.5, 0.0, 1.0)
		+ clamp((median3(b.r, b.g, b.b) - edge) / spread + 0.5, 0.0, 1.0)
		+ clamp((median3(c.r, c.g, c.b) - edge) / spread + 0.5, 0.0, 1.0)
		+ clamp((median3(d.r, d.g, d.b) - edge) / spread + 0.5, 0.0, 1.0));
}

void vertex() {
	VERTEX.xy += clip_offset;
	local_pos = VERTEX.xy;
	glyph_dilate = UV2.x;
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	float clip_alpha = rect_clip ? rect_alpha(local_pos, rect) : 1.0;

	vec4 out_color = albedo_color;
	if (use_vertex_color) {
		out_color *= COLOR;
	}

	if (use_albedo_texture) {
		if (use_msdf) {
			float range = msdf_screen_range(UV);
			float aa = 1.0 / max(range, 0.0001);
			float spread = aa + face_softness;
			float face_edge = 0.5 - face_dilate - glyph_dilate;
			float face = msdf_face(UV, face_edge, aa, face_softness);
			if (outline_thickness > 0.0) {
				float outer = msdf_face(UV, face_edge - min(outline_thickness, 0.45), aa, face_softness);
				// Inside the face the face colour shows, in the ring the outline's, and the alpha follows the colour
				// that shows: a face with almost no alpha leaves an outlined glyph hollow. -xlinka
				vec4 ringed = mix(out_color, outline_color, 1.0 - face);
				out_color = vec4(ringed.rgb, ringed.a * outer);
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
			// Crisped on the glyph's own coverage, before the colour, same as UI_Text.gdshader. -xlinka
			float coverage = pow(clamp(texture(albedo_texture, UV).a, 0.0, 1.0), coverage_gamma);
			if (alpha_clip) {
				float width = max(fwidth(coverage), 0.001);
				coverage *= smoothstep(alpha_cutoff - width, alpha_cutoff + width, coverage);
			}
			out_color.a *= coverage;
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
