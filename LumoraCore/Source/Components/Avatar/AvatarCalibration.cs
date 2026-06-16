// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Avatar.IK;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Computes avatar calibration reference poses (view, hand grips, feet, pelvis) directly from a
/// <see cref="BipedRig"/>, so the avatar can be set up automatically with no manual placement.
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
    public static bool TryComputeBodyAxes(BipedRig rig, out float3 up, out float3 right, out float3 forward)
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

        float3 fwd;
        var leftUpper = rig.TryGetBone(BodyNode.LeftUpperArm);
        var rightUpper = rig.TryGetBone(BodyNode.RightUpperArm);
        if (leftUpper != null && rightUpper != null && !leftUpper.IsDestroyed && !rightUpper.IsDestroyed)
        {
            // Forward straight from body geometry: Cross(spine-up, shoulder-line). This depends only on
            // bone POSITIONS, so it's consistent across rigs - NOT on the head bone's authored rotation,
            // which varies per model and would flip the avatar 180 degrees.
            var r = rightUpper.GlobalPosition - leftUpper.GlobalPosition;   // toward the avatar's right
            right = r.LengthSquared > 1e-6f ? r.Normalized : float3.Right;
            fwd = float3.Cross(up, right);                                  // points out the front
        }
        else
        {
            fwd = head.GlobalRotation * float3.Backward;
        }
        if (fwd.LengthSquared < 1e-6f)
            fwd = float3.Backward;
        fwd = fwd.Normalized;

        forward = fwd;
        right = float3.Cross(forward, up);
        right = right.LengthSquared > 1e-6f ? right.Normalized : float3.Right;
        return true;
    }

    /// <summary>View/head reference: in front of the head bone, looking along the (horizontal) facing.</summary>
    public static RefPose ComputeView(Slot avatarRoot, BipedRig rig)
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
    public static RefPose ComputeHandGrip(Slot avatarRoot, BipedRig rig, bool rightSide)
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

        float3 palmNormal = float3.Up;
        if (upper != null && lower != null && !upper.IsDestroyed && !lower.IsDestroyed)
        {
            var a = lower.GlobalPosition - upper.GlobalPosition;
            var b = handPos - lower.GlobalPosition;
            var n = float3.Cross(a, b);
            if (n.LengthSquared > 1e-6f)
                palmNormal = n.Normalized;
        }

        return ToLocal(avatarRoot, handPos, BuildRotation(pointDir, palmNormal));
    }

    /// <summary>Foot reference: at the foot bone, flattened to face the body's forward on the ground.</summary>
    public static RefPose ComputeFoot(Slot avatarRoot, BipedRig rig, bool rightSide)
    {
        var foot = rig?.TryGetBone(rightSide ? BodyNode.RightFoot : BodyNode.LeftFoot);
        if (avatarRoot == null || foot == null || foot.IsDestroyed)
            return default;

        TryComputeBodyAxes(rig!, out _, out _, out var forward);
        return ToLocal(avatarRoot, foot.GlobalPosition, BuildRotation(Flatten(forward), float3.Up));
    }

    /// <summary>Pelvis reference: at the hips bone, flattened to the body's forward.</summary>
    public static RefPose ComputePelvis(Slot avatarRoot, BipedRig rig)
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
    public static Slot AutoPlaceReferences(Slot avatarRoot, BipedRig rig, bool feet, bool pelvis)
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
