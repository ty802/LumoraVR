shader_type spatial;
// Detached panels have a solid backing on both sides. The capture is premultiplied;
// fill its transparent pixels with the backing instead of sampling the room.
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D dash_tex : source_color, filter_linear, repeat_disable;
uniform vec4 backing_color : source_color = vec4(0.110, 0.104, 0.157, 1.0);

void fragment() {
	vec4 content = texture(dash_tex, UV);
	ALBEDO = FRONT_FACING
		? content.rgb + backing_color.rgb * (1.0 - content.a)
		: backing_color.rgb;
	ALPHA = 1.0;
}
