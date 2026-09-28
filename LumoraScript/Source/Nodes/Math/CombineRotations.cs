// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Turning by first and then by then, as one rotation.
[Node("Math/Rotations/Combine")]
public sealed class CombineRotations : ValueNode
{
    [return: Port("rotation")]
    public static floatQ Run(floatQ first, floatQ then) => then * first;
}
