// Additive variant of Unlit.gdshader. Blend mode is a compile-time render_mode in Godot, not a
// uniform, so additive unlit materials swap to this shader (MaterialAssetHook.SetBlendMode). -xlinka
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform bool use_vertex_color = false;

uniform bool alpha_clip = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;

void vertex() {
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	vec4 tex = use_albedo_texture ? texture(albedo_texture, UV) : vec4(1.0);
	vec4 out_color = albedo_color * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

	// Additive: alpha scales the contribution (black adds nothing).
	ALBEDO = out_color.rgb * out_color.a;
	ALPHA = 1.0;
}
