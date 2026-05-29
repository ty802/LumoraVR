shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled;

uniform vec4 front_color : source_color = vec4(1.0);
uniform vec4 behind_color : source_color = vec4(0.5, 0.5, 0.5, 0.5);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;

uniform vec2 front_uv_scale = vec2(1.0);
uniform vec2 front_uv_offset = vec2(0.0);
uniform vec2 behind_uv_scale = vec2(1.0);
uniform vec2 behind_uv_offset = vec2(0.0);
uniform bool use_vertex_color = true;

void fragment() {
	bool front = FRONT_FACING;
	vec2 uv = UV * (front ? front_uv_scale : behind_uv_scale) + (front ? front_uv_offset : behind_uv_offset);
	vec4 tex = use_albedo_texture ? texture(albedo_texture, uv) : vec4(1.0);
	vec4 out_color = (front ? front_color : behind_color) * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
