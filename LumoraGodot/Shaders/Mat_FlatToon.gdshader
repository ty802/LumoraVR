shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back, diffuse_lambert, specular_disabled;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;
uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);

// Colour the surface takes where a light does not reach it. Multiplies albedo, so a neutral grey
// darkens and a blue-grey gives the usual cool shadow.
uniform vec4 shade_color : source_color = vec4(0.4, 0.42, 0.55, 1.0);

// Number of flat bands between shadow and light. 1 gives the hard two-tone cel look.
uniform int steps : hint_range(1, 8) = 2;
// Where the first band breaks, and how wide the crossfade at each break is. Softness is in
// wrapped-N.L units, not pixels, so it does not change with distance.
uniform float shade_threshold : hint_range(0.0, 1.0) = 0.5;
uniform float shade_softness : hint_range(0.001, 0.5) = 0.03;

// Optional ramp: the wrapped N.L drives U, so the texture replaces the step quantiser entirely.
uniform sampler2D ramp_texture : source_color, filter_linear, repeat_disable;
uniform bool use_ramp_texture = false;

uniform vec4 rim_color : source_color = vec4(0.0);
uniform float rim_power : hint_range(0.5, 16.0) = 4.0;

uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform float normal_scale : hint_range(0.0, 4.0) = 1.0;

uniform bool alpha_clip = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;

void vertex() {
	UV = UV * texture_scale + texture_offset;
}

void fragment() {
	vec4 base = albedo_color;
	if (use_albedo_texture) {
		base *= texture(albedo_texture, UV);
	}
	if (alpha_clip && base.a < alpha_cutoff) {
		discard;
	}

	ALBEDO = base.rgb;
	ALPHA = base.a;
	METALLIC = 0.0;
	ROUGHNESS = 1.0;

	if (use_normal_map) {
		vec3 tn = texture(normal_map, UV).xyz * 2.0 - 1.0;
		tn.xy *= normal_scale;
		NORMAL = normalize(TANGENT * tn.x + BINORMAL * tn.y + NORMAL * tn.z);
	}

	// Rim rides on emission so it survives being in shadow, which is the point of a rim light.
	float rim = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), rim_power);
	EMISSION = rim_color.rgb * rim * rim_color.a;
}

void light() {
	// Wrapped N.L (half-lambert) rather than raw, so the terminator lands somewhere adjustable instead
	// of always on the exact silhouette; shade_threshold then picks where the first band breaks.
	float ndl = dot(normalize(NORMAL), normalize(LIGHT)) * 0.5 + 0.5;

	float band;
	if (use_ramp_texture) {
		band = texture(ramp_texture, vec2(ndl, 0.5)).r;
	} else {
		float t = clamp((ndl - shade_threshold) / max(1.0 - shade_threshold, 0.0001), 0.0, 1.0);
		float f = float(steps);
		// floor(t*f)/f is the flat band; the smoothstep re-softens only the break itself so the edge
		// antialiases instead of crawling.
		float stepped = floor(t * f) / f;
		float frac_t = fract(t * f);
		band = stepped + smoothstep(0.5 - shade_softness * f, 0.5 + shade_softness * f, frac_t) / f;
		band = clamp(band, 0.0, 1.0);
	}

	vec3 lit = mix(shade_color.rgb, vec3(1.0), band);
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * lit * ATTENUATION;
}
