shader_type sky;

// A 360 picture as the sky, looked up the way Projection360 looks one up, so a panorama lands where it
// does on a sphere around the viewer: same span, angle offset and texture transform. Rotation is a yaw
// on the lookup direction, as on the cubemap sky.
const float PANO_PI = 3.14159265359;
const float PANO_TAU = 6.28318530718;

uniform sampler2D sky_panorama : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float sky_rotation = 0.0;
uniform float sky_exposure : hint_range(0.0, 8.0) = 1.0;
// The material's own exposure and tint, on the picture only.
uniform float picture_exposure = 1.0;
uniform vec4 picture_tint : source_color = vec4(1.0);
// xy the angular span, zw the angle offset, all radians.
uniform vec4 field_of_view = vec4(6.28318530718, 3.14159265359, 0.0, 0.0);
uniform vec4 texture_transform = vec4(1.0, 1.0, 0.0, 0.0);
// 0 Clip, 1 Color, 2 Clamp. Nothing lies behind the sky, so a clipped edge shows black.
uniform int outside_mode = 0;
uniform vec4 outside_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

void sky() {
    vec3 dir = normalize(EYEDIR);

    float s = sin(sky_rotation);
    float c = cos(sky_rotation);
    dir = vec3(c * dir.x - s * dir.z, dir.y, s * dir.x + c * dir.z);

    // Projection360 works in the frame the content was authored in, where looking along dir reads
    // (x, -y, -z). Its UVs are v-up and turn to our top-origin rows only on the fetch.
    vec3 d = vec3(dir.x, -dir.y, -dir.z);
    vec2 angle = vec2(atan(d.x, d.z), acos(clamp(d.y, -1.0, 1.0)) - PANO_PI * 0.5);
    angle += field_of_view.xy * 0.5;
    angle += field_of_view.zw;
    angle = mod(angle, vec2(PANO_TAU, PANO_PI));
    vec2 uv = angle / field_of_view.xy;

    bool outside = uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;
    if (outside_mode == 2) {
        uv = clamp(uv, vec2(0.0), vec2(1.0));
        outside = false;
    }

    uv = uv * texture_transform.xy + texture_transform.zw;
    vec3 picture = textureLod(sky_panorama, vec2(uv.x, 1.0 - uv.y), 0.0).rgb * picture_exposure * picture_tint.rgb;

    if (outside) {
        picture = outside_mode == 1 ? outside_color.rgb : vec3(0.0);
    }

    COLOR = picture * sky_exposure;
}
