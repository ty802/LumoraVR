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

        // Build the yaw directly: the readable front of quad/canvas content
        // is its +Z side, so point local +Z at the viewer. floatQ.LookRotation
        // builds its matrix from basis ROWS (returns the inverse rotation) so
        // headings come out negated and planes go edge-on at oblique angles -
        // avoid it for facing math.
        float yaw = System.MathF.Atan2(toViewer.x, toViewer.z);
        var global = floatQ.AxisAngle(float3.Up, yaw);
        var local = parent.GlobalRotationToLocal(global);

        float dot = floatQ.Dot(local, Slot.LocalRotation.Value);
        if (1f - (dot < 0 ? -dot : dot) > 1e-6f)
        {
            // Per-viewer local billboard of a (possibly REMOTE) user's nameplate. On an observer the write's actor
            // is the observer's own user, who doesn't own the remote plate -> denied, and the badge stops facing
            // you. Local-only visual, no sync generated, so bypass the gate. -xlinka
            using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
            Slot.LocalRotation.SetValueSilently(local, change: true);
        }
    }
}
