// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// Adds the "Dashboard" toggle to the context menu. Resolved at open time on
// whichever peer opened the menu - the dashboard lives in that peer's own
// userspace world, so this cannot be wired as a build-time event. - xlinka
[ComponentCategory("UI/Context Menu")]
public class DashboardContextAction : ContextMenuItemSource
{
    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        // Only the local user's own menu offers their dashboard.
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var userspace = Engine.Current?.WorldManager?.UserspaceWorld;
        var dashboard = userspace?.RootSlot?.GetComponentInChildren<UserspaceDashboard>();
        if (dashboard == null)
            return;

        page.AddItem(new ContextMenuItem
        {
            Label = "Dashboard",
            FillColor = new[] { 0.13f, 0.18f, 0.30f, 0.92f },
            OnPressed = _ => dashboard.Toggle(),
        });
    }
}
