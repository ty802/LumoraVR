// Shared body of the Fresnel material: the colour swings with the viewing angle.
//
//     fresnel = pow(1 - abs(dot(normal, viewDir)), exponent)
//     fresnel = pow(fresnel, gamma_curve)
//     colour  = mix(near, far, fresnel)
//
// NEAR AND FAR ARE ABOUT THE NORMAL, NOT DISTANCE. Facing the surface head on the dot is 1, the fresnel
// is 0, and you get NEAR. At a grazing angle the dot falls to 0, the fresnel rises to 1, and you get
// FAR. Reading these as "close to the camera" and "far from it" inverts the material.
//
// The abs() is what makes it work on a back face: without it a surface seen from behind has a negative
// dot, the fresnel goes past 1, and the pow of a negative base is undefined.
//
// UNLIT. The two colours ARE the answer; shading them would tint a rim effect by whatever light
// happens to be in the room. -xlinka
//
// Defines a variant can set before including, for the render states the shipped files do not cover:
//   DEPTH_OFFSET  writes a biased depth, see DepthOffset.gdshaderinc
//   ZTEST_NEVER   the depth test that never passes, so nothing draws
// -xlinka

#include "res://Shaders/PolarUV.gdshaderinc"
#include "res://Shaders/VertexColorSpace.gdshaderinc"
#ifdef DEPTH_OFFSET
#include "res://Shaders/DepthOffset.gdshaderinc"
#endif

uniform float exponent = 1.0;
uniform float gamma_curve = 1.0;
uniform bool use_vertex_color = false;

// Defaults: far is BLACK and near is WHITE, so an unconfigured Fresnel reads as a white surface
// darkening off toward its silhouette.
uniform vec4 near_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 far_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

uniform sampler2D near_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_near_texture = false;
uniform vec2 near_texture_scale = vec2(1.0);
uniform vec2 near_texture_offset = vec2(0.0);

uniform sampler2D far_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_far_texture = false;
uniform vec2 far_texture_scale = vec2(1.0);
uniform vec2 far_texture_offset = vec2(0.0);

// Wraps both colour textures around the centre of the UV square, each under its own tiling. The mask and
// the normal map keep reading the plain UV.
uniform bool polar_mapping = false;
uniform float polar_power = 1.0;

uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform float normal_scale : hint_range(0.0, 4.0) = 1.0;

// The mask folds RGB to a single factor and multiplies by its own alpha: (r + g + b) / 3 * a.
// 0 MultiplyAlpha, 1 AlphaClip - and it only applies when a mask is bound.
uniform sampler2D mask_texture : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_mask_texture = false;
uniform vec2 mask_scale = vec2(1.0);
uniform vec2 mask_offset = vec2(0.0);
uniform int mask_mode = 0;

uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
// Set by the material hook from the BlendMode member. 0 Opaque, 1 Cutout, 2 Alpha, 3 Transparent,
// 4 Additive, 5 Multiply. Blending itself is a compile-time render_mode, so this only decides the
// cutout discard and whether alpha is carried through at all.
uniform int blend_mode = 0;

void vertex() {
	COLOR = vertex_color_encode(COLOR);
}

void fragment() {
#ifdef ZTEST_NEVER
	discard;
#endif
	vec3 n = normalize(NORMAL);

	if (use_normal_map) {
		vec3 tn = texture(normal_map, UV).xyz * 2.0 - 1.0;
		tn.xy *= normal_scale;
		n = normalize(TANGENT * tn.x + BINORMAL * tn.y + NORMAL * tn.z);
	}

	// NORMAL and VIEW are both view space here, not world space. The dot of two vectors is unchanged
	// by rotating both, so the angle - and the fresnel - come out the same in either space.
	float f = pow(1.0 - abs(dot(n, normalize(VIEW))), exponent);
	f = pow(f, gamma_curve);

	vec4 near = near_color;
	vec4 far = far_color;
	if (polar_mapping) {
		vec2 polar = polar_uv(UV, polar_power);
		vec2 grad_x;
		vec2 grad_y;
		if (use_near_texture) {
			vec2 p = polar_transform(polar, near_texture_scale, near_texture_offset, grad_x, grad_y);
			near *= polar_sample(near_texture, p, grad_x, grad_y);
		}
		if (use_far_texture) {
			vec2 p = polar_transform(polar, far_texture_scale, far_texture_offset, grad_x, grad_y);
			far *= polar_sample(far_texture, p, grad_x, grad_y);
		}
	} else {
		if (use_near_texture) {
			near *= texture(near_texture, UV * near_texture_scale + near_texture_offset);
		}
		if (use_far_texture) {
			far *= texture(far_texture, UV * far_texture_scale + far_texture_offset);
		}
	}

	vec4 col = mix(near, far, f);
	if (use_vertex_color) {
		col *= vertex_color_decode(COLOR);
	}

	if (use_mask_texture) {
		vec4 m = texture(mask_texture, UV * mask_scale + mask_offset);
		float factor = (m.r + m.g + m.b) * 0.3333333 * m.a;
		if (mask_mode == 1) {
			if (factor - alpha_cutoff <= 0.0) {
				discard;
			}
		} else {
			col.a *= factor;
		}
	}

	if (blend_mode == 1 && col.a < alpha_cutoff) {
		discard;
	}

	ALBEDO = col.rgb;
	// WRITING ALPHA AT ALL IS WHAT MAKES A MATERIAL TRANSPARENT IN GODOT, whatever value you write.
	// Not the blend mode, not the value being 1.0 - the mere presence of the write. A material in the
	// transparent pass writes no depth and is sorted per object against every other transparent thing,
	// so as the camera moves the order flips and surfaces pop in and out. Every opaque material here
	// was landing in that pass because this line ran unconditionally.
	//
	// The opaque variant does not define TRANSPARENT_PASS, so this whole write vanishes at preprocess
	// and Godot keeps the material in the opaque pass with proper depth. Same split the VertexColor
	// pair has always used. -xlinka
#ifdef TRANSPARENT_PASS
	ALPHA = col.a;
#endif

#ifdef DEPTH_OFFSET
	DEPTH = offset_depth(FRAGCOORD.z);
#endif
}
