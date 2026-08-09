// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.UI;

// attach to any slot in the world or user hierarchy. ContextMenuSystem collects every active
// ContextMenuItemSource and calls PopulateContextMenu() on each when the menu opens.
[ComponentCategory("UI/Context Menu")]
public class ContextMenuItemSource : Component
{
    // higher priority sources run first (their items appear first)
    public readonly Sync<int> Priority = null!;

    public override void OnAwake()
    {
        base.OnAwake();
    }

    // called each time the menu opens, before it's shown. items already added by higher-priority
    // sources are visible in page.Items. context carries the summoning pointer and the slot it was
    // aimed at, for contextual actions like "Equip Avatar".
    public virtual void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context) { }
}
