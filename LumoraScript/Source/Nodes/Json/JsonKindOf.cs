// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// What a JSON value is: Null, Bool, Number, String, Array or Object. No value reads as Null.
[Node("JSON/Values/Kind")]
public sealed class JsonKindOf : ValueNode
{
    [return: Port("kind")]
    public static ScriptJsonKind Run(ScriptJson? json) => json?.Kind ?? ScriptJsonKind.Null;
}
