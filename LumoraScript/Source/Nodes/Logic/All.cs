// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Whether every lane of a bool vector is true.
[Node("Logic/All")]
public sealed class All : ValueNode
{
    public static bool Run(bool2 value) => value.All;
    public static bool Run(bool3 value) => value.All;
    public static bool Run(bool4 value) => value.All;
}
