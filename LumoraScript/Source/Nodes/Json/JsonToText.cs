// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// JSON written out as text: compact, or with indent two spaces a level. Keys come out in the order they
// were first set, the same on every machine. No value gives empty text.
[Node("JSON/To Text")]
public sealed class JsonToText : ValueNode
{
    [return: Port("text")]
    public static string Run(ScriptJson? json, bool indent = false)
        => json == null ? string.Empty : ScriptJsonWriter.Write(json, indent, int.MaxValue);
}
