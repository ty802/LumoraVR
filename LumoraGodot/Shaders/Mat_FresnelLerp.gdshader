shader_type spatial;
// Alpha with a depth pre-pass: a fully opaque set of colours still sorts and occludes correctly,
// while a translucent one still blends.
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_back;

// Two complete fresnel sets, crossfaded by lerp_amount. Set 0 at 0, set 1 at 1.
uniform float lerp_amount : hint_range(0.0, 1.0) = 0.0;
// Optional per-pixel crossfade on top of the scalar. Read off the red channel and multiplied in, so a
// black texture pins the surface to set 0 regardless of the scalar.
uniform sampler2D lerp_texture : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_lerp_texture = false;
uniform vec2 lerp_texture_scale = vec2(1.0);
uniform vec2 lerp_texture_offset = vec2(0.0);

// Near = facing the viewer, far = at a grazing angle. The exponent shapes how fast one becomes the
// other; 1 is a straight ramp, higher values pin the effect to the silhouette.
uniform float exponent0 : hint_range(0.1, 16.0) = 1.0;
uniform float exponent1 : hint_range(0.1, 16.0) = 1.0;

uniform vec4 near_color0 : source_color = vec4(1.0);
uniform vec4 far_color0 : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 near_color1 : source_color = vec4(0.8, 0.8, 0.8, 1.0);
uniform vec4 far_color1 : source_color = vec4(0.2, 0.2, 0.2, 1.0);

uniform sampler2D near_texture0 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_near_texture0 = false;
uniform sampler2D far_texture0 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_far_texture0 = false;
uniform sampler2D near_texture1 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_near_texture1 = false;
uniform sampler2D far_texture1 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_far_texture1 = false;

uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);
uniform bool use_vertex_color = false;

varying vec2 lerp_uv;

void vertex() {
	lerp_uv = UV * lerp_texture_scale + lerp_texture_offset;
	UV = UV * texture_scale + texture_offset;
}

void fragment() {
	float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float rim = 1.0 - facing;

	vec4 near0 = near_color0;
	if (use_near_texture0) { near0 *= texture(near_texture0, UV); }
	vec4 far0 = far_color0;
	if (use_far_texture0) { far0 *= texture(far_texture0, UV); }
	vec4 set0 = mix(near0, far0, pow(rim, exponent0));

	vec4 near1 = near_color1;
	if (use_near_texture1) { near1 *= texture(near_texture1, UV); }
	vec4 far1 = far_color1;
	if (use_far_texture1) { far1 *= texture(far_texture1, UV); }
	vec4 set1 = mix(near1, far1, pow(rim, exponent1));

	float t = lerp_amount;
	if (use_lerp_texture) {
		t *= texture(lerp_texture, lerp_uv).r;
	}

	vec4 out_color = mix(set0, set1, clamp(t, 0.0, 1.0));
	if (use_vertex_color) {
		out_color *= COLOR;
	}

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
