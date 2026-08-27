shader_type spatial;
// Inverted-hull outline: the shell is pushed out along the normal and only its BACK faces are kept,
// so it shows as a border wherever the real surface does not cover it. Godot 4 has no multi-pass
// shaders, so this is attached as the toon material's next_pass by the material hook rather than
// living as a second pass in Mat_FlatToon. -xlinka
//
// Opaque, depth-tested and depth-writing on purpose. The shell's inward-facing fragments sit BEHIND
// the model, so the depth the toon pass already laid down rejects them and only the border survives.
// outline_color's alpha is not used: a blended shell would need the depth write turned off, and then
// the interior fragments would paint straight over the model. -xlinka
render_mode unshaded, cull_front;

uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// Metres at one metre from the camera. The extrusion happens in view space scaled by the vertex's
// view depth, so the border keeps a constant apparent thickness instead of vanishing at distance
// or swallowing the model up close.
uniform float outline_width : hint_range(0.0, 0.1) = 0.0;

void vertex() {
	vec3 view_normal = normalize(MODELVIEW_NORMAL_MATRIX * NORMAL);
	vec3 view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	view_pos += view_normal * outline_width * max(-view_pos.z, 0.01);
	POSITION = PROJECTION_MATRIX * vec4(view_pos, 1.0);
}

void fragment() {
	if (outline_width <= 0.0) {
		discard;
	}
	ALBEDO = outline_color.rgb;
}
