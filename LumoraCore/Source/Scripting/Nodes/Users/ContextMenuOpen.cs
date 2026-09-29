// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Whether a person has their context menu open, as their machine last said.
[Node("Users/Context Menu Open", Capability.UsersRead)]
public sealed class ContextMenuOpen : ReadNode
{
    [return: Port("open")]
    public static bool Run(ScriptContext ctx, User? user)
    {
        if (!ScriptReads.Visible(ctx, user) || ScriptReads.RootOf(user)?.Slot is not { IsDestroyed: false } body)
            return false;
        var menu = body.GetComponentInChildren<ContextMenuSystem>();
        return menu is { IsDestroyed: false } && menu.IsOpen.Value;
    }
}
