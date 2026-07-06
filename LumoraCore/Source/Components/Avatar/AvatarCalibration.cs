// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Avatar.IK;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Computes avatar calibration reference poses (view, hand grips, feet, pelvis) directly from a
/// <see cref="HumanoidRig"/>, so the avatar can be set up automatically with no manual placement.
///
/// The key over a naive "copy the bone's rotation" approach is that bone rotations are arbitrary per
/// model - a raw hand-bone rotation makes a useless grip. Instead each reference frame is rebuilt
/// from the body geometry: facing from the spine + shoulder line, grips from the forearm + arm-bend
/// plane, feet/pelvis flattened to the ground. Results feed <see cref="AvatarReferencePoint"/>s that
/// <see cref="AvatarIK"/> reads.
/// </summary>
public static class AvatarCalibration
{
    /// <summary>A computed reference pose, expressed in the avatar root's local space.</summary>
    public struct RefPose
    {
        public bool Valid;
        public float3 LocalPosition;
        public floatQ LocalRotation;
    }

    // Eyes sit a little in front of the head bone.
    private const float ViewForwardOffset = 0.06f;

    /// <summary>
    /// Derive the body's world axes from the rig: <paramref name="up"/> along the spine,
    /// <paramref name="forward"/> the facing direction (disambiguated by the head), <paramref name="right"/>
    /// the shoulder line. Falls back to world axes / the head's facing when bones are missing.
    /// </summary>
    public static bool TryComputeBodyAxes(HumanoidRig rig, out float3 up, out float3 right, out float3 forward)
    {
        up = float3.Up;
        right = float3.Right;
        forward = float3.Backward;
        if (rig == null)
            return false;

        var head = rig.TryGetBone(BodyNode.Head);
        if (head == null || head.IsDestroyed)
            return false;

        var hips = rig.TryGetBone(BodyNode.Hips);
        if (hips != null && !hips.IsDestroyed)
        {
            var u = head.GlobalPosition - hips.GlobalPosition;
            if (u.LengthSquared > 1e-6f)
                up = u.Normalized;
        }

        // Forward from the rig's geometric front. GuessForwardAxis builds it from the shoulder line and
        // disambiguates the sign against the toes, so it stays correct even when the rig's left/right arm
        // bones are authored on the swapped physical side - which would otherwise reverse a raw
        // Cross(up, shoulder-line) and place every reference (view/grips/feet) 180 degrees backward. -xlinka
        float3 fwd;
        var geometricFwd = rig.GuessForwardAxis();
        if (geometricFwd.HasValue && geometricFwd.Value.LengthSquared > 1e-6f)
        {
            fwd = geometricFwd.Value;
        }
        else
        {
            var leftUpper = rig.TryGetBone(BodyNode.LeftUpperArm);
            var rightUpper = rig.TryGetBone(BodyNode.RightUpperArm);
            if (leftUpper != null && rightUpper != null && !leftUpper.IsDestroyed && !rightUpper.IsDestroyed)
            {
                var r = rightUpper.GlobalPosition - leftUpper.GlobalPosition;   // toward the avatar's right
                right = r.LengthSquared > 1e-6f ? r.Normalized : float3.Right;
                fwd = float3.Cross(up, right);                                  // points out the front
            }
            else
            {
                fwd = head.GlobalRotation * float3.Backward;
            }
        }
        if (fwd.LengthSquared < 1e-6f)
            fwd = float3.Backward;
        fwd = fwd.Normalized;

        forward = fwd;
        right = float3.Cross(forward, up);
        right = right.LengthSquared > 1e-6f ? right.Normalized : float3.Right;
        return true;
    }

    /// <summary>
    /// Point the avatar root's forward (-Z) at the body's geometric front WITHOUT moving anything visibly:
    /// the root frame is yawed onto the mesh front by the exact delta and every direct child is restored to
    /// its world pose. Equip resets the root to identity, so the root frame IS the worn facing - this is what
    /// makes an avatar walk snout-first regardless of how the model was authored or dropped in the world.
    /// Exact-angle (no binary 180 guess), yaw-only (root stays upright), idempotent. Run it before references
    /// are baked; re-running after only rewrites the same frame. Returns true when the frame moved. -xlinka
    /// </summary>
    public static bool AlignAvatarFacing(Slot avatarRoot, HumanoidRig rig)
    {
        if (avatarRoot == null || avatarRoot.IsDestroyed || rig == null || rig.IsDestroyed)
            return false;

        var geom = rig.GuessForwardAxis();
        if (!geom.HasValue || geom.Value.LengthSquared < 1e-6f)
            return false;

        float3 front = geom.Value;
        front.y = 0f;
        float3 rootFwd = avatarRoot.GlobalRotation * float3.Backward;
        rootFwd.y = 0f;
        if (front.LengthSquared < 1e-6f || rootFwd.LengthSquared < 1e-6f)
            return false;
        front = front.Normalized;
        rootFwd = rootFwd.Normalized;

        float delta = System.MathF.Atan2(front.x, front.z) - System.MathF.Atan2(rootFwd.x, rootFwd.z);
        while (delta > System.MathF.PI) delta -= 2f * System.MathF.PI;
        while (delta < -System.MathF.PI) delta += 2f * System.MathF.PI;
        if (System.MathF.Abs(delta) < 0.01f)
            return false;

        // Counter-restore the children so the world appearance is untouched - only the FRAME turns. The
        // references/markers/bones are all read as live transforms, so they stay consistent by construction.
        var restore = new System.Collections.Generic.List<(Slot child, float3 pos, floatQ rot)>();
        foreach (var child in avatarRoot.Children)
        {
            if (child != null && !child.IsDestroyed)
                restore.Add((child, child.GlobalPosition, child.GlobalRotation));
        }

        avatarRoot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, delta) * avatarRoot.GlobalRotation;

        foreach (var (child, pos, rot) in restore)
        {
            child.GlobalPosition = pos;
            child.GlobalRotation = rot;
        }

        Lumora.Core.Logging.Logger.Log(
            $"[IK-FACING-ALIGN] root '{avatarRoot.SlotName.Value}' frame yawed {delta * 180f / System.MathF.PI:F1} deg " +
            $"onto body front={front} (root forward was {rootFwd}); world appearance unchanged");
        return true;
    }

    /// <summary>View/head reference: in front of the head bone, looking along the (horizontal) facing.</summary>
    public static RefPose ComputeView(Slot avatarRoot, HumanoidRig rig)
    {
        var head = rig?.TryGetBone(BodyNode.Head);
        if (avatarRoot == null || head == null || head.IsDestroyed)
            return default;

        TryComputeBodyAxes(rig!, out _, out _, out var forward);
        float3 flat = Flatten(forward);
        float3 worldPos = head.GlobalPosition + flat * ViewForwardOffset;
        return ToLocal(avatarRoot, worldPos, BuildRotation(flat, float3.Up));
    }

    /// <summary>
    /// Hand grip reference: at the hand bone, oriented along the forearm with "up" along the arm-bend
    /// plane normal - a usable controller grip frame regardless of the bone's authored rotation.
    /// </summary>
    public static RefPose ComputeHandGrip(Slot avatarRoot, HumanoidRig rig, bool rightSide)
    {
        var hand = rig?.TryGetBone(rightSide ? BodyNode.RightHand : BodyNode.LeftHand);
        if (avatarRoot == null || hand == null || hand.IsDestroyed)
            return default;

        var lower = rig!.TryGetBone(rightSide ? BodyNode.RightLowerArm : BodyNode.LeftLowerArm);
        var upper = rig.TryGetBone(rightSide ? BodyNode.RightUpperArm : BodyNode.LeftUpperArm);

        float3 handPos = hand.GlobalPosition;
        float3 pointDir;
        if (lower != null && !lower.IsDestroyed)
        {
            var d = handPos - lower.GlobalPosition;
            pointDir = d.LengthSquared > 1e-6f ? d.Normalized : hand.GlobalRotation * float3.Backward;
        }
        else
        {
            pointDir = hand.GlobalRotation * float3.Backward;
        }

        // Palm normal (the grip's "up" roll). The arm-bend plane normal Cross(upper->lower, lower->hand) has the
        // OPPOSITE sign for a left vs right elbow, so reusing one cross order for both hands rolls one grip 180 deg
        // (one hand's align arrow points the wrong way vs the other). Derive it from the THUMB instead: the thumb
        // sits on mirror-opposite sides of the two hands, and that mirroring exactly cancels the forearm mirror, so
        // Cross(forearm, thumb) yields a consistent back-of-hand normal on BOTH sides. Falls back to the arm-bend
        // plane, sign-corrected by label, when the rig has no thumb bone. -xlinka
        float3 palmNormal = float3.Up;
        var thumb = FirstBone(rig, rightSide
            ? new[] { BodyNode.RightThumb_Proximal, BodyNode.RightThumb_Metacarpal, BodyNode.RightThumb_Distal }
            : new[] { BodyNode.LeftThumb_Proximal, BodyNode.LeftThumb_Metacarpal, BodyNode.LeftThumb_Distal });
        if (thumb != null && !thumb.IsDestroyed)
        {
            var n = float3.Cross(pointDir, thumb.GlobalPosition - handPos);
            if (n.LengthSquared > 1e-6f)
                palmNormal = n.Normalized;
        }
        else if (upper != null && lower != null && !upper.IsDestroyed && !lower.IsDestroyed)
        {
            var a = lower.GlobalPosition - upper.GlobalPosition;
            var b = handPos - lower.GlobalPosition;
            var n = float3.Cross(a, b);
            if (rightSide)
                n = -n; // share the labeled-left roll so both grips are consistent on a labeled rig
            if (n.LengthSquared > 1e-6f)
                palmNormal = n.Normalized;
        }

        return ToLocal(avatarRoot, handPos, BuildRotation(pointDir, palmNormal));
    }

    /// <summary>Foot reference: at the foot bone, flattened to face the body's forward on the ground.</summary>
    public static RefPose ComputeFoot(Slot avatarRoot, HumanoidRig rig, bool rightSide)
    {
        var foot = rig?.TryGetBone(rightSide ? BodyNode.RightFoot : BodyNode.LeftFoot);
        if (avatarRoot == null || foot == null || foot.IsDestroyed)
            return default;

        TryComputeBodyAxes(rig!, out _, out _, out var forward);
        return ToLocal(avatarRoot, foot.GlobalPosition, BuildRotation(Flatten(forward), float3.Up));
    }

    /// <summary>Pelvis reference: at the hips bone, flattened to the body's forward.</summary>
    public static RefPose ComputePelvis(Slot avatarRoot, HumanoidRig rig)
    {
        var hips = rig?.TryGetBone(BodyNode.Hips);
        if (avatarRoot == null || hips == null || hips.IsDestroyed)
            return default;

        TryComputeBodyAxes(rig!, out _, out _, out var forward);
        return ToLocal(avatarRoot, hips.GlobalPosition, BuildRotation(Flatten(forward), float3.Up));
    }

    /// <summary>
    /// Build (or rebuild) the "AvatarReferences" subtree with auto-aligned <see cref="AvatarReferencePoint"/>s.
    /// Returns the reference root, or null if the rig is unusable.
    /// </summary>
    public static Slot AutoPlaceReferences(Slot avatarRoot, HumanoidRig rig, bool feet, bool pelvis)
    {
        if (avatarRoot == null || rig == null)
            return null!;

        var existing = avatarRoot.FindChild("AvatarReferences", recursive: false);
        if (existing != null && !existing.IsDestroyed)
            existing.Destroy();

        var root = avatarRoot.AddSlot("AvatarReferences");
        root.LocalPosition.Value = float3.Zero;
        root.LocalRotation.Value = floatQ.Identity;

        Place(root, AvatarReferenceKind.View, "View", ComputeView(avatarRoot, rig));
        Place(root, AvatarReferenceKind.LeftHandGrip, "LeftHandGrip", ComputeHandGrip(avatarRoot, rig, rightSide: false));
        Place(root, AvatarReferenceKind.RightHandGrip, "RightHandGrip", ComputeHandGrip(avatarRoot, rig, rightSide: true));
        if (feet)
        {
            Place(root, AvatarReferenceKind.LeftFoot, "LeftFoot", ComputeFoot(avatarRoot, rig, rightSide: false));
            Place(root, AvatarReferenceKind.RightFoot, "RightFoot", ComputeFoot(avatarRoot, rig, rightSide: true));
        }
        if (pelvis)
            Place(root, AvatarReferenceKind.Pelvis, "Pelvis", ComputePelvis(avatarRoot, rig));

        return root;
    }

    private static void Place(Slot referenceRoot, AvatarReferenceKind kind, string name, in RefPose pose)
    {
        if (!pose.Valid)
            return;
        // referenceRoot has an identity local transform under the avatar, so avatar-local == here.
        var slot = referenceRoot.AddSlot(name);
        slot.LocalPosition.Value = pose.LocalPosition;
        slot.LocalRotation.Value = pose.LocalRotation;
        slot.AttachComponent<AvatarReferencePoint>().Kind.Value = kind;
    }

    private static RefPose ToLocal(Slot avatarRoot, in float3 worldPos, in floatQ worldRot)
        => new RefPose
        {
            Valid = true,
            LocalPosition = avatarRoot.GlobalPointToLocal(worldPos),
            LocalRotation = avatarRoot.GlobalRotation.Inverse * worldRot,
        };

    // First existing (non-destroyed) bone among the candidates, or null.
    private static Slot? FirstBone(HumanoidRig rig, BodyNode[] nodes)
    {
        foreach (var node in nodes)
        {
            var bone = rig.TryGetBone(node);
            if (bone != null && !bone.IsDestroyed)
                return bone;
        }
        return null;
    }

    private static float3 Flatten(float3 v)
    {
        v.y = 0f;
        return v.LengthSquared > 1e-6f ? v.Normalized : float3.Backward;
    }

    // Rotation whose facing axis (Backward, matching the IK/slot convention) points along forward and
    // whose up rolls toward up. Built from two swings to avoid floatQ.LookRotation (returns inverse).
    private static floatQ BuildRotation(float3 forward, float3 up)
    {
        if (forward.LengthSquared < 1e-8f)
            return floatQ.Identity;
        forward = forward.Normalized;

        floatQ q1 = FabrikSolver.FromToRotation(float3.Backward, forward);
        float3 curUp = q1 * float3.Up;
        float3 upProj = up - forward * float3.Dot(up, forward);
        if (upProj.LengthSquared < 1e-6f)
            return q1;
        floatQ q2 = FabrikSolver.FromToRotation(curUp, upProj.Normalized);
        return q2 * q1;
    }
}
