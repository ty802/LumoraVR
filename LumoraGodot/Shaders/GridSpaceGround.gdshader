shader_type spatial;
render_mode cull_back;

uniform vec4 base_near_color : source_color = vec4(0.15, 0.16, 0.24, 1.0);
uniform vec4 base_far_color : source_color = vec4(0.06, 0.08, 0.14, 1.0);
uniform vec4 line_near_color : source_color = vec4(0.62, 0.69, 1.00, 1.0);
uniform vec4 line_far_color : source_color = vec4(0.34, 0.44, 0.82, 1.0);
uniform float minor_scale : hint_range(0.1, 8.0) = 1.0;
uniform float major_scale : hint_range(1.0, 64.0) = 8.0;
uniform float line_width : hint_range(0.2, 2.5) = 1.6;
uniform float major_line_width : hint_range(0.4, 3.5) = 2.3;
uniform float radial_fade : hint_range(0.2, 3.0) = 1.1;

float grid_line(vec2 uv, float scale, float width_px)
{
    vec2 cell = uv / scale;
    vec2 dist = abs(fract(cell - 0.5) - 0.5) / max(fwidth(cell), vec2(0.0001));
    float line = min(dist.x, dist.y);
    return 1.0 - smoothstep(width_px, width_px + 1.0, line);
}

void fragment()
{
    float minor = grid_line(UV, minor_scale, line_width);
    float major = grid_line(UV, major_scale, major_line_width);
    float grid_mask = clamp(max(minor * 0.6, major), 0.0, 1.0);

    float radial = clamp(length(UV - vec2(0.5)) * radial_fade, 0.0, 1.0);
    vec3 base_col = mix(base_near_color.rgb, base_far_color.rgb, radial);
    vec3 line_col = mix(line_near_color.rgb, line_far_color.rgb, radial);

    ALBEDO = mix(base_col, line_col, grid_mask);
    EMISSION = line_col * grid_mask * 0.2;
    ROUGHNESS = 0.95;
    METALLIC = 0.0;
}
