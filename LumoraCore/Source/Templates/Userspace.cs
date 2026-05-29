// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.UI;
using Lumora.Core.Logging;

namespace Lumora.Core.Templates
{
    /// <summary>
    /// Manages userspace world creation and setup.
    /// </summary>
    public static class Userspace
    {
        /// <summary>
        /// Setup the userspace world for the engine.
        /// Userspace is a private overlay world for UI and settings.
        /// </summary>
        public static World SetupUserspace(Engine engine)
        {
            Logger.Log("Userspace: Setting up userspace world");

            // Create local userspace world
            var world = World.LocalWorld(engine, "Userspace", (w) =>
            {
                Logger.Log("Userspace: Initializing userspace world");

                // Create root structure
                var userspaceRoot = w.RootSlot.AddSlot("UserspaceRoot");

                // Ref-style userspace dashboard root. UserspaceDashboard owns the
                // open state and positions the Helio dash surface in front of the
                // focused user's view when shown.
                var dashboardSlot = userspaceRoot.AddSlot("UserspaceDashboard");
                var dashboard = dashboardSlot.AttachComponent<UserspaceDashboard>();
                dashboard.Close();

                // Create Context Menu (radial arc menu, toggled with A/X or middle mouse)
                var contextMenuSlot = userspaceRoot.AddSlot("ContextMenu");
                contextMenuSlot.AttachComponent<ContextMenuSystem>();

                // ── Default root items ─────────────────────────────────────────
                // these are always present. additional items are contributed at open-time
                // by RootContextMenuItem / ContextMenuItemSource components. - xlinka

                var dashboardItem = contextMenuSlot.AttachComponent<RootContextMenuItem>();
                dashboardItem.Label.Value = "Dashboard";
                dashboardItem.IconPath.Value = "res://Icons/dashboard.png";
                dashboardItem.Priority.Value = 100;
                dashboardItem.Pressed += _ => dashboard.Toggle();

                var closeItem = contextMenuSlot.AttachComponent<RootContextMenuItem>();
                closeItem.Label.Value    = "Close Menu";
                closeItem.IconPath.Value = "res://Icons/close.png";
                closeItem.Priority.Value = -100;

                Logger.Log("Userspace: Context menu created");
                Logger.Log("Userspace: Userspace world initialized");
            });

            // Register with WorldManager as userspace world
            // (UserspaceWorld setter automatically calls PrivateOverlayWorld which adds to managed worlds)
            engine.WorldManager.UserspaceWorld = world;

            Logger.Log("Userspace: Userspace setup complete");
            return world;
        }
    }
}
