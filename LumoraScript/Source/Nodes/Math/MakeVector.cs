// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

[Node("Math/Vectors/Make Vector")]
public sealed class MakeVector : ValueNode
{
    [return: Port("vector")] public static float2 Run(float x, float y) => new(x, y);
    [return: Port("vector")] public static float3 Run(float x, float y, float z) => new(x, y, z);
    [return: Port("vector")] public static float4 Run(float x, float y, float z, float w) => new(x, y, z, w);
    [return: Port("vector")] public static int2 Run(int x, int y) => new(x, y);
    [return: Port("vector")] public static int3 Run(int x, int y, int z) => new(x, y, z);
    [return: Port("vector")] public static int4 Run(int x, int y, int z, int w) => new(x, y, z, w);
    [return: Port("vector")] public static bool2 Run(bool x, bool y) => new(x, y);
    [return: Port("vector")] public static bool3 Run(bool x, bool y, bool z) => new(x, y, z);
    [return: Port("vector")] public static bool4 Run(bool x, bool y, bool z, bool w) => new(x, y, z, w);
}
