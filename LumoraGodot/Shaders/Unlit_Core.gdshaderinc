// Shared body of the unlit material: a texture times a tint, nothing lit.
//
// Unlit.gdshader and Unlit_Additive.gdshader include this for the two blends the material has always
// had. A render state neither file covers (a forced depth write, a depth bias, billboards) gets its own
// source written by the material and compiled at runtime, over this same body. Every switch below
// defaults to off, and off leaves the surface exactly as the two files drew it before they shared a body.
//
// Defines a variant can set before including:
//   UNLIT_ADDITIVE  alpha scales the colour and the surface adds onto what is behind it
//   BILLBOARD       each point of the mesh grows into a camera-facing quad, see the vertex() below
//   DEPTH_OFFSET    writes a biased depth, see DepthOffset.gdshaderinc
// -xlinka

#include "res://Shaders/PolarUV.gdshaderinc"
#include "res://Shaders/VertexColorSpace.gdshaderinc"
#include "res://Shaders/MeshUV.gdshaderinc"
#ifdef DEPTH_OFFSET
#include "res://Shaders/DepthOffset.gdshaderinc"
#endif

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;

uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform int texture_uv : hint_enum("UV1", "UV2", "UV3", "UV4") = 0;
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

// The mask folds RGB to one factor and multiplies by its own alpha: (r + g + b) / 3 * a. 0 scales this
// surface's alpha by it, 1 cuts away wherever it does not clear alpha_cutoff. Sampled at the mesh UV
// under its own tiling, not through the main texture's.
uniform sampler2D mask_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_mask_texture = false;
uniform vec2 mask_scale = vec2(1.0);
uniform vec2 mask_offset = vec2(0.0);
uniform int mask_mode = 0;

// Shifts where the main texture is read by this texture's red and green, times the magnitude. Green
// pushes UP the picture, so it subtracts from this engine's top-down V.
uniform sampler2D offset_texture : filter_linear_mipmap, repeat_enable;
uniform bool use_offset_texture = false;
uniform vec2 offset_magnitude = vec2(0.0);
uniform vec2 offset_texture_scale = vec2(1.0);
uniform vec2 offset_texture_offset = vec2(0.0);

uniform bool polar_mapping = false;
uniform float polar_power = 1.0;

// Shows a normal map as the colour it encodes rather than its raw texels: the tangent normal remapped
// from -1..1 into 0..1, alpha forced opaque.
uniform bool decode_as_normal_map = false;

#ifdef BILLBOARD
// Half the quad's width and height, in metres before the object's scale.
uniform vec2 billboard_size = vec2(0.005);
uniform bool use_per_billboard_scale = false;
uniform bool use_per_billboard_rotation = false;
uniform bool use_per_billboard_uv = false;
#endif

void vertex() {
	lm_uv34 = CUSTOM1;
#ifdef BILLBOARD
	// A geometry stage would grow each point into a quad. Godot has none, so the mesh hands every point
	// over four times, one copy per corner, in the order (0,0) (1,0) (1,1) (0,1), and the corner is read
	// off the vertex index. CUSTOM0 carries the point's own width and height scale, its roll, and 1 in W
	// to say it IS a point; UV its atlas centre and UV2 that cell's size. Triangles have no W and fold to
	// nothing, as they would with no geometry stage to feed.
	//
	// The quad faces the camera's POSITION, not its view plane, and keeps world up as its up, so it does
	// not roll with the head. In a headset that position is the midpoint of the eyes, so both eyes see
	// the one quad rather than one turned to each. Its size follows the object's scale along X. Placed in
	// world space and handed over in view space, which is why the variant that defines this also skips
	// Godot's own vertex transform. -xlinka
	int corner = VERTEX_ID & 3;
	vec2 q = vec2(corner == 1 || corner == 2 ? 1.0 : 0.0, corner >= 2 ? 1.0 : 0.0);
	vec3 centre = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 forward = normalize(centre - CAMERA_POSITION_WORLD);
	vec3 right = cross(forward, vec3(0.0, 1.0, 0.0));
	right = dot(right, right) > 1e-10 ? normalize(right) : vec3(1.0, 0.0, 0.0);
	vec3 up = cross(right, forward);
	if (use_per_billboard_rotation) {
		float c = cos(CUSTOM0.z);
		float s = sin(CUSTOM0.z);
		vec3 turned_right = c * right - s * up;
		up = s * right + c * up;
		right = turned_right;
	}
	vec2 size = billboard_size * length(MODEL_MATRIX[0].xyz);
	if (use_per_billboard_scale) {
		size *= CUSTOM0.xy;
	}
	vec3 world = centre + right * ((q.x * 2.0 - 1.0) * size.x) + up * ((q.y * 2.0 - 1.0) * size.y);
	VERTEX = CUSTOM0.w > 0.5 ? (VIEW_MATRIX * vec4(world, 1.0)).xyz : vec3(0.0);
	NORMAL = normalize(mat3(VIEW_MATRIX) * -forward);
	vec2 quad_uv = vec2(q.x, 1.0 - q.y);
	UV = use_per_billboard_uv ? UV + UV2 * (quad_uv - 0.5) : quad_uv;
#endif
	COLOR = vertex_color_encode(COLOR);
}

void fragment() {
	// The eye split has to be a fragment decision because VIEW_INDEX is per view. Moving the plain
	// transform down here with it costs nothing: an affine UV map interpolates to the same numbers
	// whether it is applied per vertex or per pixel. Outside multiview VIEW_INDEX is 0, so a flat
	// screen shows the left eye. -xlinka
	vec2 st_scale = uv_scale;
	vec2 st_offset = uv_offset;
	if (stereo_texture_transform) {
		st_scale = int(VIEW_INDEX) == 0 ? left_eye_texture_scale : right_eye_texture_scale;
		st_offset = int(VIEW_INDEX) == 0 ? left_eye_texture_offset : right_eye_texture_offset;
	}

	vec4 tex = vec4(1.0);
	vec2 texture_uvs = mesh_uv(texture_uv, UV, UV2, lm_uv34);
	if (use_albedo_texture) {
		vec2 shift = vec2(0.0);
		if (use_offset_texture) {
			shift = texture(offset_texture, UV * offset_texture_scale + offset_texture_offset).xy * offset_magnitude;
		}
		if (polar_mapping) {
			vec2 grad_x;
			vec2 grad_y;
			vec2 p = polar_transform(polar_uv(texture_uvs, polar_power), st_scale, st_offset, grad_x, grad_y);
			tex = polar_sample(albedo_texture, p + shift, grad_x, grad_y);
		} else {
			tex = texture(albedo_texture, texture_uvs * st_scale + st_offset + vec2(shift.x, -shift.y));
		}
		if (decode_as_normal_map) {
			// The sampler decodes sRGB on the way in; a normal map is data, so that is undone first.
			vec2 xy = encode_srgb(tex.rgb).rg * 2.0 - 1.0;
			vec3 n = vec3(xy, sqrt(clamp(1.0 - dot(xy, xy), 0.0, 1.0)));
			tex = vec4(n * 0.5 + 0.5, 1.0);
		}
	}
	vec4 out_color = albedo_color * tex;

	bool mask_clips = false;
	if (use_mask_texture) {
		vec4 m = texture(mask_texture, UV * mask_scale + mask_offset);
		float factor = (m.r + m.g + m.b) * 0.3333333 * m.a;
		if (mask_mode == 1) {
			mask_clips = true;
			if (factor - alpha_cutoff <= 0.0) {
				discard;
			}
		} else {
			out_color.a *= factor;
		}
	}

	if (use_vertex_color) {
		out_color *= vertex_color_decode(COLOR);
	}

	// A mask that clips is the cutout; testing alpha on top of it would cut twice.
	if (alpha_clip && !mask_clips) {
		float alpha_width = max(fwidth(out_color.a), 0.001);
		out_color.a *= smoothstep(alpha_cutoff - alpha_width, alpha_cutoff + alpha_width, out_color.a);
	}

#ifdef UNLIT_ADDITIVE
	// Additive: alpha scales the contribution (black adds nothing).
	ALBEDO = out_color.rgb * out_color.a;
	ALPHA = 1.0;
#else
	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
#endif

#ifdef DEPTH_OFFSET
	DEPTH = offset_depth(FRAGCOORD.z);
#endif
}
