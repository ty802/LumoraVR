// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A JSON value that is an array, as one; ok is false, and the array nothing, for any other.
[Node("JSON/Values/As Array")]
public sealed class JsonAsArray : ValueNode
{
    [return: Port("array")]
    public static ScriptJsonArray? Run(ScriptJson? json, out bool ok)
    {
        var array = json as ScriptJsonArray;
        ok = array != null;
        return array;
    }
}
