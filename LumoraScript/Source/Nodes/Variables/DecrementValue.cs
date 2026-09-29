// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Takes "by", one unless set, from a stored value and writes it back, like Increment Value; whole numbers
// wrap below their smallest value.
[Node("Variables/Decrement Value")]
public sealed class DecrementValue<T> : ScriptNode
{
    public static bool IsValidGenericType => NodeRegistry.IsSteppable(typeof(T));
}
