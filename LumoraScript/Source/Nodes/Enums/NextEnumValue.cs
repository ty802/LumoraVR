// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// The value with the next higher number, the lowest again after the last. Two names for one number are
// one value. A value the enum does not name gives the first one above it.
[Node("Enums/Next Value")]
public sealed class NextEnumValue<T> : ValueNode where T : struct, Enum
{
    [return: Port("next")]
    public static T Run(T value) => EnumSteps<T>.Shift(value, 1);
}
