// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

[Node("Math/Rotations/Rotate Vector")]
public sealed class RotateVector : ValueNode
{
    public static float3 Run(floatQ rotation, float3 vector) => rotation * vector;
}
