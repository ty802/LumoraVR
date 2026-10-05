shader_type spatial;
// Cuts whatever is behind the surface to two levels per channel at threshold, with a ramp transition
// wide between them. The arithmetic is kept exactly as the Unity shader has it, half-transition
// nudge included, so authored values land the same. -xlinka
render_mode unshaded, fog_disabled, blend_mix, cull_disabled, depth_draw_never;

#include "res://Shaders/ScreenFilter_Core.gdshaderinc"

uniform float threshold = 0.5;
uniform float transition = 0.005;

void fragment() {
	if (filter_culled(FRONT_FACING)) {
		discard;
	}

	vec3 behind = texture(screen_behind, SCREEN_UV).rgb;

	vec3 c = behind - threshold;
	c /= transition;
	c += transition * 0.5;
	c = clamp(c, vec3(0.0), vec3(1.0));

	ALBEDO = filter_blend(c, behind);
	ALPHA = 1.0;
}
