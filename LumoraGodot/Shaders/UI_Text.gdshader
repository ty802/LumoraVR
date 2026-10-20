shader_type spatial;
// UI is rendered as a logical 2D layer above world geometry. Render priority
// handles ordering inside the UI tree.
render_mode unshaded, blend_mix, depth_draw_never, cull_front;

// FontAssetHook stores glyph coverage in alpha and uploads mipmaps so distance
// sampling stays stable.

uniform vec4 albedo_color : source_color = vec4(1.0);
// Sample MIP 0 only (no mipmaps), and for the love of god do not "improve" this to
// filter_linear_mipmap_anisotropic. I did exactly that thinking it'd sharpen text at a distance and it does
// the opposite: MSDF is resolution-independent, the median reconstruction + the screen-space pixel_range
// below rebuild a crisp 1px edge from the FULL-RES distance field at any size. Mipmaps pre-average the
// distance field, so minified/far text samples a blurred mip and the whole edge goes soft. That is the
// "blurry at a distance" trap, learned the hard way. Aniso needs mipmaps so it's out for the same reason;
// the fwidth range already handles glancing angles. The atlas still uploads mips, just never sampled. - xlinka
uniform sampler2D albedo_texture : filter_linear, repeat_disable;
uniform bool use_albedo_texture = true;
uniform bool use_vertex_color = true;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

// MSDF: distance-field span (in atlas texels) the font was generated with. Must match the atlas's
// msdf_pixel_range. Drives the screen-space edge width in the MSDF branch below. - xlinka
uniform float pixel_range = 8.0;
uniform float coverage_gamma : hint_range(0.5, 2.0) = 0.9;
// SDF styling (MSDF path only - coverage has no true distance to offset). Units are distance-field units:
// 0.5 spans the whole pixel_range, so 0.2 = ~1.6 atlas texels at range 8. face_dilate bolds the glyph by
// pushing its edge outward; outline_thickness draws an outline_color ring outside the dilated face. -xlinka
uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float outline_thickness = 0.0;
uniform float face_dilate = 0.0;
// face_softness widens the edge-smoothing band past the ~1px screen AA (glow/soft text, 0 = crisp).
// underlay = drop shadow: the field re-sampled at underlay_offset (atlas texels), laid BEHIND the glyph. -xlinka
uniform float face_softness = 0.0;
uniform vec4 underlay_color : source_color = vec4(0.0);
uniform vec2 underlay_offset = vec2(0.0);
uniform float underlay_softness = 0.0;
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

// Screen pixels the atlas's pixel_range spans at this fragment. Converts the distance field's texel range into
// screen space so the smoothstep edge stays ~1px wide regardless of how big/small/angled the glyph is drawn -
// this is what keeps MSDF crisp without mipmaps. - xlinka
float msdf_screen_range(vec2 uv) {
	vec2 unit_range = vec2(pixel_range) / vec2(textureSize(albedo_texture, 0));
	vec2 screen_tex_size = vec2(1.0) / max(fwidth(uv), vec2(1e-6));
	return max(0.5 * dot(unit_range, screen_tex_size), 1.0);
}

// Glyph coverage across this fragment. Once a pixel spans more than two atlas texels (small or far text) one
// sample per pixel flickers and drops thin strokes, so it takes four spread over the pixel, each with half the
// edge width. aa is one screen pixel in field units. The atlas has no mips, so level 0 is read by hand. -xlinka
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
	// Scroll offset moves the vertex and the clip test from the same uniform (see UI_Unlit) - keeps a
	// scrolled chunk's position and clip in lockstep so text never leaks past the viewport. -xlinka
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
			// Reconstruct the signed distance from the median of the 3 MSDF channels, then anti-alias across
			// the (dilated) edge in SCREEN space so it stays ~1px at any scale. The outline is a second,
			// further-out threshold filled with outline_color; the face wins where both cover. -xlinka
			float range = msdf_screen_range(UV);
			// Base smoothing width is the ~1px screen AA (1/range in field units); softness adds to it.
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
			// Coverage still wants the fwidth smoothstep to crisp its soft alpha (MSDF already has a clean edge).
			// It runs on the glyph's own coverage, before the colour: run on the tinted alpha, a colour at half
			// alpha or less sat on the cutoff and most of every letter went with it. -xlinka
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
