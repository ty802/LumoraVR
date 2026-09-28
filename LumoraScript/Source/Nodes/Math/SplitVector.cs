// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

[Node("Math/Vectors/Split Vector")]
public sealed class SplitVector : ValueNode
{
    public static void Run(float2 vector, out float x, out float y)
    {
        x = vector.x;
        y = vector.y;
    }

    public static void Run(float3 vector, out float x, out float y, out float z)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
    }

    public static void Run(float4 vector, out float x, out float y, out float z, out float w)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
        w = vector.w;
    }

    public static void Run(int2 vector, out int x, out int y)
    {
        x = vector.x;
        y = vector.y;
    }

    public static void Run(int3 vector, out int x, out int y, out int z)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
    }

    public static void Run(int4 vector, out int x, out int y, out int z, out int w)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
        w = vector.w;
    }

    public static void Run(bool2 vector, out bool x, out bool y)
    {
        x = vector.x;
        y = vector.y;
    }

    public static void Run(bool3 vector, out bool x, out bool y, out bool z)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
    }

    public static void Run(bool4 vector, out bool x, out bool y, out bool z, out bool w)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
        w = vector.w;
    }
}
