// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Keeps the slot positioned at its user's head plus a vertical offset
// (nameplate anchor). Runs on every peer from the replicated head transform
// and writes the result locally (no sync generation) - broadcasting it would
// duplicate the head stream every frame. Offset scales with the user so
// plates stay above scaled avatars. - xlinka
[ComponentCategory("Utility")]
public class PositionAtUser : Component
{
    public readonly Sync<float> VerticalOffset = new();

    public override void OnInit()
    {
        base.OnInit();
        VerticalOffset.Value = 0.25f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var userRoot = Slot?.ActiveUserRoot;
        var head = userRoot?.HeadSlot;
        var parent = Slot?.Parent;
        if (head == null || head.IsDestroyed || parent == null)
            return;

        float scale = userRoot!.GlobalScale;
        var target = head.GlobalPosition + float3.Up * (VerticalOffset.Value * scale);
        var local = parent.GlobalPointToLocal(target);

        if ((Slot!.LocalPosition.Value - local).LengthSquared > 1e-10f)
        {
            // This anchor lives under the (possibly REMOTE) user's root, so on an observer the write's actor is
            // the observer's own user, who doesn't own it -> the permission gate denies it and the badge stops
            // tracking the head. It's a purely-local visual follow of already-replicated head data (no sync
            // generated), so bypass the gate - same treatment as the remote-body stream apply. -xlinka
            using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
            Slot.LocalPosition.SetValueSilently(local, change: true);
        }
    }
}
