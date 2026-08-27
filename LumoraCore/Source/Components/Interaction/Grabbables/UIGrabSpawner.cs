// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// The dispenser as a UI element: grip-pull a row or a tile in a panel and a copy of the template
// comes out into the hand, instead of the panel moving or a reference card appearing.
//
// Put it on the row that should be pullable. Rows without one keep whatever they already do, so a
// catalogue panel can mix spawner tiles with ordinary reference rows.
//
// This rides the pull path the reference cards already use: the hand resolves the exact UI slot
// under the pointer, walks up for a proxy source, and grabs whatever it is handed. A card source
// answers with a card; this one answers with a real copy of the template. Same path, same walk,
// same grab - nothing in the hand had to learn about dispensers, and the card rows are untouched
// because a row only behaves differently when someone puts this on it. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public class UIGrabSpawner : GrabSpawnerBase, IProxySource, ICustomInspectorUI
{
    // Face the copy the way the panel faces, rather than keeping the template's facing.
    public readonly Sync<bool> FaceLikePanel;

    public UIGrabSpawner()
    {
        FaceLikePanel = new Sync<bool>(this, true);
    }

    public IGrabbable? TryCreateProxy(Grabber grabber, in float3 spawnPoint)
    {
        if (grabber == null)
            return null;

        // The pull point is where the pointer met the panel, so the copy appears under the cursor
        // and the grab that follows keeps it there rather than snapping it to the hand.
        floatQ rotation = FaceLikePanel.Value && Slot != null && !Slot.IsDestroyed
            ? Slot.GlobalRotation
            : Template.Target?.GlobalRotation ?? floatQ.Identity;

        return Spawn(in spawnPoint, in rotation, overridePose: true);
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Template", Template.Target?.SlotName.Value ?? "none");
        int max = MaxInstances.Value;
        InspectorStats.AddRow(ui, "Live copies", max > 0 ? $"{LiveInstances} / {max}" : LiveInstances.ToString());
        InspectorStats.AddRow(ui, "State", DescribeBlock() ?? "ready");
    }
}
