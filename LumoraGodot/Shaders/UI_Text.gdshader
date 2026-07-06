shader_type spatial;
// UI is rendered as a logical 2D layer above world geometry. Render priority
// handles ordering inside the UI tree.
render_mode unshaded, blend_mix, depth_draw_never, cull_front;

// FontAssetHook stores glyph coverage in alpha and uploads mipmaps so distance
// sampling stays stable.

uniform vec4 albedo_color : source_color = vec4(1.0);
// Plain bilinear, no mipmaps, no aniso. At glancing angles screen-space
// derivatives grow along the foreshortened axis and the GPU picks higher mip
// levels of the font atlas - the glyph alpha blurs, then the fwidth-widened
// smoothstep below preserves the blurred shape with a thick fuzzy edge, so
// glyph interiors disappear and labels bleed white across the whole quad.
// Forcing mip 0 keeps glyphs sharp at any angle. The 96-px atlas raster is
// high enough that distant text reads cleanly without mipmaps. - xlinka
uniform sampler2D albedo_texture : filter_linear, repeat_disable;
uniform bool use_albedo_texture = true;
uniform bool use_vertex_color = true;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

// MSDF: distance-field span (in atlas texels) the font was generated with. Must match the atlas's
// msdf_pixel_range. Drives the screen-space edge width in the MSDF branch below. - xlinka
uniform float pixel_range = 8.0;
uniform float coverage_gamma : hint_range(0.5, 2.0) = 0.9;
// When the atlas holds a multi-channel signed distance field (RGB), reconstruct the glyph from the median of
// the three channels for a crisp edge at any size/angle. Off = plain coverage in the alpha channel (fallback
// for bitmap fonts or when the font wasn't imported with MSDF). Set by the material from the atlas. -xlinka
uniform bool use_msdf = false;

// Crisp text at any distance/angle: smoothstep across the half-coverage
// threshold using fwidth so the edge stays ~1px wide in screen space, rather
// than soft-alphaing the whole glyph coverage. - xlinka
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
uniform bool alpha_clip = true;

uniform bool rect_clip = false;
uniform vec4 rect = vec4(0.0);
// Scroll offset added to the vertex before the rect test (see UI_Unlit). Lets a moved (scrolled) content
// chunk clip correctly against a fixed canvas-space rect without re-tessellating. Zero for static UI. -xlinka
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

// Screen pixels the atlas's pixel_range spans at this fragment. Converts the distance field's texel range into
// screen space so the smoothstep edge stays ~1px wide regardless of how big/small/angled the glyph is drawn -
// this is what keeps MSDF crisp without mipmaps. - xlinka
float msdf_screen_range(vec2 uv) {
	vec2 unit_range = vec2(pixel_range) / vec2(textureSize(albedo_texture, 0));
	vec2 screen_tex_size = vec2(1.0) / max(fwidth(uv), vec2(1e-6));
	return max(0.5 * dot(unit_range, screen_tex_size), 1.0);
}

void vertex() {
	// Scroll offset moves the vertex and the clip test from the same uniform (see UI_Unlit) - keeps a
	// scrolled chunk's position and clip in lockstep so text never leaks past the viewport. -xlinka
	VERTEX.xy += clip_offset;
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
		if (use_msdf) {
			// Reconstruct the signed distance from the median of the 3 MSDF channels, then anti-alias across
			// the 0.5 threshold in SCREEN space so the edge is a clean ~1px regardless of scale. -xlinka
			vec3 msd = texture(albedo_texture, UV).rgb;
			float sd = median3(msd.r, msd.g, msd.b);
			coverage = clamp(msdf_screen_range(UV) * (sd - 0.5) + 0.5, 0.0, 1.0);
		} else {
			coverage = texture(albedo_texture, UV).a;
			coverage = pow(clamp(coverage, 0.0, 1.0), coverage_gamma);
		}
	}
	out_color.a *= coverage;

	// Coverage path still wants the fwidth smoothstep to crisp its soft alpha; MSDF already produced a clean
	// screen-space edge above, so the extra smoothstep would only erode it. -xlinka
	if (alpha_clip && !use_msdf) {
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
