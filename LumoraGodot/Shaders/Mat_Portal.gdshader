shader_type spatial;
// Portal face: the destination's picture inside an ellipse, a crackling crystal rim around it, a soft
// glow past that and twinkling sparks in the glow. One quad, one material. The ellipse is the unit
// circle of UV space, so the quad's own proportions decide the ellipse and the picture is cover-fitted
// to it. Unshaded and additive-looking on purpose: a door should read from across a room. -xlinka
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_disabled;

uniform sampler2D picture : source_color, filter_linear, repeat_disable;
uniform bool use_picture = false;
uniform bool flip_picture = false;
uniform vec4 picture_tint : source_color = vec4(1.0);
uniform vec4 fill_color : source_color = vec4(0.08, 0.09, 0.16, 1.0);
uniform vec4 rim_color : source_color = vec4(0.3, 0.5, 1.0, 1.0);
uniform vec4 crack_color : source_color = vec4(1.0);
uniform float rim_width : hint_range(0.02, 0.4) = 0.16;
uniform float glow_width : hint_range(0.0, 0.6) = 0.14;
uniform float glow_strength : hint_range(0.0, 4.0) = 1.4;
uniform float crack_scale : hint_range(2.0, 80.0) = 22.0;
uniform float crack_width : hint_range(0.005, 0.2) = 0.045;
uniform float crack_brightness : hint_range(0.0, 8.0) = 2.5;
uniform float flicker : hint_range(0.0, 1.0) = 0.6;
uniform float sparkle : hint_range(0.0, 2.0) = 0.8;
uniform float speed : hint_range(0.0, 4.0) = 1.0;
uniform float seed = 0.0;
// Width over height of the quad this is on; the picture keeps its own shape inside that.
uniform float quad_aspect = 0.76;
// The material layer writes these on every shader material. The render modes above are fixed, so
// they are accepted and ignored.
uniform int blend_mode = 0;
uniform int cull_mode = 0;

vec2 hash22(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p + seed) * 43758.5453);
}

float hash12(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233)) + seed) * 43758.5453);
}

// Voronoi cells laid along the rim: x runs round the circumference (wrapped, so the seam is
// invisible), y runs across the band. Cell centres drift with time, which is what makes the crystal
// "crackle" as the cracks between cells slide. f1 is the nearest centre, f2 the second nearest, so
// f2 - f1 is zero exactly on a crack.
void voronoi(vec2 p, float t, out float f1, out float f2, out float id) {
	vec2 cell = floor(p);
	vec2 frac = fract(p);
	f1 = 8.0;
	f2 = 8.0;
	id = 0.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 n = vec2(float(x), float(y));
			vec2 key = vec2(mod(cell.x + n.x + crack_scale, crack_scale), cell.y + n.y);
			vec2 h = hash22(key);
			vec2 centre = n + 0.5 + 0.32 * sin(t * 0.6 + 6.2831853 * h);
			float d = length(centre - frac);
			if (d < f1) {
				f2 = f1;
				f1 = d;
				id = hash12(key);
			} else if (d < f2) {
				f2 = d;
			}
		}
	}
}

void fragment() {
	vec2 uv = UV;
	vec2 p = uv * 2.0 - 1.0;
	// The ellipse sits inside the quad with room for the glow and the sparks past it, so nothing is
	// cut off at the quad's edge: everything below works in a space where the ellipse is the unit
	// circle and the quad edge is a little beyond the outer halo.
	float fit = 1.0 + glow_width * 2.0 + 0.04;
	float r = length(p) * fit;
	float t = TIME * speed;
	float inner = 1.0 - rim_width;
	float aa = max(fwidth(r) * 1.5, 0.002);

	vec3 col = fill_color.rgb;
	float alpha = 1.0;

	// The doorway: the picture, cover-fitted, darkened toward the rim so the crystal reads as a frame.
	if (r < inner + aa) {
		if (use_picture) {
			vec2 ts = vec2(textureSize(picture, 0));
			float pic_aspect = ts.x / max(ts.y, 1.0);
			vec2 window = vec2(1.0);
			if (pic_aspect > quad_aspect) {
				window.x = quad_aspect / pic_aspect;
			} else {
				window.y = pic_aspect / quad_aspect;
			}
			vec2 puv = (uv - 0.5) * window * fit + 0.5;
			if (flip_picture) {
				puv.y = 1.0 - puv.y;
			}
			col = texture(picture, puv).rgb * picture_tint.rgb;
		}
		float vig = smoothstep(inner, inner * 0.55, r);
		col *= mix(0.5, 1.0, vig);
	}

	// The crystal rim.
	float band = smoothstep(inner - aa, inner + aa, r) * (1.0 - smoothstep(1.0 - aa, 1.0 + aa, r));
	if (band > 0.0) {
		float ang = atan(p.y, p.x) / 6.2831853 + 0.5;
		float across = clamp((r - inner) / max(rim_width, 0.001), 0.0, 1.0);
		vec2 vp = vec2(ang * crack_scale, across * 2.0);
		float f1;
		float f2;
		float id;
		voronoi(vp, t, f1, f2, id);
		float crack = 1.0 - smoothstep(0.0, crack_width * 4.0, f2 - f1);
		float pulse = 0.5 + 0.5 * sin(t * 2.0 + id * 40.0);
		float facet = 0.55 + 0.7 * id;
		vec3 crystal = rim_color.rgb * facet * (1.0 - flicker * 0.5 + flicker * pulse) * mix(0.7, 1.35, across);
		vec3 rim = crystal + crack_color.rgb * crack * crack_brightness * (0.6 + 0.4 * pulse);
		col = mix(col, rim, band);
	}

	// Past the rim: glow that falls off, with sparks that twinkle in it.
	if (r > 1.0 - aa) {
		float g = 1.0 - smoothstep(1.0, 1.0 + glow_width, r);
		g *= g;
		vec3 glow = rim_color.rgb * glow_strength * g;

		vec2 sp = uv * 40.0;
		vec2 sc = floor(sp);
		vec2 sf = fract(sp) - 0.5;
		vec2 sh = hash22(sc);
		float life = fract(t * 0.7 + sh.y);
		float tw = pow(sin(life * 3.14159), 4.0);
		float d = length(sf - (sh - 0.5) * 0.6);
		float spark = smoothstep(0.12, 0.0, d) * tw * step(0.72, sh.x);
		float halo = 1.0 - smoothstep(1.0, 1.0 + glow_width * 1.8, r);
		float sparkAlpha = spark * sparkle * halo;

		col = glow + crack_color.rgb * sparkAlpha * 3.0;
		alpha = clamp(g + sparkAlpha, 0.0, 1.0);
	}

	ALBEDO = col;
	ALPHA = alpha;
}
