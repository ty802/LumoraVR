shader_type spatial;
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_disabled;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform bool use_vertex_color = false;

uniform bool alpha_clip = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;

// A stereo frame holds both eyes in one picture, so each eye gets its own window into the texture.
// The pair REPLACES uv_scale/uv_offset while it is on: one transform, not two that multiply. -xlinka
uniform bool stereo_texture_transform = false;
uniform vec2 left_eye_texture_scale = vec2(1.0);
uniform vec2 left_eye_texture_offset = vec2(0.0);
uniform vec2 right_eye_texture_scale = vec2(1.0);
uniform vec2 right_eye_texture_offset = vec2(0.0);

void fragment() {
	// The eye split has to be a fragment decision because VIEW_INDEX is per view. Moving the plain
	// transform down here with it costs nothing: an affine UV map interpolates to the same numbers
	// whether it is applied per vertex or per pixel. Outside multiview VIEW_INDEX is 0, so a flat
	// screen shows the left eye. -xlinka
	vec2 uv = UV * uv_scale + uv_offset;
	if (stereo_texture_transform) {
		uv = VIEW_INDEX == 0
			? UV * left_eye_texture_scale + left_eye_texture_offset
			: UV * right_eye_texture_scale + right_eye_texture_offset;
	}

	vec4 tex = use_albedo_texture ? texture(albedo_texture, uv) : vec4(1.0);
	vec4 out_color = albedo_color * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

	if (alpha_clip) {
		float alpha_width = max(fwidth(out_color.a), 0.001);
		out_color.a *= smoothstep(alpha_cutoff - alpha_width, alpha_cutoff + alpha_width, out_color.a);
	}

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
