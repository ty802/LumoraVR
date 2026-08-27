shader_type spatial;
// Opaque variant. Nothing here writes ALPHA, so Godot keeps the material in the opaque pass and it
// sorts and depth-writes like any other solid surface. Mat_PBS_VertexColorTransparent is the same
// body plus the vertex alpha; the hook picks between the two on UseVertexAlpha. -xlinka
render_mode blend_mix, cull_back;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;
uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);

uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform float normal_scale : hint_range(0.0, 4.0) = 1.0;

// G = roughness, B = metallic, matching the glTF packing the rest of the engine assumes. A plain
// greyscale map also works because its channels are equal.
uniform sampler2D metallic_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_metallic_map = false;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float smoothness : hint_range(0.0, 1.0) = 0.25;
uniform vec4 emissive_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

void vertex() {
	UV = UV * texture_scale + texture_offset;
}

void fragment() {
	vec4 base = albedo_color * COLOR;
	if (use_albedo_texture) {
		base *= texture(albedo_texture, UV);
	}

	ALBEDO = base.rgb;

	float rough = 1.0 - smoothness;
	float metal = metallic;
	if (use_metallic_map) {
		vec4 mr = texture(metallic_map, UV);
		rough *= mr.g;
		metal *= mr.b;
	}
	ROUGHNESS = clamp(rough, 0.0, 1.0);
	METALLIC = clamp(metal, 0.0, 1.0);
	EMISSION = emissive_color.rgb * emissive_color.a;

	if (use_normal_map) {
		vec3 tn = texture(normal_map, UV).xyz * 2.0 - 1.0;
		tn.xy *= normal_scale;
		NORMAL = normalize(TANGENT * tn.x + BINORMAL * tn.y + NORMAL * tn.z);
	}
}
