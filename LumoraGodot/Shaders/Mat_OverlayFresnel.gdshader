shader_type spatial;
// Same overlay band as the plain overlay unlit material: additive, never depth-written, never depth-
// tested, both sides drawn. It is meant to sit in the late render queue and stay visible through the
// scene, so ordering is the render priority's job, not the depth buffer's.
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled;

// Near = facing the viewer, far = at a grazing angle.
uniform float exponent : hint_range(0.1, 16.0) = 3.0;
uniform vec4 front_near_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 front_far_color : source_color = vec4(1.0);
uniform vec4 behind_near_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 behind_far_color : source_color = vec4(0.25, 0.25, 0.25, 1.0);

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;
uniform vec2 front_uv_scale = vec2(1.0);
uniform vec2 front_uv_offset = vec2(0.0);
uniform vec2 behind_uv_scale = vec2(1.0);
uniform vec2 behind_uv_offset = vec2(0.0);
uniform bool use_vertex_color = false;

// Metres of slack on the occlusion test. Without it a surface resting exactly on the geometry behind
// it flickers between the two colour sets as depth precision wobbles.
uniform float depth_bias : hint_range(0.0, 0.5) = 0.005;

// The engine fills this one in; it is not a material parameter. Godot 4 dropped the DEPTH_TEXTURE
// builtin, so the opaque pass's depth has to be asked for by hint. Nearest filtering because an
// interpolated depth between two surfaces is a distance nothing is actually at.
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest;

void fragment() {
	// The depth test is off, so "in front" and "behind" cannot come from the rasteriser. Read the
	// opaque pass's depth back instead and compare view-space distances: that is a real occlusion
	// answer, unlike splitting on face orientation. Surfaces that never wrote depth (other blended
	// overlays, the sky) count as not occluding, which is the behaviour you want here. -xlinka
	float raw = texture(scene_depth, SCREEN_UV).r;
	vec4 unprojected = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	float scene_dist = -(unprojected.z / unprojected.w);
	float frag_dist = -VERTEX.z;
	bool behind = frag_dist > scene_dist + depth_bias;

	vec2 uv = UV * (behind ? behind_uv_scale : front_uv_scale) + (behind ? behind_uv_offset : front_uv_offset);
	vec4 tex = use_albedo_texture ? texture(albedo_texture, uv) : vec4(1.0);

	float rim = 1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float f = pow(rim, exponent);

	vec4 near_color = behind ? behind_near_color : front_near_color;
	vec4 far_color = behind ? behind_far_color : front_far_color;
	vec4 out_color = mix(near_color, far_color, f) * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
