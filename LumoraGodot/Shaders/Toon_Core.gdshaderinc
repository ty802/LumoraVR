// Shared body of the toon material. Culling is a compile-time render_mode in Godot 4, so the surface
// ships as three thin variant shaders that differ only in that line and pull the shading in from here.
// Keeping one copy of the body is the point: a fix to the ramp must not have to be made three times.
// -xlinka

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;
uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);

uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform float normal_scale : hint_range(0.0, 4.0) = 1.0;

// Wrapped N.L drives U, so a ramp texture replaces the two-tone fallback entirely and its texels are
// the light response itself.
uniform sampler2D shadow_ramp : source_color, filter_linear, repeat_disable;
uniform bool use_shadow_ramp = false;
uniform vec4 light_tint : source_color = vec4(1.0);
uniform vec4 shadow_tint : source_color = vec4(0.42, 0.45, 0.58, 1.0);
uniform float shadow_boundary : hint_range(0.0, 1.0) = 0.5;
uniform float shadow_softness : hint_range(0.001, 0.5) = 0.05;

uniform vec4 rim_color : source_color = vec4(0.0);
uniform float rim_power : hint_range(0.5, 16.0) = 4.0;
uniform float rim_boundary : hint_range(0.0, 1.0) = 0.0;

// Indexed by the view-space normal, so the highlight follows the camera.
uniform sampler2D matcap_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_matcap_texture = false;
uniform float matcap_strength : hint_range(0.0, 1.0) = 1.0;
uniform bool matcap_additive = true;

uniform vec4 emission_color : source_color = vec4(0.0);
uniform sampler2D emission_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_emission_map = false;
uniform float emission_strength : hint_range(0.0, 16.0) = 1.0;

uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float smoothness : hint_range(0.0, 1.0) = 0.0;
// glTF packing, which is what imports carry: metallic in B, roughness in G.
uniform sampler2D metallic_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_metallic_map = false;

// 0 opaque, 1 cutout, 2 blend.
uniform int alpha_mode : hint_range(0, 2) = 0;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;

void vertex() {
	UV = UV * texture_scale + texture_offset;
}

void fragment() {
	vec4 base = albedo_color;
	if (use_albedo_texture) {
		base *= texture(albedo_texture, UV);
	}
	if (alpha_mode == 1 && base.a < alpha_cutoff) {
		discard;
	}

	// NORMAL, TANGENT and BINORMAL are all view-space here, which is also what the matcap lookup and
	// the rim fresnel want, so nothing has to be transformed back.
	vec3 n = NORMAL;
	if (use_normal_map) {
		vec3 tn = texture(normal_map, UV).xyz * 2.0 - 1.0;
		tn.xy *= normal_scale;
		n = normalize(TANGENT * tn.x + BINORMAL * tn.y + NORMAL * tn.z);
		NORMAL = n;
	}

	vec3 emission = emission_color.rgb * emission_strength;
	if (use_emission_map) {
		emission *= texture(emission_map, UV).rgb;
	}

	if (use_matcap_texture) {
		vec3 cap = texture(matcap_texture, n.xy * 0.5 + 0.5).rgb;
		if (matcap_additive) {
			emission += cap * matcap_strength;
		} else {
			base.rgb *= mix(vec3(1.0), cap, matcap_strength);
		}
	}

	// The rim rides on emission so it survives the shadow side, which is the entire point of it.
	float fresnel = 1.0 - clamp(dot(n, normalize(VIEW)), 0.0, 1.0);
	float rim = smoothstep(rim_boundary, 1.0, pow(fresnel, rim_power)) * rim_color.a;
	emission += rim_color.rgb * rim;

	float m = metallic;
	float s = smoothness;
	if (use_metallic_map) {
		vec4 packed = texture(metallic_map, UV);
		m = packed.b;
		s = 1.0 - packed.g;
	}

	ALBEDO = base.rgb;
	ALPHA = alpha_mode == 2 ? base.a : 1.0;
	EMISSION = emission;
	METALLIC = m;
	ROUGHNESS = clamp(1.0 - s, 0.0, 1.0);
}

void light() {
	vec3 n = normalize(NORMAL);
	// Wrapped N.L (half-lambert) rather than raw: the terminator lands where shadow_boundary puts it
	// instead of always on the exact silhouette, and the shadow side keeps enough range for a ramp to
	// have something to say about it.
	float wrapped = dot(n, normalize(LIGHT)) * 0.5 + 0.5;
	float band = smoothstep(shadow_boundary - shadow_softness, shadow_boundary + shadow_softness, wrapped);

	vec3 shade;
	if (use_shadow_ramp) {
		shade = texture(shadow_ramp, vec2(wrapped, 0.5)).rgb * light_tint.rgb;
	} else {
		shade = mix(shadow_tint.rgb, light_tint.rgb, band);
	}

	// ATTENUATION scales the WHOLE term, shadow tint included. Folding the tint in ahead of it instead
	// would have a light that is out of range or fully shadow-mapped still adding its shadow colour to
	// the surface, so every distant lamp in a world would quietly brighten every avatar in it. -xlinka
	//
	// No ALBEDO and a divide by PI in the write, both deliberate and both measured against a
	// StandardMaterial3D lambert under the same light: Godot multiplies DIFFUSE_LIGHT by ALBEDO after
	// this function returns, and hands LIGHT_COLOR in already scaled by PI. Writing ALBEDO here squares
	// it and skipping the divide comes out PI times hot, which is an avatar glowing next to the wall it
	// is standing against. -xlinka
	DIFFUSE_LIGHT += LIGHT_COLOR * shade * ATTENUATION * (1.0 - METALLIC) / PI;

	// One flat highlight rather than a falloff, gated by the same band so it cannot float on the
	// shadow side of the terminator. Same PI convention as the diffuse write.
	float gloss = clamp(1.0 - ROUGHNESS, 0.0, 1.0);
	vec3 half_dir = normalize(normalize(LIGHT) + normalize(VIEW));
	float lobe = pow(clamp(dot(n, half_dir), 0.0, 1.0), exp2(gloss * 9.0 + 1.0));
	float highlight = smoothstep(0.25, 0.35, lobe) * gloss;
	SPECULAR_LIGHT += LIGHT_COLOR * highlight * mix(vec3(1.0), ALBEDO, METALLIC) * ATTENUATION * band / PI;
}
