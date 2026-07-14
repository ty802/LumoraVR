// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// marks a UI row as pullable: gripping the row spawns a ReferenceProxy card for the element this
// source points at (the row's sync member, its component, or its slot) instead of grabbing the
// panel. attached by the inspector builders on member rows, component headers, and tree rows.
// also serves the row's chip button: pressing it pops the card out in front of the panel for
// setups where grip is awkward.
[ComponentCategory("Utility/Inspectors")]
public class ReferenceProxySource : Component, IProxySource
{
    public readonly SyncRef<IWorldElement> Target;

    public ReferenceProxySource()
    {
        Target = new SyncRef<IWorldElement>(this);
    }

    public IGrabbable? TryCreateProxy(Grabber grabber, in float3 spawnPoint)
    {
        var target = Target.Target;
        if (target == null || target.IsDestroyed || World == null)
            return null;
        return ReferenceProxy.Spawn(World, target, spawnPoint);
    }

    // button path: spawns the card just off the pressed point, toward the presser's head
    [SyncMethod]
    public void OnPullPressed(Button button, UIInteractionContext context)
    {
        var target = Target.Target;
        if (target == null || target.IsDestroyed || World == null)
            return;
        var head = World.LocalUser?.Root?.HeadSlot;
        float3 point = context.WorldPoint;
        float3 offset = head != null && (head.GlobalPosition - point).LengthSquared > 1e-6f
            ? (head.GlobalPosition - point).Normalized * 0.25f
            : float3.Up * 0.1f;
        ReferenceProxy.Spawn(World, target, point + offset);
    }
}
