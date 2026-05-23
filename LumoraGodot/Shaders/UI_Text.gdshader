shader_type spatial;
// depth_test_disabled: matches UI_Unlit — UI is a logical 2D layer above world geometry,
// ordering inside the UI is resolved by render_priority not depth. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled;

// UI text shader for rasterized-coverage atlases. Godot's runtime TTF rasterizer
// outputs LA8 (luminance + alpha), which FontAssetHook stores as coverage in the
// alpha channel. The shader does fwidth-based edge AA — coverage transitions over
// ~1 screen pixel regardless of display size, so small text stays legible. - xlinka

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : filter_linear, repeat_disable;
uniform bool use_albedo_texture = true;
uniform bool use_vertex_color = true;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);

// Kept for future SDF/MSDF migration if we ever bake an offline atlas. Unused in
// the current coverage path. - xlinka
uniform float pixel_range = 4.0;

uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.01;
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

void vertex() {
	local_pos = VERTEX.xy;
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	if (rect_clip) {
		if (local_pos.x < rect.x || local_pos.y < rect.y || local_pos.x > rect.z || local_pos.y > rect.w) {
			discard;
		}
	}

	vec4 out_color = albedo_color;
	if (use_vertex_color) {
		out_color *= COLOR;
	}

	float coverage = 1.0;
	if (use_albedo_texture) {
		coverage = texture(albedo_texture, UV).a;
		// fwidth(coverage) = how fast coverage changes per screen pixel near this fragment.
		// smoothstep across that range gives a ~1-screen-pixel-wide soft edge regardless
		// of how zoomed in/out the text is. - xlinka
		float aa = max(fwidth(coverage), 0.0001);
		coverage = smoothstep(0.5 - aa, 0.5 + aa, coverage);
	}
	out_color.a *= coverage;

	if (alpha_clip && out_color.a < alpha_cutoff) {
		discard;
	}

	if ((color_mask % 2) == 0) out_color.r = 0.0;
	if (((color_mask / 2) % 2) == 0) out_color.g = 0.0;
	if (((color_mask / 4) % 2) == 0) out_color.b = 0.0;
	if (((color_mask / 8) % 2) == 0) out_color.a = 0.0;

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
