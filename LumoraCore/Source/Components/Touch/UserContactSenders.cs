// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// A user's body as contact senders: head, torso, both hands, four fingers a hand and both feet, carrying the body's
// standard tags, so any receiver in the room (on someone's avatar, on the user's own, in the world) feels them.
// Auto-added to the avatar scaffold.
//
// Shapes come from the worn avatar's skeleton where it has the bones, else from the tracked head and hands. An avatar
// that carries its own body senders (marked Body, sized by its author) replaces all of this. Every peer builds these
// from poses it already has, like the bone chains' player colliders: nothing here is sent. -xlinka
[ComponentCategory("Users/Avatar")]
public class UserContactSenders : UserRootComponent
{
    // Radii in metres at 1:1 user scale; the user's scale multiplies them.
    public readonly Sync<float> HeadRadius;
    public readonly Sync<float> TorsoRadius;
    public readonly Sync<float> HandRadius;
    public readonly Sync<float> FingerRadius;
    public readonly Sync<float> FootRadius;

    private const int PartCount = 14;
    private static readonly int[][] PartTags = new int[PartCount][];
    private static readonly ulong[] PartMasks = new ulong[PartCount];

    private static readonly FingerType[] FingerOrder = { FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Pinky };

    private readonly float3[] _lastCentre = new float3[PartCount];
    private readonly bool[] _hasLast = new bool[PartCount];
    private ulong _lastFrame;

    private ContactManager? _manager;
    private UserRoot? _cachedRoot;
    private HumanoidRig? _rig;
    private bool _avatarBringsBody;
    private double _nextResolve = double.NegativeInfinity;

    static UserContactSenders()
    {
        Set(0, ContactTags.Head);
        Set(1, ContactTags.Torso);
        Set(2, ContactTags.HandLeft);
        Set(3, ContactTags.HandRight);
        Set(4, ContactTags.FootLeft);
        Set(5, ContactTags.FootRight);
        for (int i = 0; i < 4; i++)
        {
            Set(6 + i, ContactTags.FingersLeft[i]);
            Set(10 + i, ContactTags.FingersRight[i]);
        }

        static void Set(int part, string[] names)
        {
            PartTags[part] = ContactTags.Ids(names);
            PartMasks[part] = ContactTags.Mask(PartTags[part]);
        }
    }

    public UserContactSenders()
    {
        HeadRadius = new Sync<float>(this, 0.11f);
        TorsoRadius = new Sync<float>(this, 0.13f);
        HandRadius = new Sync<float>(this, 0.04f);
        FingerRadius = new Sync<float>(this, 0.012f);
        FootRadius = new Sync<float>(this, 0.06f);
    }

    public override void OnStart()
    {
        base.OnStart();
        _manager = ContactManager.For(World);
        _manager?.Register(this);
    }

    public override void OnDestroy()
    {
        _manager?.Unregister(this);
        _manager = null;
        base.OnDestroy();
    }

    internal void Contribute(ContactManager manager, ulong frame, float delta)
    {
        if (!Enabled.Value || IsDestroyed)
            return;
        var root = Slot?.ActiveUserRoot;
        if (root == null || root.IsDestroyed || root.Slot == null || !root.Slot.IsActive)
            return;
        Resolve(root);
        if (_avatarBringsBody)
            return;

        var owner = root.ActiveUser;
        if (owner == null)
            return;
        float scale = root.GlobalScale;
        if (scale <= 1e-4f)
            scale = 1f;
        bool fresh = frame != _lastFrame + 1;
        _lastFrame = frame;
        var rig = _rig != null && !_rig.IsDestroyed ? _rig : null;

        var head = root.HeadSlot;
        if (head != null && !head.IsDestroyed)
        {
            var centre = head.LocalPointToGlobal(float3.Backward * 0.08f);
            Add(manager, 0, centre, centre, HeadRadius.Value * scale, owner, delta, fresh);
        }

        var hips = Bone(rig, BodyNode.Hips);
        var neck = Bone(rig, BodyNode.Neck) ?? Bone(rig, BodyNode.UpperChest) ?? Bone(rig, BodyNode.Chest);
        if (hips != null && neck != null)
        {
            Add(manager, 1, hips.GlobalPosition, neck.GlobalPosition, TorsoRadius.Value * scale, owner, delta, fresh);
        }
        else if (head != null && !head.IsDestroyed)
        {
            var space = root.Slot;
            var top = space.GlobalPointToLocal(head.GlobalPosition);
            var a = space.LocalPointToGlobal(new float3(top.x, top.y - 0.25f, top.z));
            var b = space.LocalPointToGlobal(new float3(top.x, MathF.Max(top.y - 0.75f, 0.1f), top.z));
            Add(manager, 1, a, b, TorsoRadius.Value * scale, owner, delta, fresh);
        }

        Hand(manager, rig, root, Chirality.Left, 2, 6, scale, owner, delta, fresh);
        Hand(manager, rig, root, Chirality.Right, 3, 10, scale, owner, delta, fresh);
        Foot(manager, rig, BodyNode.LeftFoot, BodyNode.LeftToes, 4, scale, owner, delta, fresh);
        Foot(manager, rig, BodyNode.RightFoot, BodyNode.RightToes, 5, scale, owner, delta, fresh);
    }

    private void Hand(ContactManager manager, HumanoidRig? rig, UserRoot root, Chirality side, int handPart, int firstFinger,
        float scale, User owner, float delta, bool fresh)
    {
        var hand = Bone(rig, side == Chirality.Left ? BodyNode.LeftHand : BodyNode.RightHand);
        var knuckle = Bone(rig, FingerType.Middle.ComposeFinger(FingerSegmentType.Proximal, side));
        if (hand != null && knuckle != null)
        {
            Add(manager, handPart, hand.GlobalPosition, knuckle.GlobalPosition, HandRadius.Value * scale, owner, delta, fresh);
            for (int i = 0; i < FingerOrder.Length; i++)
            {
                var finger = FingerOrder[i];
                var start = Bone(rig, finger.ComposeFinger(FingerSegmentType.Proximal, side));
                var last = Bone(rig, finger.ComposeFinger(FingerSegmentType.Distal, side));
                if (start == null || last == null)
                    continue;
                var tip = Bone(rig, finger.ComposeFinger(FingerSegmentType.Tip, side));
                float3 end;
                if (tip != null)
                {
                    end = tip.GlobalPosition;
                }
                else
                {
                    // No tip bone: the last segment again past the last joint.
                    var middle = Bone(rig, finger.ComposeFinger(FingerSegmentType.Intermediate, side)) ?? start;
                    end = last.GlobalPosition + (last.GlobalPosition - middle.GlobalPosition) * 0.8f;
                }
                Add(manager, firstFinger + i, start.GlobalPosition, end, FingerRadius.Value * scale, owner, delta, fresh);
            }
            return;
        }

        // Tracked hand alone: the palm forward of the wrist and a pointing finger past it.
        var tracked = side == Chirality.Left ? root.LeftHandSlot : root.RightHandSlot;
        if (tracked == null || tracked.IsDestroyed)
            return;
        Add(manager, handPart, tracked.GlobalPosition, tracked.LocalPointToGlobal(new float3(0f, 0f, 0.08f)), HandRadius.Value * scale, owner, delta, fresh);
        Add(manager, firstFinger, tracked.LocalPointToGlobal(new float3(0f, 0f, 0.09f)), tracked.LocalPointToGlobal(new float3(0f, 0f, 0.17f)),
            FingerRadius.Value * scale, owner, delta, fresh);
    }

    private void Foot(ContactManager manager, HumanoidRig? rig, BodyNode footNode, BodyNode toesNode, int part, float scale, User owner,
        float delta, bool fresh)
    {
        var foot = Bone(rig, footNode);
        if (foot == null)
            return;
        var toes = Bone(rig, toesNode);
        var centre = toes != null ? (foot.GlobalPosition + toes.GlobalPosition) * 0.5f : foot.GlobalPosition;
        Add(manager, part, centre, centre, FootRadius.Value * scale, owner, delta, fresh);
    }

    private void Add(ContactManager manager, int part, in float3 a, in float3 b, float radius, User owner, float delta, bool fresh)
    {
        var centre = (a + b) * 0.5f;
        var velocity = !fresh && _hasLast[part] && delta > 1e-5f ? (centre - _lastCentre[part]) / delta : float3.Zero;
        _lastCentre[part] = centre;
        _hasLast[part] = true;
        manager.AddShape(ContactShape.Make(a, b, radius, PartTags[part], PartMasks[part], owner, true, velocity));
    }

    private static Slot? Bone(HumanoidRig? rig, BodyNode node)
    {
        if (rig == null)
            return null;
        var bone = rig.TryGetBone(node);
        return bone != null && !bone.IsDestroyed ? bone : null;
    }

    // The worn avatar's skeleton, and whether the avatar brings its own body senders. Looked up again once a second,
    // and at once when the user root changes or the rig goes away.
    private void Resolve(UserRoot root)
    {
        double now = World?.Time.TotalTime ?? 0d;
        bool stale = !ReferenceEquals(root, _cachedRoot) || (_rig != null && _rig.IsDestroyed);
        if (!stale && now < _nextResolve)
            return;
        _nextResolve = now + 1.0;
        _cachedRoot = root;
        var worn = root.Slot.GetComponentInChildren<AvatarEquipManager>()?.Slot;
        _rig = worn?.GetComponentInChildren<HumanoidRig>() ?? root.Slot.GetComponentInChildren<HumanoidRig>();
        _avatarBringsBody = false;
        if (_rig != null)
        {
            foreach (var sender in _rig.Slot.GetComponentsInChildren<ContactSender>())
            {
                if (sender.Body.Value && sender.Enabled.Value)
                {
                    _avatarBringsBody = true;
                    break;
                }
            }
        }
    }
}
