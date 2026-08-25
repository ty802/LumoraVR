// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// A physical touching tip riding on a hand. Rides an avatar's fingertip bone when there is one and
// falls back to a fixed offset off its own slot when there is not, so touch works with a full
// tracked hand, with a stubby default hand, and with a bare controller.
//
// The probe casts from BEHIND its own tip so the same ray answers both questions at once: how far
// short of the surface the tip still is (hover), and how far past it the tip has gone (contact).
// Sampling the tip position alone can only ever tell you which side of the surface you ended up on,
// which is how you get buttons that only fire if you happen to stop your hand inside them. -xlinka
//
// Runs after the hand tool (order 0), so a beam probe reads the pose and press state the tool pushed
// into the laser THIS frame instead of last frame's. -xlinka
[DefaultUpdateOrder(100)]
[ComponentCategory("Interaction/Touch")]
public class FingertipProbe : TouchProbe
{
    // Only used to find the matching rig bone.
    public readonly Sync<FingerType> Finger;

    // Follow the equipped avatar's fingertip bone when one exists. Off pins the tip to TipOffset off
    // this slot, which is what a bare controller wants.
    public readonly Sync<bool> TrackRigFingertip;

    // Overrides the rig lookup when set.
    public readonly SyncRef<Slot> TipSlot;

    // Tip position in the tip slot's local space.
    public readonly Sync<float3> TipOffset;

    // Direction the tip pushes along, in the tip slot's local space.
    public readonly Sync<float3> TipAxis;

    private Slot? _boneCache;
    private Slot? _behindCache;
    private double _nextBoneSearch;
    private readonly List<ControllerHandVisual> _visualBuffer = new(2);

    // A rig search walks the whole avatar hierarchy. Cheap once, wasteful every frame while an avatar
    // has no rig at all, which is the common case for the default hands. -xlinka
    private const double BoneSearchInterval = 0.5;

    public FingertipProbe()
    {
        Finger = new Sync<FingerType>(this, FingerType.Index);
        TrackRigFingertip = new Sync<bool>(this, true);
        TipSlot = new SyncRef<Slot>(this);
        TipOffset = new Sync<float3>(this, float3.Zero);
        TipAxis = new Sync<float3>(this, float3.Forward);
    }

    public override TouchProbeKind Kind => TouchProbeKind.Fingertip;

    public override float3 TipPosition
    {
        get
        {
            var anchor = ResolveAnchor();
            return anchor != null ? anchor.LocalPointToGlobal(TipOffset.Value) : float3.Zero;
        }
    }

    public override float3 TipDirection
    {
        get
        {
            var anchor = ResolveAnchor();
            if (anchor == null)
                return float3.Forward;

            // A fingertip bone has no meaningful authored forward axis - importers disagree, and the
            // hand visual's joints are bare spheres with no rotation at all - so take the direction
            // the last SEGMENT actually runs in, from the joint behind the tip to the tip. -xlinka
            var behind = _behindCache != null && !_behindCache.IsDestroyed ? _behindCache : null;
            if (behind != null && !ReferenceEquals(behind, anchor))
            {
                float3 along = anchor.GlobalPosition - behind.GlobalPosition;
                float length = along.Length;
                if (length > 1e-4f)
                    return along / length;
            }

            float3 axis = anchor.LocalDirectionToGlobal(TipAxis.Value);
            float axisLength = axis.Length;
            return axisLength > 1e-4f ? axis / axisLength : float3.Forward;
        }
    }

    public override void OnStart()
    {
        base.OnStart();
        BindOwnerFromHierarchy();
    }

    protected override bool Sample(out TouchSample sample)
    {
        sample = default;

        var world = World;
        if (world == null)
            return false;

        float maxPenetration = MathF.Max(MaxPenetration.Value, 0.001f);
        float hoverMargin = MathF.Max(HoverMargin.Value, 0f);

        float3 tip = TipPosition;
        float3 direction = TipDirection;

        // Start the ray one full penetration allowance behind the tip and run it one hover margin
        // past. The hit distance then reads directly as signed depth: anything closer than the
        // back-off is surface the tip has already passed. -xlinka
        float3 origin = tip - direction * maxPenetration;
        float length = maxPenetration + hoverMargin;

        if (!world.Physics.Raycast(in origin, in direction, length, OwnBodyExclusions(), out var hit, hitTriggers: true))
            return false;

        var target = ResolveTarget(hit.Slot);
        if (target == null)
            return false;

        sample = new TouchSample(target, in hit.Point, in hit.Normal, maxPenetration - hit.Distance);
        return true;
    }

    private Slot? ResolveAnchor()
    {
        var explicitSlot = TipSlot.Target;
        if (explicitSlot != null && !explicitSlot.IsDestroyed)
            return explicitSlot;

        if (TrackRigFingertip.Value)
        {
            var bone = ResolveRigBone();
            if (bone != null)
                return bone;
        }

        return Slot;
    }

    // Two sources, in order of how much they know about the hand: a real avatar's rigged fingertip
    // bone, then the drawn hand skeleton the controller visual maintains. The second one matters more
    // than it looks - it is what everyone is using before they equip an avatar, and it is already
    // tracking real finger poses on headsets that report them. -xlinka
    private Slot? ResolveRigBone()
    {
        if (_boneCache != null && !_boneCache.IsDestroyed)
            return _boneCache;

        _boneCache = null;
        _behindCache = null;

        var world = World;
        double now = world?.Time.TotalTime ?? 0.0;
        if (now < _nextBoneSearch)
            return null;
        _nextBoneSearch = now + BoneSearchInterval;

        var side = ResolveSide();
        var tipNode = Finger.Value.ComposeFinger(FingerSegmentType.Tip, side);
        var behindNode = Finger.Value.ComposeFinger(FingerSegmentType.Distal, side);
        var rootSlot = Owner.Target?.Root?.Slot ?? Slot?.ActiveUserRoot?.Slot;

        var rig = rootSlot?.GetComponentInChildren<HumanoidRig>();
        if (rig != null && !rig.IsDestroyed)
        {
            var bone = rig.TryGetBone(tipNode);
            if (bone != null && !bone.IsDestroyed)
            {
                _boneCache = bone;
                _behindCache = rig.TryGetBone(behindNode) ?? bone.Parent;
                return bone;
            }
        }

        var visual = FindHandVisual(rootSlot);
        if (visual != null)
        {
            var joint = visual.TryGetJointSlot(tipNode);
            if (joint != null)
            {
                _boneCache = joint;
                _behindCache = visual.TryGetJointSlot(behindNode);
                return joint;
            }
        }

        return null;
    }

    // The hand visual is an EQUIPPED piece hanging off the hand socket, so it is a sibling subtree of
    // this probe, never an ancestor. Look under the probe's own controller node first (which can only
    // hold this hand's visual), then fall back to a side-filtered sweep of the user root. -xlinka
    private ControllerHandVisual? FindHandVisual(Slot? rootSlot)
    {
        var side = ResolveSide();

        var near = Slot?.Parent?.GetComponentInChildren<ControllerHandVisual>();
        if (near != null && !near.IsDestroyed && near.HandSide.Value == side)
            return near;

        if (rootSlot == null || rootSlot.IsDestroyed)
            return null;

        _visualBuffer.Clear();
        rootSlot.GetComponentsInChildren(_visualBuffer);
        for (int i = 0; i < _visualBuffer.Count; i++)
        {
            var candidate = _visualBuffer[i];
            if (candidate != null && !candidate.IsDestroyed && candidate.HandSide.Value == side)
                return candidate;
        }
        return null;
    }

    private Chirality ResolveSide()
        => Hand.Value != Chirality.None ? Hand.Value : Chirality.Right;
}
