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

// MSDF distance-field span in atlas texels (the font's msdf_pixel_range); drives the screen-space edge width
// in the MSDF branch below. Set by the material from the atlas. - xlinka
uniform float pixel_range = 8.0;
uniform float coverage_gamma : hint_range(0.5, 2.0) = 0.9;
// The shared font atlas can be MSDF (distance field in RGB, alpha left opaque) OR plain coverage (shape in
// alpha). Nameplates/labels render off the SAME atlas as Helio UI text, so branch per material: MSDF rebuilds
// the glyph from median(RGB), coverage reads alpha. Miss this on an MSDF atlas and alpha is 1 everywhere, so
// the whole quad fills solid - that is the "nametag is just a white block" regression. Set from atlas.IsMSDF. - xlinka
uniform bool use_msdf = false;

// Optional glyph outline for floating text (nameplates). This atlas stores coverage, not a
// signed distance, so we approximate an outline by dilating the coverage (max of 8 offset
// taps) and tinting the surrounding ring. thickness is in texels; 0 disables, so
// non-nameplate text is unchanged. - xlinka
uniform vec4 outline_color : source_color = vec4(0.0);
uniform float outline_thickness = 0.0;

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

float median3(float a, float b, float c) {
	return max(min(a, b), min(max(a, b), c));
}

// Screen pixels the atlas's pixel_range spans at this fragment. Converts the distance field's texel range into
// screen space so the smoothstep edge stays ~1px wide however big/small/angled the glyph is drawn - keeps
// world-space MSDF text crisp without mipmaps, same as UI_Text. - xlinka
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
		// Distance-field glyph: rebuild coverage from the median of the 3 MSDF channels, anti-aliased in
		// SCREEN space so the edge stays ~1px at any size/distance. Alpha is opaque on an MSDF atlas, so the
		// coverage=texture().a path below would fill the whole quad solid (that was the white-block bug). - xlinka
		vec3 msd = texture(albedo_texture, UV).rgb;
		float sd = median3(msd.r, msd.g, msd.b);
		float screen_range = msdf_screen_range(UV);
		float face = clamp(screen_range * (sd - 0.5) + 0.5, 0.0, 1.0);

		if (outline_thickness > 0.0) {
			// Outline = a second edge pushed outward by thickness texels. One atlas texel is 1/pixel_range in
			// distance-field units, so shift the 0.5 threshold out by that much and take the ring. - xlinka
			float offset = outline_thickness / max(pixel_range, 0.0001);
			float outer = clamp(screen_range * (sd - (0.5 - offset)) + 0.5, 0.0, 1.0);
			float face_a = out_color.a * face;
			float out_a = outline_color.a * outer;
			float comb_a = max(face_a, out_a);
			out_color = vec4(mix(outline_color.rgb, out_color.rgb, face_a / max(comb_a, 0.0001)), comb_a);
		} else {
			out_color.a *= face;
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

		// Outline post-step: leaves the glyph face untouched (face_a dominates where the glyph is
		// solid) and fills the dilated ring around it with outline_color. Gated on thickness.
		if (outline_thickness > 0.0 && use_albedo_texture) {
			vec2 ts = outline_thickness / vec2(textureSize(albedo_texture, 0));
			float dil = 0.0;
			dil = max(dil, texture(albedo_texture, UV + vec2(ts.x, 0.0)).a);
			dil = max(dil, texture(albedo_texture, UV + vec2(-ts.x, 0.0)).a);
			dil = max(dil, texture(albedo_texture, UV + vec2(0.0, ts.y)).a);
			dil = max(dil, texture(albedo_texture, UV + vec2(0.0, -ts.y)).a);
			dil = max(dil, texture(albedo_texture, UV + ts).a);
			dil = max(dil, texture(albedo_texture, UV - ts).a);
			dil = max(dil, texture(albedo_texture, UV + vec2(ts.x, -ts.y)).a);
			dil = max(dil, texture(albedo_texture, UV + vec2(-ts.x, ts.y)).a);
			float out_a = pow(clamp(dil, 0.0, 1.0), coverage_gamma) * outline_color.a;
			float face_a = out_color.a;
			float comb_a = max(face_a, out_a);
			vec3 comb_rgb = mix(outline_color.rgb, out_color.rgb, face_a / max(comb_a, 0.0001));
			out_color = vec4(comb_rgb, comb_a);
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
