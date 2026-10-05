// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop;

// The one conversion a package needs and was not getting: package data is LEFT-handed, this engine is
// right-handed, and reading one's numbers straight into the other is a mirror.
//
// It is a quiet mirror. Nothing is obviously broken - the avatar stands up, faces forward, the arms
// are where arms go - it is just that every right is on the left. Lettering on a leg reads backwards.
// A prop's labels read backwards. Every bone named .R sits on the physical left, so the rig code had
// to detect that from the toes and re-key whole limb pairs, and every mesh arrived inside-out, so the
// mesh reader had to reverse its winding. Two compensations downstream of one missing reflection,
// and the hands in VR still came out on the wrong arms.
//
// This is that reflection, applied once, where the data comes in. Across the YZ plane - negating X -
// rather than Z, because package data and this engine both call +Z Forward and every system downstream
// (facing, IK, equip) reads that way; negating Z would fix chirality and turn every avatar around. -xlinka
internal static class PackageHandedness
{
    // A position, or an ordinary direction: mirror the X.
    public static float3 Point(in float3 v) => new float3(-v.x, v.y, v.z);

    // An ANGULAR quantity - angular velocity, Euler angles, torque - is a pseudovector, and a mirror
    // flips the components that lie IN the mirror plane, not the one across it.
    public static float3 Axial(in float3 v) => new float3(v.x, -v.y, -v.z);

    // Conjugating a rotation by the reflection. The vector part is axial.
    public static floatQ Rotation(in floatQ q) => new floatQ(q.x, -q.y, -q.z, q.w);

    // M' = R M R with R = diag(-1, 1, 1, 1): negate row 0 and column 0, which leaves (0,0) alone
    // because it is negated twice. Written on the columns, so it is the same operation whichever way
    // the matrix is stored.
    public static float4x4 Matrix(in float4x4 m) => new float4x4(
        new float4(m.c0.x, -m.c0.y, -m.c0.z, -m.c0.w),
        new float4(-m.c1.x, m.c1.y, m.c1.z, m.c1.w),
        new float4(-m.c2.x, m.c2.y, m.c2.z, m.c2.w),
        new float4(-m.c3.x, m.c3.y, m.c3.z, m.c3.w));

    // Which float3 MEMBERS of a component are spatial. A package stores sizes, scales and colours in
    // the same shape as positions and directions, and only the name says which is which. A miss here
    // leaves one member mirrored, which is the state everything was in before.
    private static readonly string[] AxialNames = { "Angular", "Euler", "Torque", "Spin" };

    private static readonly string[] PolarNames =
    {
        "Position", "Offset", "Direction", "Normal", "Velocity", "Axis", "Point", "Center", "Centre",
        "Force", "Gravity", "Anchor", "Pivot", "Origin", "Translation", "Delta",
    };

    private static readonly string[] NeverNames = { "Scale", "Size", "Extent", "Range", "Color", "Colour", "HSV" };

    public static float3 Member(string memberName, in float3 v)
    {
        if (Contains(memberName, NeverNames))
            return v;
        if (Contains(memberName, AxialNames))
            return Axial(v);
        if (Contains(memberName, PolarNames))
            return Point(v);
        return v;
    }

    private static bool Contains(string name, string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (name.IndexOf(needles[i], StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }
}
