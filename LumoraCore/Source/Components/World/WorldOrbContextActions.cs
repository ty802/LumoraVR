// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.Network;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Components;

// The orb's own entries on the radial menu. It lives on the orb's slot, so the menu only collects it
// while the orb is in a hand: the menu walks the user root, and a held object hangs under it.
[ComponentCategory("Hidden")]
public sealed class WorldOrbContextActions : ContextMenuItemSource
{
    public readonly SyncRef<WorldOrb> Orb;

    private static readonly float[] OpenFill = { 0.10f, 0.24f, 0.34f, 0.92f };
    private static readonly float[] PortalFill = { 0.20f, 0.12f, 0.32f, 0.92f };
    private static readonly float[] BlockedFill = { 0.22f, 0.16f, 0.16f, 0.92f };
    private static readonly float[] DeleteFill = { 0.32f, 0.10f, 0.10f, 0.92f };

    public WorldOrbContextActions()
    {
        Orb = new SyncRef<WorldOrb>(this);
    }

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        var orb = Orb.Target;
        if (orb == null || orb.IsDestroyed || !orb.Enabled.Value)
            return;
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        page.AddItem(new ContextMenuItem
        {
            Group = ContextMenuGroup.Held,
            Label = orb.Kind.Value == WorldLinkKind.SavedWorld ? "Open" : "Join",
            FillColor = OpenFill,
            OnPressed = _ => orb.Open(),
        });

        if (orb.Kind.Value == WorldLinkKind.Session)
            AddDropPortal(page, orb);

        page.AddItem(new ContextMenuItem
        {
            Group = ContextMenuGroup.Held,
            Label = "Delete orb",
            FillColor = DeleteFill,
            OnPressed = _ => orb.Slot?.Destroy(),
        });
    }

    // Asked here rather than only in the handler, so a refusal is something the user can read: the
    // menu has already closed by the time an action runs, and a button that quietly does nothing
    // reads as a broken button.
    private void AddDropPortal(ContextMenuPage page, WorldOrb orb)
    {
        // The orb is in a hand, so the world it lives in IS the world the user is standing in. Both
        // gates bind: the room has to allow items, and the session has to be one anybody can reach.
        var into = orb.World;
        bool allowed = WorldLink.CanSpawnIn(into, out var reason);
        if (allowed && orb.JoinUrl.Value == null)
        {
            allowed = false;
            reason = "no join address yet";
        }

        page.AddItem(new ContextMenuItem
        {
            Group = ContextMenuGroup.Held,
            Label = allowed ? "Drop portal" : reason ?? "Drop portal",
            IsEnabled = allowed,
            FillColor = allowed ? PortalFill : BlockedFill,
            OnPressed = allowed ? item => WorldPortal.Drop(into, orb, into.LocalUser, out _) : null,
        });
    }
}
