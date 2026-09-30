// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Shading;

// The helper functions node code calls. Noise hashes whole cell numbers with integer math instead of
// the usual fract(sin()) trick, so a pattern comes out the same on a desktop card and a standalone
// headset's GPU. Loops keep literal bounds the sandbox can read; the cheap ones are written out. Anything
// that takes a derivative only works per pixel. -xlinka
public static class GlslLibrary
{
    private static readonly Dictionary<string, GlslFunction> _functions = new(StringComparer.Ordinal);

    static GlslLibrary()
    {
        Add(new GlslFunction("lm_hash", """
uint lm_hash(uint x) {
	x ^= x >> 16u;
	x *= 0x7feb352du;
	x ^= x >> 15u;
	x *= 0x846ca68bu;
	x ^= x >> 16u;
	return x;
}
"""));
        Add(new GlslFunction("lm_cell_hash", """
uint lm_cell_hash(vec2 cell) {
	ivec2 i = ivec2(floor(cell));
	return lm_hash(uint(i.x) ^ lm_hash(uint(i.y) + 0x9e3779b9u));
}
""", "lm_hash"));
        Add(new GlslFunction("lm_hash21", """
float lm_hash21(vec2 cell) {
	return float(lm_cell_hash(cell) >> 8u) * (1.0 / 16777216.0);
}
""", "lm_cell_hash"));
        Add(new GlslFunction("lm_hash22", """
vec2 lm_hash22(vec2 cell) {
	uint h = lm_cell_hash(cell);
	return vec2(float(h >> 8u), float(lm_hash(h + 0x632be5abu) >> 8u)) * (1.0 / 16777216.0);
}
""", "lm_cell_hash", "lm_hash"));
        Add(new GlslFunction("lm_value_noise", """
float lm_value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = lm_hash21(i);
	float b = lm_hash21(i + vec2(1.0, 0.0));
	float c = lm_hash21(i + vec2(0.0, 1.0));
	float d = lm_hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}
""", "lm_hash21"));
        Add(new GlslFunction("lm_simple_noise", """
float lm_simple_noise(vec2 uv, float scale) {
	float fine = lm_value_noise(uv * scale);
	float middle = lm_value_noise(uv * scale * 0.5);
	float coarse = lm_value_noise(uv * scale * 0.25);
	return (fine * 0.125 + middle * 0.25 + coarse * 0.5) / 0.875;
}
""", "lm_value_noise"));
        Add(new GlslFunction("lm_gradient", """
vec2 lm_gradient(vec2 cell) {
	float angle = lm_hash21(cell) * 6.28318530718;
	return vec2(cos(angle), sin(angle));
}
""", "lm_hash21"));
        Add(new GlslFunction("lm_perlin", """
float lm_perlin(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = dot(lm_gradient(i), f);
	float b = dot(lm_gradient(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
	float c = dot(lm_gradient(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
	float d = dot(lm_gradient(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y) * 0.70710678 + 0.5;
}
""", "lm_gradient"));
        Add(new GlslFunction("lm_noise_texture", """
float lm_noise_texture(vec2 uv, float scale, float detail, float roughness) {
	vec2 p = uv * scale;
	float octaves = clamp(detail, 0.0, 15.0);
	float weight = 1.0;
	float total = 0.0;
	float norm = 0.0;
	for (int i = 0; i < 16; i++) {
		if (float(i) > octaves) {
			break;
		}
		total += lm_perlin(p) * weight;
		norm += weight;
		weight *= clamp(roughness, 0.0, 1.0);
		p *= 2.0;
	}
	return total / max(norm, 1e-5);
}
""", "lm_perlin"));
        Add(new GlslFunction("lm_voronoi", """
void lm_voronoi(vec2 uv, float angle_offset, float density, out float distance_out, out float cells_out) {
	vec2 p = uv * density;
	vec2 g = floor(p);
	vec2 f = fract(p);
	float best = 8.0;
	float cell = 0.0;
	for (int y = 0; y < 3; y++) {
		for (int x = 0; x < 3; x++) {
			vec2 lattice = vec2(float(x - 1), float(y - 1));
			vec2 h = lm_hash22(g + lattice);
			vec2 point = lattice + 0.5 + 0.5 * sin(angle_offset + 6.28318530718 * h);
			float d = distance(point, f);
			if (d < best) {
				best = d;
				cell = h.x;
			}
		}
	}
	distance_out = best;
	cells_out = cell;
}
""", "lm_hash22"));
        Add(new GlslFunction("lm_checker", """
float lm_checker(vec2 uv, vec2 frequency) {
	vec2 p = uv * frequency * 0.5;
	vec2 w = max(fwidth(p), vec2(1e-5));
	vec2 i = (abs(fract((p - 0.5 * w) * 0.5) - 0.5) - abs(fract((p + 0.5 * w) * 0.5) - 0.5)) / w;
	return 0.5 - 0.5 * i.x * i.y;
}
"""));
        Add(new GlslFunction("lm_ellipse", """
float lm_ellipse(vec2 uv, float width, float height) {
	float d = length((uv * 2.0 - 1.0) / vec2(max(width, 1e-5), max(height, 1e-5)));
	return clamp((1.0 - d) / max(fwidth(d), 1e-5), 0.0, 1.0);
}
"""));
        Add(new GlslFunction("lm_rectangle", """
float lm_rectangle(vec2 uv, float width, float height) {
	vec2 d = abs(uv * 2.0 - 1.0) - vec2(width, height);
	d = 1.0 - d / max(fwidth(d), vec2(1e-5));
	return clamp(min(d.x, d.y), 0.0, 1.0);
}
"""));
        Add(new GlslFunction("lm_rotate_uv", """
vec2 lm_rotate_uv(vec2 uv, vec2 center, float radians) {
	vec2 d = uv - center;
	float s = sin(radians);
	float c = cos(radians);
	return vec2(d.x * c - d.y * s, d.x * s + d.y * c) + center;
}
"""));
        Add(new GlslFunction("lm_polar", """
vec2 lm_polar(vec2 uv, vec2 center, float radial_scale, float length_scale) {
	vec2 d = uv - center;
	return vec2(length(d) * 2.0 * radial_scale, atan(d.y, d.x) * (1.0 / 6.28318530718) * length_scale);
}
"""));
        Add(new GlslFunction("lm_twirl", """
vec2 lm_twirl(vec2 uv, vec2 center, float strength, vec2 offset) {
	vec2 d = uv - center;
	float angle = strength * length(d);
	float s = sin(angle);
	float c = cos(angle);
	return vec2(c * d.x - s * d.y, s * d.x + c * d.y) + center + offset;
}
"""));
        Add(new GlslFunction("lm_flipbook", """
vec2 lm_flipbook(vec2 uv, float columns, float rows, float tile) {
	float across = max(floor(columns), 1.0);
	float down = max(floor(rows), 1.0);
	float t = mod(floor(tile), across * down);
	vec2 cell = vec2(mod(t, across), floor(t / across));
	return (fract(uv) + cell) / vec2(across, down);
}
"""));
        Add(new GlslFunction("lm_rgb_to_hsv", """
vec3 lm_rgb_to_hsv(vec3 c) {
	float high = max(c.r, max(c.g, c.b));
	float low = min(c.r, min(c.g, c.b));
	float range = high - low;
	float h = 0.0;
	if (range > 1e-6) {
		if (high == c.r) {
			h = mod((c.g - c.b) / range, 6.0);
		} else if (high == c.g) {
			h = (c.b - c.r) / range + 2.0;
		} else {
			h = (c.r - c.g) / range + 4.0;
		}
	}
	return vec3(h / 6.0, high > 1e-6 ? range / high : 0.0, high);
}
"""));
        Add(new GlslFunction("lm_hsv_to_rgb", """
vec3 lm_hsv_to_rgb(vec3 c) {
	vec3 k = mod(vec3(5.0, 3.0, 1.0) + c.x * 6.0, 6.0);
	return c.z - c.z * c.y * clamp(min(k, 4.0 - k), 0.0, 1.0);
}
"""));
        Add(new GlslFunction("lm_hue_saturation_value", """
vec3 lm_hue_saturation_value(vec3 c, float hue, float saturation, float value, float factor) {
	vec3 hsv = lm_rgb_to_hsv(c);
	hsv.x = fract(hsv.x + hue - 0.5);
	hsv.y = clamp(hsv.y * saturation, 0.0, 1.0);
	hsv.z *= value;
	return mix(c, lm_hsv_to_rgb(hsv), factor);
}
""", "lm_rgb_to_hsv", "lm_hsv_to_rgb"));
        Add(new GlslFunction("lm_brightness_contrast", """
vec3 lm_brightness_contrast(vec3 c, float bright, float contrast) {
	float a = 1.0 + contrast;
	float b = bright - contrast * 0.5;
	return max(a * c + b, vec3(0.0));
}
"""));
        Add(new GlslFunction("lm_blend_overlay", """
vec3 lm_blend_overlay(vec3 base, vec3 blend) {
	vec3 low = 2.0 * base * blend;
	vec3 high = 1.0 - 2.0 * (1.0 - base) * (1.0 - blend);
	return mix(low, high, step(0.5, base));
}
"""));
        Add(new GlslFunction("lm_blend_soft_light", """
vec3 lm_blend_soft_light(vec3 base, vec3 blend) {
	vec3 screen = 1.0 - (1.0 - blend) * (1.0 - base);
	return (1.0 - base) * blend * base + base * screen;
}
"""));
        Add(new GlslFunction("lm_blend_burn", """
vec3 lm_blend_burn(vec3 base, vec3 blend) {
	return clamp(1.0 - (1.0 - base) / max(blend, vec3(1e-5)), 0.0, 1.0);
}
"""));
        Add(new GlslFunction("lm_blend_dodge", """
vec3 lm_blend_dodge(vec3 base, vec3 blend) {
	return min(base / max(1.0 - blend, vec3(1e-5)), vec3(1.0));
}
"""));
        Add(new GlslFunction("lm_blend_divide", """
vec3 lm_blend_divide(vec3 base, vec3 blend) {
	vec3 safe = mix(vec3(1.0), blend, step(vec3(1e-6), abs(blend)));
	return base / safe;
}
"""));
        Add(new GlslFunction("lm_unpack_normal", """
vec3 lm_unpack_normal(vec4 c, float strength) {
	vec2 xy = (c.xy * 2.0 - 1.0) * strength;
	return normalize(vec3(xy, sqrt(max(1.0 - dot(xy, xy), 0.0))));
}
"""));
        Add(new GlslFunction("lm_normal_blend", """
vec3 lm_normal_blend(vec3 a, vec3 b) {
	vec3 t = a + vec3(0.0, 0.0, 1.0);
	vec3 u = b * vec3(-1.0, -1.0, 1.0);
	return normalize(t * dot(t, u) / max(t.z, 1e-5) - u);
}
"""));
        Add(new GlslFunction("lm_bump", """
vec3 lm_bump(float height, float strength, vec3 position, vec3 normal, vec3 tangent, vec3 binormal) {
	vec3 dpdx = dFdx(position);
	vec3 dpdy = dFdy(position);
	float dhdx = dFdx(height);
	float dhdy = dFdy(height);
	vec3 r1 = cross(dpdy, normal);
	vec3 r2 = cross(normal, dpdx);
	float det = dot(dpdx, r1);
	if (abs(det) < 1e-12) {
		return vec3(0.0, 0.0, 1.0);
	}
	vec3 gradient = sign(det) * (dhdx * r1 + dhdy * r2);
	vec3 bumped = normalize(abs(det) * normal - strength * gradient);
	return normalize(vec3(dot(bumped, tangent), dot(bumped, binormal), dot(bumped, normal)));
}
"""));
        Add(new GlslFunction("lm_fresnel", """
float lm_fresnel(vec3 normal, vec3 view, float power) {
	return pow(1.0 - clamp(dot(normalize(normal), normalize(view)), 0.0, 1.0), power);
}
"""));
        Add(new GlslFunction("lm_rotate_about_axis", """
vec3 lm_rotate_about_axis(vec3 v, vec3 axis, float radians) {
	vec3 a = normalize(axis);
	float s = sin(radians);
	float c = cos(radians);
	return v * c + cross(a, v) * s + a * dot(a, v) * (1.0 - c);
}
"""));
        Add(new GlslFunction("lm_map_range", """
float lm_map_range(float v, float from_min, float from_max, float to_min, float to_max) {
	float span = from_max - from_min;
	return to_min + (v - from_min) * (to_max - to_min) / (abs(span) < 1e-8 ? 1e-8 : span);
}
"""));
        Add(new GlslFunction("lm_srgb_to_linear", """
vec3 lm_srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((max(c, vec3(0.0)) + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}
"""));
    }

    public static IReadOnlyCollection<string> Names => _functions.Keys;

    public static bool TryGet(string name, out GlslFunction function) => _functions.TryGetValue(name, out function!);

    // Every function asked for and every one those call, each once, callees first, in a fixed order.
    public static List<GlslFunction> Resolve(IEnumerable<string> names)
    {
        var ordered = new List<GlslFunction>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var sorted = new List<string>(names);
        sorted.Sort(StringComparer.Ordinal);
        foreach (var name in sorted)
            Visit(name, ordered, done, 0);
        return ordered;
    }

    private static void Visit(string name, List<GlslFunction> ordered, HashSet<string> done, int depth)
    {
        if (done.Contains(name))
            return;
        if (depth > 16 || !_functions.TryGetValue(name, out var function))
            throw new InvalidOperationException($"no shader function '{name}'");
        foreach (var need in function.Needs)
            Visit(need, ordered, done, depth + 1);
        done.Add(name);
        ordered.Add(function);
    }

    private static void Add(GlslFunction function) => _functions.Add(function.Name, function);
}
