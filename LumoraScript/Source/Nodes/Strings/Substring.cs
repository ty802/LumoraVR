// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// length characters from start, cut short at the end of the text rather than failing.
[Node("Strings/Substring")]
public sealed class Substring : ValueNode
{
    public static string Run(string? text, int start, int length)
    {
        text ??= string.Empty;
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        return text.Substring(start, length);
    }
}
