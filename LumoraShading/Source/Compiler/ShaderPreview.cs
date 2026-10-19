// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Text;
using System.Text.RegularExpressions;

namespace Lumora.Shading;

// Display transforms for node cards only. Values flowing through the graph and the material output
// keep their full range. A curve samples the preview's horizontal UV slice at V = 0.5.
public static class ShaderPreview
{
    public const string CurveUniform = "lm_preview_curve";
    public const string RangeUniform = "lm_preview_range";

    public static bool IsNumeric(ShaderType type) => type is not (ShaderType.Color or ShaderType.Texture);

    internal static string Fragment(string body, GlslValue value)
    {
        bool numeric = IsNumeric(value.Type);
        var text = new StringBuilder();
        if (numeric)
        {
            text.Append("\tvec2 lm_preview_uv = lm_preview_curve ? vec2(UV.x, 0.5) : UV;\n");
            text.Append(Regex.Replace(body, @"\bUV\b", "lm_preview_uv"));
        }
        else
            text.Append(body);

        string rgb = value.Type switch
        {
            ShaderType.Texture => value.Code.Length > 0 ? $"texture({value.Code}, UV).rgb" : "vec3(1.0)",
            ShaderType.Color => value.Code + ".rgb",
            _ => ShaderTypes.Convert(value, ShaderType.Float3).Code,
        };
        text.Append("\tvec3 lm_preview_rgb = ").Append(rgb).Append(";\n");
        if (numeric)
        {
            text.Append("\tif (lm_preview_curve) {\n\t\tvec3 lm_preview_plot = lm_preview_grid(UV);\n");
            int channels = ShaderTypes.Width(value.Type);
            string[] inks = channels == 1 ? new[] { "vec3(0.45, 0.28, 0.065)" }
                : new[] { "vec3(0.45, 0.10, 0.08)", "vec3(0.08, 0.38, 0.14)", "vec3(0.08, 0.22, 0.45)", "vec3(0.36, 0.15, 0.42)" };
            for (int i = 0; i < channels; i++)
            {
                string channel = channels == 1 ? ShaderTypes.Convert(value, ShaderType.Float).Code : value.Code + "." + "xyzw"[i];
                text.Append("\t\tlm_preview_plot = mix(lm_preview_plot, ").Append(inks[i])
                    .Append(", lm_preview_line(UV, ").Append(channel).Append(", lm_preview_range));\n");
            }
            text.Append("\t\tALBEDO = lm_preview_plot;\n\t} else {\n\t\tALBEDO = lm_preview_image(lm_preview_rgb, true);\n\t}\n");
        }
        else
            text.Append("\tALBEDO = lm_preview_image(lm_preview_rgb, false);\n");
        return text.ToString();
    }

    internal const string Helpers = """
vec3 lm_preview_image(vec3 lm_rgb, bool lm_numeric) {
    if (any(isnan(lm_rgb)) || any(isinf(lm_rgb))) {
        return vec3(0.30, 0.055, 0.16);
    }
    lm_rgb = max(lm_rgb, vec3(0.0));
    if (lm_numeric) {
        // Clamp before gamma conversion: numeric outputs can be negative or arbitrarily large.
        lm_rgb = clamp(lm_rgb, vec3(0.0), vec3(1.0));
        lm_rgb = mix(lm_rgb / 12.92, pow((lm_rgb + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), lm_rgb));
    }
    float lm_peak = max(1.0, max(lm_rgb.r, max(lm_rgb.g, lm_rgb.b)));
    return (lm_rgb / lm_peak) * 0.45;
}

vec3 lm_preview_grid(vec2 lm_uv) {
    vec2 lm_cell = abs(fract(lm_uv * 4.0 + 0.5) - 0.5);
    vec2 lm_pixel = max(fwidth(lm_uv) * 4.0, vec2(0.006));
    vec2 lm_grid = vec2(1.0) - smoothstep(lm_pixel * 0.3, lm_pixel * 1.1, lm_cell);
    float lm_axis = 1.0 - smoothstep(0.002, max(fwidth(lm_uv.y), 0.006), abs(lm_uv.y - 0.5));
    vec3 lm_ground = mix(vec3(0.012, 0.016, 0.022), vec3(0.035, 0.043, 0.055), max(lm_grid.x, lm_grid.y));
    return mix(lm_ground, vec3(0.08, 0.09, 0.11), lm_axis);
}

float lm_preview_line(vec2 lm_uv, float lm_value, float lm_range) {
    if (isnan(lm_value) || isinf(lm_value)) {
        // Invalid results get a dashed marker at the top instead of poisoning the frame with NaNs.
        return (1.0 - smoothstep(0.004, 0.012, abs(lm_uv.y - 0.04))) * step(0.5, fract(lm_uv.x * 16.0));
    }
    float lm_scale = max(lm_range, 0.000001);
    float lm_y = 0.5 - 0.42 * clamp(lm_value / lm_scale, -1.0, 1.0);
    float lm_distance = lm_uv.y - lm_y;
    float lm_width = clamp(fwidth(lm_distance), 0.006, 0.025);
    float lm_line = 1.0 - smoothstep(lm_width * 0.35, lm_width * 1.2, abs(lm_distance));
    // Dashed at the range boundary means clipped, not a value equal to that boundary.
    if (abs(lm_value) > lm_scale) {
        lm_line *= step(0.5, fract(lm_uv.x * 16.0));
    }
    return lm_line;
}
""";
}
