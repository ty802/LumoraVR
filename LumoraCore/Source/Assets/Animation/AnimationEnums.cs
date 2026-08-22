// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets.Animation;

// Which storage family a track uses. The family decides how a sample between two keys is produced,
// not what the values mean.
public enum AnimationTrackType : byte
{
    // Keys carry an interpolation mode and optional tangents; samples blend between keys.
    Curve = 0,

    // Keys hold their value until the next key. No blending, ever.
    Discrete = 1
}

// Per-key blend rule for a curve track. Stored per key so one track can hold a stepped section and
// a smooth section without splitting.
public enum KeyframeInterpolation : byte
{
    // Hold this key's value until the next key.
    Step = 0,

    // Straight blend to the next key (slerp for rotations).
    Linear = 1,

    // Cubic Hermite through this key's out tangent and the next key's in tangent.
    Cubic = 2
}

// Value type tag written into the binary stream. These numbers are FILE FORMAT: never renumber an
// existing entry, only append. The set is deliberately limited to the primitives the data model can
// actually hold - there is no double or half variant because no field uses one. -xlinka
public enum AnimationElementType : byte
{
    Bool = 0,
    Int = 1,
    Long = 2,
    Float = 3,
    Float2 = 4,
    Float3 = 5,
    Float4 = 6,
    FloatQ = 7,
    Color = 8,
    ColorHDR = 9,
    String = 10
}

public enum AnimationWrapMode : byte
{
    // Clamp to the clip range and stay on the last frame.
    Once = 0,

    Loop = 1,

    PingPong = 2
}

public static class AnimationWrap
{
    // Non-positive duration collapses to 0 so a keyless clip can never produce NaN.
    public static float Wrap(float time, float duration, AnimationWrapMode mode)
    {
        if (!float.IsFinite(time) || duration <= 0f)
            return 0f;

        switch (mode)
        {
            case AnimationWrapMode.Loop:
            {
                float t = time % duration;
                return t < 0f ? t + duration : t;
            }
            case AnimationWrapMode.PingPong:
            {
                float period = duration * 2f;
                float t = time % period;
                if (t < 0f)
                    t += period;
                return t <= duration ? t : period - t;
            }
            default:
                return time < 0f ? 0f : (time > duration ? duration : time);
        }
    }

    // Loop and ping-pong never finish.
    public static bool IsFinished(float time, float duration, AnimationWrapMode mode)
        => mode == AnimationWrapMode.Once && duration > 0f && time >= duration;

    public static bool RequiresTangents(this KeyframeInterpolation interpolation)
        => interpolation == KeyframeInterpolation.Cubic;
}
