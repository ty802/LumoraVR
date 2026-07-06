shader_type spatial;
// Depth-TESTED against the world (walls occlude panels) but never depth-written; panel backings
// (UI_UnlitZWrite) lay down the depth wall. UI-internal ordering stays render_priority. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, cull_front;

uniform vec4 albedo_color : source_color = vec4(1.0);
// Plain bilinear, NO mipmaps, NO aniso. UI textures at glancing viewing angles
// otherwise drop to a higher mip level (because screen-space derivatives grow
// along the foreshortened axis) - rounded-rect sprites lose their alpha shape
// and tinted backgrounds wash toward grey/white. UI is interactive and viewed
// close, mip 0 sampling everywhere matches what the designer painted. - xlinka
uniform sampler2D albedo_texture : source_color, filter_linear, repeat_disable;
uniform bool use_albedo_texture = false;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform bool use_vertex_color = true;

uniform bool alpha_clip = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.01;

uniform bool rect_clip = false;
uniform vec4 rect = vec4(0.0);
// Added to the vertex position before the rect test. For scrolled content whose chunk is moved by a
// transform (not re-tessellated), rect stays fixed in canvas space and this carries the scroll offset,
// so the clip window stays pinned to the viewport while content slides under it. Zero for static UI. -xlinka
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

void vertex() {
	// Scroll offset: move the vertex AND clip against it from the SAME uniform, so a scrolled chunk's
	// position and clip window can never desync (that caused content to leak past the viewport). -xlinka
	VERTEX.xy += clip_offset;
	local_pos = VERTEX.xy;
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	float clip_alpha = rect_clip ? rect_alpha(local_pos, rect) : 1.0;

	vec4 tex = use_albedo_texture ? texture(albedo_texture, UV) : vec4(1.0);
	vec4 out_color = albedo_color * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

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
