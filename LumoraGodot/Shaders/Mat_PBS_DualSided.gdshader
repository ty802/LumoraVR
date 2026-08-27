shader_type spatial;
// Both sides are drawn, so culling is off for the life of this shader. Godot already flips NORMAL for
// back faces under cull_disabled, which is what makes the back side light correctly on its own.
render_mode blend_mix, depth_prepass_alpha, cull_disabled;

uniform vec4 front_albedo_color : source_color = vec4(1.0);
uniform sampler2D front_albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_front_albedo_texture = false;
uniform sampler2D front_normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_front_normal_map = false;
uniform vec4 front_emissive_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform sampler2D front_emissive_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_front_emissive_map = false;

uniform vec4 back_albedo_color : source_color = vec4(1.0);
uniform sampler2D back_albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_back_albedo_texture = false;
uniform sampler2D back_normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_back_normal_map = false;
uniform vec4 back_emissive_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform sampler2D back_emissive_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_back_emissive_map = false;

uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);
uniform float normal_scale : hint_range(0.0, 4.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float smoothness : hint_range(0.0, 1.0) = 0.25;

uniform bool alpha_clip = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;

void vertex() {
	UV = UV * texture_scale + texture_offset;
}

void fragment() {
	bool front = FRONT_FACING;

	vec4 base = front ? front_albedo_color : back_albedo_color;
	if (front && use_front_albedo_texture) {
		base *= texture(front_albedo_texture, UV);
	} else if (!front && use_back_albedo_texture) {
		base *= texture(back_albedo_texture, UV);
	}
	if (alpha_clip && base.a < alpha_cutoff) {
		discard;
	}

	ALBEDO = base.rgb;
	ALPHA = base.a;
	METALLIC = metallic;
	ROUGHNESS = clamp(1.0 - smoothness, 0.0, 1.0);

	vec4 emissive = front ? front_emissive_color : back_emissive_color;
	vec3 emission = emissive.rgb * emissive.a;
	if (front && use_front_emissive_map) {
		emission *= texture(front_emissive_map, UV).rgb;
	} else if (!front && use_back_emissive_map) {
		emission *= texture(back_emissive_map, UV).rgb;
	}
	EMISSION = emission;

	bool has_normal = front ? use_front_normal_map : use_back_normal_map;
	if (has_normal) {
		vec3 tn = front
			? texture(front_normal_map, UV).xyz * 2.0 - 1.0
			: texture(back_normal_map, UV).xyz * 2.0 - 1.0;
		tn.xy *= normal_scale;
		NORMAL = normalize(TANGENT * tn.x + BINORMAL * tn.y + NORMAL * tn.z);
	}
}
