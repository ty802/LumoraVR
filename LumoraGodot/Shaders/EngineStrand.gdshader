shader_type spatial;
// cull_disabled, unlike the billboard particle shader: a ribbon has a back and you WILL be standing
// behind half of one at any moment.
render_mode blend_add, depth_draw_never, cull_disabled, unshaded;

uniform float emission_strength : hint_range(0.0, 6.0) = 1.4;
uniform sampler2D strand_texture : source_color, hint_default_white, filter_linear, repeat_enable;
uniform bool use_texture = false;

void fragment() {
    vec4 tint = COLOR;
    if (use_texture) {
        tint *= texture(strand_texture, UV);
    }
    ALBEDO = tint.rgb;
    EMISSION = tint.rgb * emission_strength;
    ALPHA = tint.a;
}
