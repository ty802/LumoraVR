// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Exact and case sensitive.
[Node("Strings/Ends With")]
public sealed class EndsWith : ValueNode
{
    public static bool Run(string? text, string? end) => (text ?? string.Empty).EndsWith(end ?? string.Empty, StringComparison.Ordinal);
}
