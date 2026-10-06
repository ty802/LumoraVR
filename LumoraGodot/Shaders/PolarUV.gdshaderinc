// Polar UV mapping: the texture wrapped around the centre of the UV square, angle across U and radius
// along V. polar_power bends the radius, so 2 crowds the picture toward the rim and 0.5 toward the middle.
//
// The angle is measured with V counting UP from the bottom, the way imported UVs were authored, so a
// polar picture keeps its winding; the coordinate it lands on is then turned back to this engine's
// top-down V when it samples. Without that the radius would run from the image's top row instead of its
// bottom and every radial gradient would come out inside out.
//
// The seam is where the angle wraps from 1 back to 0. Across it the derivative is the whole texture wide,
// which drops the sample to the smallest mip in a one-pixel line, so the gradient comes from whichever of
// the plain or the half-turned coordinate changes less at this pixel. -xlinka

vec2 polar_uv(vec2 uv, float radius_pow) {
	vec2 raw = vec2(uv.x, 1.0 - uv.y) * 2.0 - 1.0;
	float radius = pow(length(raw), radius_pow);
	float angle = atan(raw.x, raw.y) + PI;
	return vec2(angle / TAU, radius);
}

// Scale and offset applied, still in the upward-V orientation polar_uv returns.
vec2 polar_transform(vec2 polar, vec2 scale, vec2 offset, out vec2 grad_x, out vec2 grad_y) {
	vec2 p = polar * scale + offset;
	vec2 turned = vec2(mod(abs(p.x) + 0.5, 1.0), p.y);
	grad_x = dFdx(p);
	grad_y = dFdy(p);
	vec2 turned_x = dFdx(turned);
	vec2 turned_y = dFdy(turned);
	if (length(turned_x) + length(turned_y) < length(grad_x) + length(grad_y)) {
		grad_x = turned_x;
		grad_y = turned_y;
	}
	return p;
}

vec4 polar_sample(sampler2D tex, vec2 p, vec2 grad_x, vec2 grad_y) {
	return textureGrad(tex, vec2(p.x, 1.0 - p.y), vec2(grad_x.x, -grad_x.y), vec2(grad_y.x, -grad_y.y));
}
