// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// An object with nothing in it, to set keys on.
[Node("JSON/Objects/Empty Object")]
public sealed class JsonEmptyObject : ValueNode
{
    [return: Port("object")]
    public static ScriptJsonObject Run() => ScriptJsonObject.Empty;
}
