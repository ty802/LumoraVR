// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// The number behind an enum value. An enum wider than an int gives its low 32 bits.
[Node("Enums/Enum To Number")]
public sealed class EnumToNumber<T> : ValueNode where T : struct, Enum
{
    [return: Port("number")]
    public static int Run(T value) => EnumSteps<T>.ToInt(value);
}
