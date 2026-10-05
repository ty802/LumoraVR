// Shared body of the fur material: shell fur over a Blinn-Phong skin.
//
// The surface is drawn twenty one times. Layer 0 is the skin, the mesh as it is. Layers 0.05 to 1.0 are
// shells, the same mesh pushed out along its normals by fur_length * layer, each one cut down to the
// strands a noise texture leaves standing. Stacked, the shells read as fur. Godot has no multi-pass
// shader, so each shell is its own material chained on the one before (next_pass), all sharing the
// shell shader and differing only in fur_layer, which the material hook hands each one as it builds the
// chain.
//
// Lighting is the classic Blinn-Phong the fur was authored under, not PBR: a lambert diffuse plus a
// specular lobe of power shininess * 128 times gloss, tinted by the specular colour, with no environment
// reflection. The shells add a rim glow and darken toward the skin so the root of the fur sits in its own
// shadow.
//
// Defines: FUR_SHELL for the shell passes. -xlinka

uniform vec4 albedo_color : source_color = vec4(1.0);
// RGB colour, ALPHA how far the fur grows here: a shell is dropped wherever its layer is past both this
// alpha and SKIN_ALPHA, so a dark alpha keeps the fur short and never bald.
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;
uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);

uniform vec4 specular_color : source_color = vec4(1.0);
uniform float shininess = 8.0;
uniform float gloss = 1.0;
uniform vec4 rim_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float rim_power = 4.0;

// Applied to the skin only, read at the mesh UV.
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;

// Red is where a strand stands; the same texture offset by 0.1 tints strands lighter or darker. Data,
// not colour, so no sRGB decode. Unbound reads as white: every shell solid.
uniform sampler2D noise_texture : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_noise_texture = false;

// Length in the mesh's own units, so the fur scales with the object.
uniform float fur_length = 0.05;
// Multiplies the length the shells actually reach; the forces below ignore it.
uniform float fur_hardness = 1.0;
// Noise tiling: higher is finer strands.
uniform float fur_thinness = 2.0;
// How much darker the inner shells are than the tips.
uniform float fur_shading = 0.25;
// How far the noise tints strands away from the base colour.
uniform float fur_coloring = 0.1;
uniform float alpha_cutoff = 0.2;

// Bends the fur, growing with the square of the layer so roots stay put and tips swing. Global is a world
// direction (gravity, wind), local goes through the projection as the fur was authored (a push that
// follows the view). Each clamped to -1..1.
uniform vec4 force_global = vec4(0.0);
uniform vec4 force_local = vec4(0.0);

// Which pass this is: 0 the skin, 0.05 to 1 the shells. Set once per chained material, never by a member.
uniform float fur_layer = 0.0;

// Where fur is cut regardless of the texture's alpha: no shell past half the length grows where alpha is
// below this. Fixed, as the material never exposed it.
const float SKIN_ALPHA = 0.5;

varying float lit_gloss;

void vertex() {
#ifdef FUR_SHELL
	VERTEX += NORMAL * fur_length * fur_layer * fur_hardness;
	vec4 fg = clamp(force_global, -1.0, 1.0);
	vec4 fl = clamp(force_local, -1.0, 1.0);
	if (fg != vec4(0.0) || fl != vec4(0.0)) {
		vec3 push = (inverse(MODEL_MATRIX) * fg).xyz + (PROJECTION_MATRIX * fl).xyz;
		VERTEX += push * fur_layer * fur_layer * fur_length;
	}
#endif
}

void fragment() {
	vec2 uv = UV * texture_scale + texture_offset;
	vec4 tex = use_albedo_texture ? texture(albedo_texture, uv) : vec4(1.0);
	vec3 albedo = tex.rgb * albedo_color.rgb;

#ifdef FUR_SHELL
	if (fur_layer > max(tex.a, SKIN_ALPHA)) {
		discard;
	}
	vec3 noise = use_noise_texture ? texture(noise_texture, uv * fur_thinness).rgb : vec3(1.0);
	if (noise.r < alpha_cutoff) {
		discard;
	}
	lit_gloss = gloss * noise.r;

	float rim = 1.0 - clamp(dot(normalize(VIEW), normalize(NORMAL)), 0.0, 1.0);
	EMISSION = rim_color.rgb * pow(rim, rim_power);

	vec3 tint = use_noise_texture ? texture(noise_texture, uv * fur_thinness + 0.1).rgb : vec3(1.0);
	albedo += (tint * 2.0 - 1.0) * 0.5 * tex.rgb * fur_coloring;
	albedo -= (1.0 - fur_layer) * fur_shading * tex.rgb;
	// Strong shading can take the tint past black. Negative light in the HDR buffer poisons glow and
	// temporal filters, so it stops at black.
	ALBEDO = max(albedo, vec3(0.0));
	// Any ALPHA write puts the shells in the transparent pass, which is the point: there they cast no
	// shadow (twenty more shadow draws per light), while depth_draw_always keeps them occluding each
	// other and everything behind. The cut strands are already discarded, so 1 blends as opaque.
	ALPHA = 1.0;
#else
	lit_gloss = gloss;
	ALBEDO = albedo;
	if (use_normal_map) {
		NORMAL_MAP = texture(normal_map, UV).rgb;
	}
#endif

	// No environment reflection: zero F0, so Godot's indirect specular adds nothing.
	METALLIC = 0.0;
	SPECULAR = 0.0;
	ROUGHNESS = 1.0;
}

void light() {
	vec3 n = normalize(NORMAL);
	vec3 l = normalize(LIGHT);
	vec3 h = normalize(l + normalize(VIEW));
	// LIGHT_COLOR carries a factor of PI for physically based models; Blinn-Phong was authored without it.
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI;
	DIFFUSE_LIGHT += radiance * max(dot(n, l), 0.0);
	float spec = pow(max(dot(n, h), 1e-6), max(shininess * 128.0, 1e-4)) * lit_gloss;
	SPECULAR_LIGHT += radiance * specular_color.rgb * spec;
}
