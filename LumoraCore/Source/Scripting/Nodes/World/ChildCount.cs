// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

[Node("World/Slots/Child Count", Capability.WorldRead)]
public sealed class ChildCount : ReadNode
{
    [return: Port("count")]
    public static int Run(ScriptContext ctx, Slot? slot) => ScriptReads.Visible(ctx, slot) ? slot!.ChildCount : 0;
}
