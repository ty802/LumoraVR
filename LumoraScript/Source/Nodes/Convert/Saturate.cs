// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Numerics;

namespace Lumora.Script;

// What the whole-number conversions share: a float goes toward zero, held at the target's end when it is
// past it, and NaN is 0. NaN is caught here rather than left to the cast, which not every machine
// answers the same way. -xlinka
internal static class Saturate
{
    public static T Whole<T>(double value) where T : IBinaryInteger<T>
        => double.IsNaN(value) ? T.Zero : T.CreateSaturating(value);
}
