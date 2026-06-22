shader_type spatial;
// Mask WRITER. Stamps the stencil reference value into the stencil buffer wherever this (usually invisible)
// mask geometry rasterizes; UI content drawn afterwards with UI_UnlitStencil tests against it, so the content
// is clipped to this mask's exact SHAPE (a circle Image, a rounded panel, any geometry) - not just an AABB.
// depth_test_disabled keeps the mask on the UI layer; stencil is independent of the depth test. Godot 4.5+
// stencil_mode (shader-language render mode). -xlinka
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled;
stencil_mode write, compare_always, 1;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear, repeat_disable;
uniform bool use_albedo_texture = false;
uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform bool use_vertex_color = true;

// When false the mask is invisible - it writes the stencil but draws no color (the common case: the mask
// shape only defines the clip region). When true the mask graphic is ALSO drawn. We can't toggle stencil_mode
// at runtime (it's compile-time), but the visible color is gateable. -xlinka
uniform bool show_mask_graphic = false;
// The mask SHAPE is where coverage (texture alpha * color alpha) is at/above this cutoff. -xlinka
uniform float mask_cutoff : hint_range(0.0, 1.0) = 0.5;

void vertex() {
	UV = UV * uv_scale + uv_offset;
}

void fragment() {
	vec4 tex = use_albedo_texture ? texture(albedo_texture, UV) : vec4(1.0);
	vec4 out_color = albedo_color * tex;
	if (use_vertex_color) {
		out_color *= COLOR;
	}
	// Discard OUTSIDE the shape so the stencil is written ONLY where the mask is opaque - a circle texture
	// stencils a circle, not its bounding quad. Discarding skips the stencil write, which is exactly what
	// leaves the outside unstenciled. INSIDE the shape the fragment is kept and still rasterizes (even with
	// ALPHA=0), so the stencil write lands. This is what makes the stencil follow the mask's real shape. -xlinka
	if (out_color.a < mask_cutoff) {
		discard;
	}
	ALBEDO = out_color.rgb;
	// Invisible by default (stencil-only); kept fragment, so stencil still writes.
	ALPHA = show_mask_graphic ? out_color.a : 0.0;
}
