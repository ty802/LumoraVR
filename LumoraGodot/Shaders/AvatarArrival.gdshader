shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

// The stand-in a viewer sees where somebody's avatar is still arriving (AvatarArrival).
//
// One material for every stand-in in the world; what differs per person rides each piece's own instance
// value. x is presence: 0 to 1 while it fades in, 1 to 2 while it dissolves away upward, negative on the
// floor pad. y is the floor under the person and z the top of the figure, both world heights. w is the
// progress, plus 2 once the load has failed.
//
// The figure fills with light from the floor up as the avatar arrives: under the fill line it is dense
// and bright, above it only the rim and the drifting contour lines, with a bright waterline between. The
// pad under the feet carries the same progress as an arc. Everything is worked out from world height,
// so the pieces of one figure agree with each other however they are cut. -xlinka

instance uniform vec4 instance_params = vec4(1.0, 0.0, 1.8, 0.0);

uniform vec4 core_color = vec4(0.050, 0.045, 0.160, 1.0);
uniform vec4 glow_color = vec4(0.300, 0.236, 0.917, 1.0);
uniform vec4 fail_color = vec4(0.913, 0.462, 0.069, 1.0);
uniform float band_spacing : hint_range(0.01, 0.3) = 0.06;
uniform float band_speed : hint_range(0.0, 0.5) = 0.035;
uniform float time_scale : hint_range(0.0, 4.0) = 1.0;

varying vec3 world_pos;
varying vec2 pad_uv;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// The pad is the kit's ball pressed flat, radius 0.5 across its own XZ plane; this puts its rim at 1.
	pad_uv = VERTEX.xz * 2.0;
	// The pool lies on the floor the person stands on, and plenty of floors are drawn a few centimetres
	// proud of the surface people stand on. Slid toward the eye along its own line of sight it stays exactly
	// where it was on screen and still clears such a floor in the depth test. A shader that writes POSITION
	// has to write it for every vertex, so the rest of the figure goes through the same transform unmoved.
	vec3 view = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (instance_params.x < 0.0) {
		float d = length(view);
		view *= max(d - 0.15, 0.05) / max(d, 1e-4);
	}
	POSITION = PROJECTION_MATRIX * vec4(view, 1.0);
}

void fragment() {
	vec4 s = instance_params;
	bool pad = s.x < 0.0;
	float presence = abs(s.x);
	float appear = clamp(presence, 0.0, 1.0);
	float dissolve = clamp(presence - 1.0, 0.0, 1.0);
	bool failed = s.w > 1.5;
	float progress = clamp(failed ? s.w - 2.0 : s.w, 0.0, 1.0);
	float height = max(s.z - s.y, 0.25);
	float h = (world_pos.y - s.y) / height;
	float t = TIME * time_scale;

	vec3 glow = failed ? fail_color.rgb : glow_color.rgb;
	vec3 core = failed ? fail_color.rgb * 0.22 : core_color.rgb;
	vec3 color;
	float alpha;

	if (pad) {
		float r = length(pad_uv);
		if (r > 1.0) {
			discard;
		}
		// Clockwise seen from above, starting in front of the person (its -Z).
		float around = fract(atan(pad_uv.x, -pad_uv.y) / 6.2831853 + 1.0);
		float ring = smoothstep(0.80, 0.84, r) * (1.0 - smoothstep(0.93, 0.98, r));
		float arc = 1.0 - smoothstep(progress - 0.004, progress + 0.004, around);
		float pool = (1.0 - smoothstep(0.0, 0.85, r)) * 0.16;
		float wave = abs(fract(r * 1.4 - t * 0.45) - 0.5);
		float ripple = failed ? 0.0 : (1.0 - smoothstep(0.0, 0.05, wave)) * (1.0 - r);
		alpha = pool + ring * mix(0.16, 0.9, arc) + ripple * 0.10;
		color = mix(core, glow, clamp(ring * (0.4 + 0.6 * arc) + ripple, 0.0, 1.0)) + vec3(0.35) * ring * arc;
		alpha *= appear * (1.0 - dissolve);
	} else {
		float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
		float rim = pow(1.0 - facing, 2.4);

		// The waterline, with a slow ripple running round the body.
		float wobble = 0.006 * sin(world_pos.x * 23.0 + world_pos.z * 17.0 + t * 2.2) / height;
		float level = h - progress - wobble;
		float below = 1.0 - smoothstep(-0.003, 0.003, level);
		float waterline = exp(-pow(level * height / 0.009, 2.0)) * (progress < 0.999 ? 1.0 : 0.0);

		// Contour lines that drift upward while it loads and hold still once it has failed.
		float coord = (world_pos.y - (failed ? 0.0 : t * band_speed)) / band_spacing;
		float d = abs(fract(coord) - 0.5);
		float band = 1.0 - smoothstep(0.0, 0.06 + fwidth(coord) * 1.2, 0.5 - d);

		// What has arrived is lit from inside, a little whiter than the accent; what has not is dark glass.
		vec3 tone = mix(glow, mix(glow, vec3(1.0), 0.2), below);
		alpha = mix(0.04, 0.2, below) + rim * mix(0.3, 0.75, below) + band * mix(0.06, 0.22, below) + waterline * 0.85;
		color = mix(core, tone, clamp(rim * 1.2 + band * 0.6 + below * 0.45, 0.0, 1.0)) + vec3(0.9) * waterline;

		if (failed) {
			alpha *= 0.72 + 0.28 * sin(t * 2.4);
		}

		// Leaving: a line climbs from the feet over the head and takes the figure with it, glowing where it
		// cuts, while the avatar shows through what is left.
		if (dissolve > 0.0) {
			float edge = h - (dissolve * 1.25 - 0.1);
			if (edge < 0.0) {
				discard;
			}
			float cut = exp(-edge * height / 0.025);
			color += glow * cut * 1.6;
			alpha = max(alpha * (1.0 - dissolve * 0.6), cut);
		}
		alpha *= appear;
	}

	ALBEDO = color;
	ALPHA = clamp(alpha, 0.0, 1.0);
}
