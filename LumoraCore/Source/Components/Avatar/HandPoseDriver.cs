// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Drives an avatar hand's finger bones from an <see cref="IHandPoseSource"/>.
/// Attached to a hand under a rigged avatar; finds its finger bones through the
/// <see cref="HumanoidRig"/> and aims each segment toward the next.
/// </summary>
// Retarget approach: a convention-robust direction-aim swing rather than a
// LookRotation-based coordinate compensation (floatQ.LookRotation returns the
// inverse here and Godot is right-handed, so that path would invert). Each
// finger segment is aimed at the next using a shortest-arc swing: we capture, in
// the wrist's local frame, each bone's rest rotation and its rest
// direction-to-next, then at runtime swing that rest direction onto the
// source-provided direction. The cost is no finger twist (fingers barely twist,
// so this reads fine).
//
// Bone drives are LocalValueOnly: every peer computes finger pose itself, so no
// finger data goes on the wire. Remote/desktop hands rest because their source
// reports not-tracking, never because of a fake pose. - xlinka
[ComponentCategory("Users/Avatar/Hands")]
public class HandPoseDriver : UserRootComponent
{
    /// <summary>Which hand this poser drives.</summary>
    public readonly Sync<Chirality> Side = new();

    /// <summary>Explicit finger source. When null, falls back to the user-root <see cref="UserHandPoseInfo"/>.</summary>
    public readonly SyncRef<IHandPoseSourceComponent> PoseSource = null!;

    /// <summary>
    /// Idle fallback source, used only when no live/explicit source is tracking this hand - e.g. an idle
    /// <see cref="HandPosePreset"/> so a desktop hand holds a real relaxed shape instead of the authored
    /// bind pose. Live VR finger tracking (via <see cref="PoseSource"/> / the user-root source) still wins
    /// whenever it's actually tracking.
    /// </summary>
    public readonly SyncRef<IHandPoseSourceComponent> IdlePose = null!;

    /// <summary>Wrist bone used as the hand-root frame. Resolved from the rig when unset.</summary>
    public readonly SyncRef<Slot> HandRoot = null!;

    // The visibly-curling joints, proximal outward. Thumb has no intermediate.
    // Tip is included only as an aim target for the distal bone.
    private static readonly FingerType[] AllFingers =
    {
        FingerType.Thumb, FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Pinky,
    };

    private static readonly FingerSegmentType[] ThumbSegments =
    {
        FingerSegmentType.Proximal, FingerSegmentType.Distal, FingerSegmentType.Tip,
    };

    private static readonly FingerSegmentType[] FingerSegments =
    {
        FingerSegmentType.Proximal, FingerSegmentType.Intermediate, FingerSegmentType.Distal, FingerSegmentType.Tip,
    };

    // One driven segment: its bone, the next node it aims at, and rest calibration.
    private sealed class SegmentDrive
    {
        public Slot Bone = null!;
        public BodyNode Node;
        public BodyNode NextNode;
        public FieldDrive<floatQ> Drive = null!;
        public floatQ RestLocalRotation;   // bone.LocalRotation captured at rest
        public floatQ RestRotWrist;        // bone rotation in wrist space at rest
        public float3 RestDirWrist;        // rest direction (this -> next) in wrist space
    }

    private readonly List<List<SegmentDrive>> _fingers = new();
    private Slot _wrist = null!;
    private bool _assigned;
    private bool _handReset;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_assigned)
        {
            TryAssign();
            if (!_assigned)
                return;
        }

        var source = ResolveSource(Side.Value);
        if (source == null || !source.IsHandTracked(Side.Value))
        {
            // Untracked (no live source and no fallback): park the hand in its authored pose once.
            if (!_handReset)
            {
                ResetToRest();
                _handReset = true;
            }
            return;
        }

        _handReset = false;
        DrivePose(source);
    }

    public override void OnDestroy()
    {
        ReleaseDrives();
        base.OnDestroy();
    }

    // Pick the source for one hand. Order: the explicit PoseSource (the equip assigner copies the user-root
    // VR stream here), else the user-root source directly - either, when actually tracking this side, beats
    // the idle fallback so live VR finger tracking always wins. When nothing is tracking, the IdlePose
    // fallback (e.g. an idle preset) gives the hand a real relaxed shape instead of the authored bind pose.
    private IHandPoseSource ResolveSource(Chirality side)
    {
        var primary = PoseSource?.Target
                      ?? Slot?.ActiveUserRoot?.GetRegisteredComponent<UserHandPoseInfo>()?.HandPoseSource?.Target;
        if (primary != null && primary.IsHandTracked(side))
            return primary;

        var idle = IdlePose?.Target;
        if (idle != null)
            return idle;

        // No idle fallback: keep the prior behavior of returning the primary source directly (it may be
        // present but momentarily not tracking, or absent - the caller then rests the hand).
        return primary!;
    }

    // Bind to the rig's finger bones and capture the rest calibration. Retried
    // each frame until the avatar's rig is built and its hand has fingers.
    private void TryAssign()
    {
        var rig = Slot?.GetComponentInParent<HumanoidRig>() ?? Slot?.GetComponentInChildren<HumanoidRig>();
        if (rig == null)
            return;

        var side = Side.Value;
        bool hasFingers = side == Chirality.Left ? rig.HasLeftFingerBones : rig.HasRightFingerBones;
        if (!hasFingers)
            return;

        var wrist = HandRoot?.Target
                    ?? rig.TryGetBone(side == Chirality.Left ? BodyNode.LeftHand : BodyNode.RightHand);
        if (wrist == null)
            return;

        ReleaseDrives();
        _wrist = wrist;
        _fingers.Clear();

        foreach (var finger in AllFingers)
        {
            var chain = BuildChain(rig, finger, side, wrist);
            if (chain.Count > 0)
                _fingers.Add(chain);
        }

        _assigned = _fingers.Count > 0;
    }

    private List<SegmentDrive> BuildChain(HumanoidRig rig, FingerType finger, Chirality side, Slot wrist)
    {
        var segTypes = finger == FingerType.Thumb ? ThumbSegments : FingerSegments;

        // Collect the segments that actually have bones, with rest data.
        var bones = new List<(Slot bone, BodyNode node, float3 posWrist, floatQ rotWrist, floatQ local)>();
        foreach (var seg in segTypes)
        {
            var node = finger.ComposeFinger(seg, side);
            var bone = rig.TryGetBone(node);
            if (bone == null)
                continue;

            bones.Add((
                bone,
                node,
                wrist.GlobalPointToLocal(bone.GlobalPosition),
                wrist.GlobalRotationToLocal(bone.GlobalRotation),
                bone.LocalRotation.Value));
        }

        // Drive every bone that has a following node to aim at.
        var chain = new List<SegmentDrive>();
        for (int i = 0; i < bones.Count - 1; i++)
        {
            var a = bones[i];
            var b = bones[i + 1];

            var dir = b.posWrist - a.posWrist;
            if (dir.LengthSquared < 1e-10f)
                continue;

            var drive = new FieldDrive<floatQ>(World) { LocalValueOnly = true };
            drive.DriveTarget(a.bone.LocalRotation);

            chain.Add(new SegmentDrive
            {
                Bone = a.bone,
                Node = a.node,
                NextNode = b.node,
                Drive = drive,
                RestLocalRotation = a.local,
                RestRotWrist = a.rotWrist,
                RestDirWrist = dir.Normalized,
            });
        }

        return chain;
    }

    private void DrivePose(IHandPoseSource source)
    {
        if (_wrist == null || _wrist.IsDestroyed)
            return;

        bool tracksMetacarpals = source.TracksMetacarpals;

        foreach (var chain in _fingers)
        {
            foreach (var seg in chain)
            {
                if (!seg.Drive.IsLinkValid)
                    continue;

                // When the source has no metacarpal data, leave any segment whose
                // aim involves a metacarpal node at its authored rest - driving it
                // from absent/stale data would splay the palm. (The default segment
                // chains start at Proximal, so this normally no-ops; it guards
                // sources that do publish metacarpals against this one not.)
                if (!tracksMetacarpals &&
                    (IsMetacarpal(seg.Node) || IsMetacarpal(seg.NextNode)))
                {
                    seg.Drive.SetValue(seg.RestLocalRotation);
                    continue;
                }

                if (!source.TryGetFingerPosition(seg.Node, out var pA) ||
                    !source.TryGetFingerPosition(seg.NextNode, out var pB))
                {
                    seg.Drive.SetValue(seg.RestLocalRotation);
                    continue;
                }

                var desired = pB - pA;
                if (desired.LengthSquared < 1e-10f)
                {
                    seg.Drive.SetValue(seg.RestLocalRotation);
                    continue;
                }

                // Swing the bone's rest direction onto the source direction, both
                // in wrist space, then carry the result back through the (current)
                // wrist world rotation into the bone's parent-local space.
                var newRotWrist = FromTo(seg.RestDirWrist, desired.Normalized) * seg.RestRotWrist;
                var worldRot = _wrist.LocalRotationToGlobal(newRotWrist);
                var parent = seg.Bone.Parent;
                var local = parent != null ? parent.GlobalRotationToLocal(worldRot) : worldRot;
                seg.Drive.SetValue(local);
            }
        }
    }

    private void ResetToRest()
    {
        foreach (var chain in _fingers)
            foreach (var seg in chain)
                if (seg.Drive.IsLinkValid)
                    seg.Drive.SetValue(seg.RestLocalRotation);
    }

    private void ReleaseDrives()
    {
        foreach (var chain in _fingers)
            foreach (var seg in chain)
                seg.Drive?.ReleaseLink();
        _fingers.Clear();
        _assigned = false;
        _handReset = false;
    }

    private static bool IsMetacarpal(BodyNode node)
        => node.IsFinger() && node.GetFingerSegmentType() == FingerSegmentType.Metacarpal;

    // Shortest-arc rotation taking unit vector `from` onto unit vector `to`.
    private static floatQ FromTo(float3 from, float3 to)
    {
        float d = float3.Dot(from, to);
        if (d >= 0.99999f)
            return floatQ.Identity;
        if (d <= -0.99999f)
        {
            var axis = float3.Cross(float3.Up, from);
            if (axis.LengthSquared < 1e-6f)
                axis = float3.Cross(float3.Right, from);
            return floatQ.AxisAngleRad(axis.Normalized, MathF.PI);
        }
        var c = float3.Cross(from, to).Normalized;
        float angle = MathF.Acos(System.Math.Clamp(d, -1f, 1f));
        return floatQ.AxisAngleRad(c, angle);
    }
}
