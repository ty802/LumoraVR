// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// JSON null, for a key or item that is there with nothing in it.
[Node("JSON/Values/Null")]
public sealed class JsonNull : ValueNode
{
    [return: Port("json")]
    public static ScriptJson Run() => ScriptJsonValue.Null;
}
