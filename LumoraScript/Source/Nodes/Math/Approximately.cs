// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// Whether two values are the same within a tolerance. Numbers and vectors compare lane by lane: a lane passes
// when its difference is below epsilon, or below a millionth of the larger of the two where that is more. Two
// rotations are the same when the four-number dot product of them is at least 1 - epsilon; a rotation and its
// negation turn alike but do not count as the same here. -xlinka
[Node("Math/Compare/Approximately")]
public sealed class Approximately : ValueNode
{
    public static bool Run(float a, float b, float epsilon = 0.000001f)
        => MathF.Abs(a - b) < MathF.Max(0.000001f * MathF.Max(MathF.Abs(a), MathF.Abs(b)), epsilon);

    public static bool Run(float3 a, float3 b, float epsilon = 0.000001f)
        => Run(a.x, b.x, epsilon) && Run(a.y, b.y, epsilon) && Run(a.z, b.z, epsilon);

    public static bool Run(floatQ a, floatQ b, float epsilon = 0.000001f)
        => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w >= 1f - epsilon;
}
