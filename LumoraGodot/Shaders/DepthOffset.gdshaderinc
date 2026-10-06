// Depth bias, the rasterizer's polygon offset done by hand, because Godot has no render state for it.
// offset_factor scales the surface's depth slope at this pixel, offset_units the smallest step a float
// depth buffer resolves at this depth. Negative pulls the surface toward the camera, as authored.
//
// Depth is REVERSED here, near is 1 and far is 0, so pulling closer RAISES the stored value. Writing DEPTH
// costs the surface its early depth rejection, which is why only the generated variant that asks for a
// bias includes this at all. -xlinka
uniform float offset_factor = 0.0;
uniform float offset_units = 0.0;

float offset_depth(float z) {
	float slope = max(abs(dFdx(z)), abs(dFdy(z)));
	float step_size = exp2(floor(log2(max(z, 1e-30))) - 23.0);
	return clamp(z - (offset_factor * slope + offset_units * step_size), 0.0, 1.0);
}
