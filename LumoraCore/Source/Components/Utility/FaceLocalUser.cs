// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Yaw-billboards the slot toward the local viewer. Every peer computes its
// own facing locally (no sync generation), so each viewer sees the plate
// turned toward themselves - per-user visual divergence by design. - xlinka
[ComponentCategory("Utility")]
public class FaceLocalUser : Component
{
    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var parent = Slot?.Parent;
        if (parent == null)
            return;

        var viewerHead = World?.LocalUser?.Root?.HeadSlot;
        if (viewerHead == null || viewerHead.IsDestroyed)
            return;

        var toViewer = viewerHead.GlobalPosition - Slot!.GlobalPosition;
        toViewer.y = 0f;
        if (toViewer.LengthSquared < 1e-6f)
            return;

        // Point the AWAY direction at LookRotation so the glyph front face
        // (readable side) is what the viewer sees - facing the look axis
        // itself presents the mirrored back of the double-sided quads.
        var global = floatQ.LookRotation((-toViewer).Normalized, float3.Up);
        var local = parent.GlobalRotationToLocal(global);

        float dot = floatQ.Dot(local, Slot.LocalRotation.Value);
        if (1f - (dot < 0 ? -dot : dot) > 1e-6f)
            Slot.LocalRotation.SetValueSilently(local, change: true);
    }
}
