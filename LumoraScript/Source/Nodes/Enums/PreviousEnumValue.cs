// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// The value with the next lower number, the highest again before the first. Two names for one number
// are one value. A value the enum does not name gives the first one below it.
[Node("Enums/Previous Value")]
public sealed class PreviousEnumValue<T> : ValueNode where T : struct, Enum
{
    [return: Port("previous")]
    public static T Run(T value) => EnumSteps<T>.Shift(value, -1);
}
