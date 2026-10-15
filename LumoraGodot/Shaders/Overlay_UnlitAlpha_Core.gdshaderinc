// Overlay unlit, blended. The body the three cull variants share.
//
// Never depth-tested and never depth-written, so it stays on top of the scene. It reads the opaque pass's
// depth back to tell what is in front from what is behind: the front colour where nothing covers it, the
// behind colour where the scene does, which is how a handle stays findable through the object it sits in
// and still reads as behind it. Surfaces that never wrote depth (other overlays, the sky) do not cover.
// The colour is clamped to one: a blended overlay paints over the scene and never feeds the glow pass as
// light. -xlinka

uniform vec4 front_color : source_color = vec4(1.0);
uniform vec4 behind_color : source_color = vec4(0.5, 0.5, 0.5, 0.5);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;

uniform vec2 front_uv_scale = vec2(1.0);
uniform vec2 front_uv_offset = vec2(0.0);
uniform vec2 behind_uv_scale = vec2(1.0);
uniform vec2 behind_uv_offset = vec2(0.0);
uniform bool use_vertex_color = true;

// Metres of slack on the occlusion test, so a surface resting on the geometry behind it does not flicker
// between the two colours.
uniform float depth_bias : hint_range(0.0, 0.5) = 0.005;

uniform sampler2D scene_depth : hint_depth_texture, filter_nearest;

void fragment() {
	float raw = texture(scene_depth, SCREEN_UV).r;
	vec4 unprojected = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	float scene_dist = -(unprojected.z / unprojected.w);
	bool behind = -VERTEX.z > scene_dist + depth_bias;

	vec2 uv = UV * (behind ? behind_uv_scale : front_uv_scale) + (behind ? behind_uv_offset : front_uv_offset);
	vec4 tex = use_albedo_texture ? texture(albedo_texture, uv) : vec4(1.0);
	vec4 out_color = (behind ? behind_color : front_color) * tex;

	if (use_vertex_color) {
		out_color *= COLOR;
	}

	ALBEDO = clamp(out_color.rgb, vec3(0.0), vec3(1.0));
	ALPHA = clamp(out_color.a, 0.0, 1.0);
}
