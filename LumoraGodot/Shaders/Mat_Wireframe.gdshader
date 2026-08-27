shader_type spatial;
// Depth-tested wireframe. Mat_WireframeOverlay is the same body with the depth test off; the hook
// swaps between them on DrawOverDepth, the same way the UI unlit family swaps ZTest variants.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

// Barycentric coordinates arrive in the vertex COLOR channel. Godot 4 has no geometry shader and no
// barycentric builtin, and the mesh upload path carries only position/normal/tangent/color/uv0/index,
// so COLOR is the only per-vertex slot a triangle-local coordinate can ride in. Bake it with the mesh's
// "Bake Wireframe Barycentrics" action, which unwelds the triangles first (shared vertices cannot carry
// three different corner values). Without a bake there are no wires, only the fill. -xlinka
uniform bool use_vertex_barycentric = true;

uniform vec4 line_color : source_color = vec4(1.0);
uniform vec4 fill_color : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform float line_width : hint_range(0.1, 20.0) = 1.5;

uniform sampler2D fill_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_fill_texture = false;
uniform vec2 texture_scale = vec2(1.0);
uniform vec2 texture_offset = vec2(0.0);

void vertex() {
	UV = UV * texture_scale + texture_offset;
}

void fragment() {
	vec4 fill = fill_color;
	if (use_fill_texture) {
		fill *= texture(fill_texture, UV);
	}

	float wire = 0.0;
	if (use_vertex_barycentric) {
		// Screen-space width: fwidth of the barycentric gives how much it changes per pixel, so
		// dividing by it turns the coordinate into a pixel distance to the nearest edge. Constant
		// line thickness at any distance or angle without a thickness uniform in world units.
		vec3 bary = COLOR.rgb;
		vec3 d = fwidth(bary);
		vec3 edge = smoothstep(vec3(0.0), d * line_width, bary);
		wire = 1.0 - min(min(edge.x, edge.y), edge.z);
	}

	vec4 out_color = mix(fill, line_color, wire);
	ALBEDO = out_color.rgb;
	ALPHA = out_color.a;
}
