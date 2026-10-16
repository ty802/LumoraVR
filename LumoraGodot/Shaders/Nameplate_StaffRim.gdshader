shader_type spatial;
// The ring round a staff member's nameplate: its own colour, breathing a little, with two bright arcs
// chasing round it. The angle is taken in the ring's own space, so it runs the same from any side and in
// mirrors and cameras, and one material serves every staff plate in the world. -xlinka
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec4 glint_color : source_color = vec4(1.0);
uniform float laps_per_second = 0.3;

varying vec2 ring_pos;

void vertex() {
	ring_pos = VERTEX.xy;
}

void fragment() {
	float turn = atan(ring_pos.y, ring_pos.x) / 6.2831853 + 0.5;
	float behind = fract(TIME * laps_per_second - turn);
	float arc = pow(1.0 - behind, 9.0) + pow(1.0 - fract(behind + 0.5), 9.0);
	float breath = 0.85 + 0.15 * sin(TIME * 1.6);
	float lit = clamp(arc, 0.0, 1.0);
	ALBEDO = mix(COLOR.rgb * breath, glint_color.rgb, lit);
	ALPHA = clamp(max(COLOR.a, lit), 0.0, 1.0);
}
