shader_type sky;

// Cubemap sky. Rotation is a yaw applied to the LOOKUP direction, not to the texture, so turning the
// sky costs the same as not turning it.
uniform samplerCube sky_cubemap : source_color, filter_linear_mipmap;
uniform float sky_rotation = 0.0;
uniform float sky_exposure : hint_range(0.0, 8.0) = 1.0;

void sky() {
    vec3 dir = normalize(EYEDIR);

    float s = sin(sky_rotation);
    float c = cos(sky_rotation);
    dir = vec3(c * dir.x - s * dir.z, dir.y, s * dir.x + c * dir.z);

    COLOR = texture(sky_cubemap, dir).rgb * sky_exposure;
}
