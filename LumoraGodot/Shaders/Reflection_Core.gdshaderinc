// Rectangular mirrors sample surface UVs from a plane-aligned capture. Legacy portals use
// screen UVs. VIEW_INDEX selects the eye texture in VR; each capture is already lit.

uniform sampler2D reflection_texture : source_color, filter_linear, repeat_disable;
uniform bool use_reflection_texture = false;
uniform sampler2D reflection_texture_right : source_color, filter_linear, repeat_disable;
uniform bool use_reflection_texture_right = false;

uniform bool mirror_sampling = true;
uniform bool surface_uv_sampling = false;

uniform vec4 tint_color : source_color = vec4(1.0);

// The normal map does not shade; it moves the sample. distort is authored 0..2 and scaled by a tenth,
// which keeps 2 as a visible wobble rather than a smear across the whole screen.
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform vec2 normal_map_scale = vec2(1.0);
uniform vec2 normal_map_offset = vec2(0.0);
uniform float distort : hint_range(0.0, 2.0) = 0.0;

uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
// Set by the material hook from the BlendMode member. 0 Opaque, 1 Cutout, 2 Alpha, 3 Transparent,
// 4 Additive, 5 Multiply. Blending itself is a compile-time render_mode chosen by the variant.
uniform int blend_mode = 0;

// Viewport textures have no mip chain. One bilinear lookup skips reflected texels when
// the glass gets smaller on screen, making fine details sparkle during movement.
// Integrate over the screen pixel's footprint while keeping close-up samples sharp.
vec4 sample_reflection(sampler2D source, vec2 uv) {
	if (!surface_uv_sampling) {
		return texture(source, uv);
	}
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	vec2 pixels = vec2(textureSize(source, 0));
	float footprint = max(length(dx * pixels), length(dy * pixels));
	vec4 centre = texture(source, uv);
	if (footprint <= 1.0) {
		return centre;
	}
	vec2 a = 0.375 * dx + 0.125 * dy;
	vec2 b = 0.125 * dx - 0.375 * dy;
	vec4 filtered = (texture(source, uv + a) + texture(source, uv - a)
		+ texture(source, uv + b) + texture(source, uv - b)) * 0.25;
	return mix(centre, filtered, smoothstep(1.0, 2.0, footprint));
}

void fragment() {
	vec2 uv = surface_uv_sampling ? UV : SCREEN_UV;

	if (use_normal_map) {
		vec3 n = texture(normal_map, UV * normal_map_scale + normal_map_offset).xyz * 2.0 - 1.0;
		uv += n.xy * distort * 0.1;
	}

	if (mirror_sampling) {
		uv.x = 1.0 - uv.x;
	}
	uv = clamp(uv, vec2(0.0), vec2(1.0));

	// With nothing rendered into it yet a mirror is black glass, not white: white is what an untextured
	// material looks like and would read as broken rather than as waiting.
	vec4 c = vec4(0.0, 0.0, 0.0, 1.0);
	if (VIEW_INDEX == 1 && use_reflection_texture_right) {
		c = sample_reflection(reflection_texture_right, uv);
	} else if (use_reflection_texture) {
		c = sample_reflection(reflection_texture, uv);
	}
	c *= tint_color;

	if (blend_mode == 1 && c.a < alpha_cutoff) {
		discard;
	}

	ALBEDO = c.rgb;
#ifdef TRANSPARENT_PASS
	ALPHA = c.a;
#endif
}
