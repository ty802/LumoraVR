// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Where find first starts in text, or -1. Exact and case sensitive.
[Node("Strings/Index Of")]
public sealed class IndexOf : ValueNode
{
    [return: Port("index")]
    public static int Run(string? text, string? find) => (text ?? string.Empty).IndexOf(find ?? string.Empty, StringComparison.Ordinal);
}
