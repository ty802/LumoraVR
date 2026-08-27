shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_texture = false;

uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform float normal_scale : hint_range(0.0, 4.0) = 1.0;

// Both maps are read off one channel each so a packed occlusion/roughness/metallic texture works when
// plugged into both slots (G = roughness, B = metallic, the glTF packing the rest of the engine already
// assumes), and a plain greyscale map works too because its channels are equal. -xlinka
uniform sampler2D roughness_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_roughness_map = false;
uniform sampler2D metallic_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_metallic_map = false;

uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float smoothness : hint_range(0.0, 1.0) = 0.25;
uniform vec4 emissive_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

uniform vec3 tiling = vec3(1.0);
uniform vec3 offset = vec3(0.0);
// Exponent on the |normal| weights. 1 is a wide mush, 8 is a hard seam. 4 reads as a clean blend.
uniform float blend_sharpness : hint_range(1.0, 16.0) = 4.0;
// Project in world space (texture stays put while the object moves) or object space (texture rides
// along with it). World is the usual choice for terrain and level geometry.
uniform bool world_space = true;

uniform bool alpha_clip = false;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;

varying vec3 tri_pos;
varying vec3 tri_normal;

void vertex() {
	if (world_space) {
		tri_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		tri_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	} else {
		tri_pos = VERTEX;
		tri_normal = normalize(NORMAL);
	}
	tri_pos = tri_pos * tiling + offset;
}

vec3 blend_weights(vec3 n) {
	vec3 w = pow(abs(n), vec3(blend_sharpness));
	return w / max(w.x + w.y + w.z, 0.0001);
}

vec4 tri_sample(sampler2D tex, vec3 p, vec3 w) {
	return texture(tex, p.zy) * w.x + texture(tex, p.xz) * w.y + texture(tex, p.xy) * w.z;
}

// Whiteout blend: each plane's tangent normal is folded onto the surface normal before the weighted
// sum, which keeps detail on the two planes that are not facing the camera instead of washing it out
// the way a straight per-channel average does.
vec3 tri_normal_sample(sampler2D tex, vec3 p, vec3 n, vec3 w, float scale) {
	vec3 tx = texture(tex, p.zy).xyz * 2.0 - 1.0;
	vec3 ty = texture(tex, p.xz).xyz * 2.0 - 1.0;
	vec3 tz = texture(tex, p.xy).xyz * 2.0 - 1.0;
	tx.xy *= scale;
	ty.xy *= scale;
	tz.xy *= scale;
	tx = vec3(tx.xy + n.zy, abs(tx.z) * n.x);
	ty = vec3(ty.xy + n.xz, abs(ty.z) * n.y);
	tz = vec3(tz.xy + n.xy, abs(tz.z) * n.z);
	return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

void fragment() {
	vec3 n = normalize(tri_normal);
	vec3 w = blend_weights(n);

	vec4 base = albedo_color;
	if (use_albedo_texture) {
		base *= tri_sample(albedo_texture, tri_pos, w);
	}
	if (alpha_clip && base.a < alpha_cutoff) {
		discard;
	}

	ALBEDO = base.rgb;
	ALPHA = base.a;

	float rough = 1.0 - smoothness;
	if (use_roughness_map) {
		rough *= tri_sample(roughness_map, tri_pos, w).g;
	}
	float metal = metallic;
	if (use_metallic_map) {
		metal *= tri_sample(metallic_map, tri_pos, w).b;
	}
	ROUGHNESS = clamp(rough, 0.0, 1.0);
	METALLIC = clamp(metal, 0.0, 1.0);
	EMISSION = emissive_color.rgb * emissive_color.a;

	if (use_normal_map) {
		// The perturbed normal comes out in the projection space (world or object), so it has to be
		// carried back to view space by hand; there is no tangent frame to hand to NORMAL_MAP here.
		vec3 pn = tri_normal_sample(normal_map, tri_pos, n, w, normal_scale);
		mat3 to_view = world_space ? mat3(VIEW_MATRIX) : mat3(VIEW_MATRIX * MODEL_MATRIX);
		NORMAL = normalize(to_view * pn);
	}
}
