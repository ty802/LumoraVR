shader_type spatial;
// depth_test_disabled: UI is a logical 2D layer, world geometry must never occlude it.
// Ordering inside the UI tree is resolved by render_priority, not by depth. - xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear, repeat_disable;
uniform bool use_albedo_texture = false;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform bool use_vertex_color = true;

uniform bool alpha_clip = true;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.01;

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

	vec4 tex = use_albedo_texture ? texture(albedo_texture, UV) : vec4(1.0);
	vec4 out_color = albedo_color * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

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
