// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

[Node("World/Slots/Name", Capability.WorldRead)]
public sealed class SlotName : ReadNode
{
    [return: Port("name")]
    public static string Run(ScriptContext ctx, Slot? slot)
        => ScriptReads.Visible(ctx, slot) ? slot!.Name.Value ?? string.Empty : string.Empty;
}
