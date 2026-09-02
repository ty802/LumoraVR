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

// Wrapping used to live here as an animation-only enum. It is Lumora.Core.PlaybackLoopMode now,
// because a clip and a media stream and a timeline all want the same three answers and a second
// three-case enum next to the first one is how the two drift apart.

public static class KeyframeInterpolationExtensions
{
    public static bool RequiresTangents(this KeyframeInterpolation interpolation)
        => interpolation == KeyframeInterpolation.Cubic;
}
