// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Text with its %XX codes read back into the characters they stand for. Anything that is not a whole
// code is left as it is.
[Node("URLs/Unescape URL Text")]
public sealed class UnescapeUrlText : ValueNode
{
    public static string Run(string? text) => string.IsNullOrEmpty(text) ? string.Empty : Uri.UnescapeDataString(text);
}
