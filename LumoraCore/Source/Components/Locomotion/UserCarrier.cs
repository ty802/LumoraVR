// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Components;

// Carries whoever stands in this slot's colliders along with a space: a lift, a turning stage, a cart. The
// moment the person at this machine starts touching one, the user root is hung from the space, so every move,
// turn, tilt and scale of it carries them, and every other machine sees them fixed to it, since the root streams
// its pose in the space it hangs from. When they stop touching, the root goes back to what it hung from before.
// Carriers stack: off the cart and back onto the stage, off the stage and back onto the floor.
//
// Each machine moves only its own user, the way a person moves themselves. Touching is the person's upright body,
// feet to head on the root's own floor, against the colliders on this slot: triggers only unless TriggersOnly is off,
// and a solid collider counts from a couple of centimetres away, so standing on it does. Standing on the ground,
// the way up and the person's own body are looked at only as they step in. -xlinka
[ComponentCategory("Users/Locomotion")]
public sealed class UserCarrier : Component
{
    public readonly Sync<bool> TriggersOnly;

    // Back to what the root hung from when they stop touching. Off, they stay carried until something else
    // moves them.
    public readonly Sync<bool> ReturnOnLeave;

    // Only someone standing on something is taken in.
    public readonly Sync<bool> MustBeOnGround;

    // Only a body whose down is near one of these is taken in; an empty list takes anyone.
    public readonly SyncList<CarryAlignment> Alignments;

    // A carrier on a user's own body leaves that user alone.
    public readonly Sync<bool> IgnoreOwnUser;

    // What riders hang from. Empty is this slot.
    public readonly SyncRef<Slot> CarrySpace;

    private const float Skin = 0.02f;
    private const float DefaultBodyRadius = 0.3f;

    private bool _touching;
    private Slot? _watched;

    public UserCarrier()
    {
        TriggersOnly = new Sync<bool>(this, true);
        ReturnOnLeave = new Sync<bool>(this, true);
        MustBeOnGround = new Sync<bool>(this, false);
        Alignments = new SyncList<CarryAlignment>();
        IgnoreOwnUser = new Sync<bool>(this, false);
        CarrySpace = new SyncRef<Slot>(this);
    }

    public Slot? Space => CarrySpace.Target is { IsDestroyed: false } space ? space : Slot is { IsDestroyed: false } own ? own : null;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var root = World?.LocalUser?.Root;
        bool touching = root is { IsDestroyed: false } && root.Slot is { IsDestroyed: false } && Slot is { IsActive: true }
            && Touching(root);
        if (touching != _touching)
        {
            _touching = touching;
            if (touching)
                StepIn(root!);
            else
                StepOut();
        }
    }

    public override void OnDisabled()
    {
        if (_touching)
        {
            _touching = false;
            StepOut();
        }
        base.OnDisabled();
    }

    public override void OnDestroy()
    {
        if (_touching)
        {
            _touching = false;
            StepOut();
        }
        Unwatch();
        base.OnDestroy();
    }

    private void StepIn(UserRoot root)
    {
        if (IgnoreOwnUser.Value && Slot.ActiveUserRoot is { } own && ReferenceEquals(own, root))
            return;
        var rootSlot = root.Slot;
        if (MustBeOnGround.Value && rootSlot.GetComponent<CharacterController>()?.IsGrounded() != true)
            return;
        if (Alignments.Count > 0)
        {
            var down = rootSlot.GlobalRotation * float3.Down;
            bool accepted = false;
            for (int i = 0; i < Alignments.Count && !accepted; i++)
                accepted = Alignments[i] is { } alignment && alignment.Accepts(down);
            if (!accepted)
                return;
        }
        var space = Space;
        if (space == null || ReferenceEquals(space, rootSlot) || space.IsDescendantOf(rootSlot))
            return;
        var locomotion = rootSlot.GetComponent<LocomotionController>();
        if (ReturnOnLeave.Value && locomotion is { IsDestroyed: false })
            locomotion.EnterSpace(space);
        else
            Hang(rootSlot, space);
        if (ReferenceEquals(rootSlot.Parent, space))
            Watch(space);
    }

    private void StepOut()
    {
        if (!ReturnOnLeave.Value)
            return;
        var root = World?.LocalUser?.Root;
        var rootSlot = root?.Slot;
        var space = Space;
        Unwatch();
        if (rootSlot == null || rootSlot.IsDestroyed || space == null)
            return;
        var locomotion = rootSlot.GetComponent<LocomotionController>();
        if (locomotion is { IsDestroyed: false })
            locomotion.LeaveSpace(space);
        else if (ReferenceEquals(rootSlot.Parent, space))
            Hang(rootSlot, World!.RootSlot);
    }

    // A space going away takes its children with it, so whoever hangs from it is let down first.
    private void OnSpaceDestroying(Slot space)
    {
        Unwatch();
        var rootSlot = World?.LocalUser?.Root?.Slot;
        if (rootSlot == null || rootSlot.IsDestroyed || !ReferenceEquals(rootSlot.Parent, space))
            return;
        var locomotion = rootSlot.GetComponent<LocomotionController>();
        if (locomotion is { IsDestroyed: false })
            locomotion.LeaveSpace(space);
        if (ReferenceEquals(rootSlot.Parent, space) && World?.RootSlot is { } top)
            Hang(rootSlot, top);
    }

    private void Watch(Slot space)
    {
        if (ReferenceEquals(_watched, space))
            return;
        Unwatch();
        _watched = space;
        space.OnPrepareDestroy += OnSpaceDestroying;
    }

    private void Unwatch()
    {
        if (_watched != null)
            _watched.OnPrepareDestroy -= OnSpaceDestroying;
        _watched = null;
    }

    // The person's own root, moved by the person's own machine: engine movement of the body, like a step.
    private void Hang(Slot rootSlot, Slot parent)
    {
        using (World?.DataModelPermissions?.EnterSystemBypass())
            rootSlot.SetParent(parent, preserveGlobalTransform: true);
    }

    // The body is upright on the root's floor under the head, from the floor to the head, as wide as the
    // character collider on the root.
    private bool Touching(UserRoot root)
    {
        var rootSlot = root.Slot;
        var components = Slot.Components;
        bool any = false;
        for (int i = 0; i < components.Count && !any; i++)
            any = Counts(components[i] as Collider);
        if (!any)
            return false;

        var scale = rootSlot.GlobalScale;
        float size = MathF.Max(MathF.Abs(scale.x), MathF.Abs(scale.z));
        float radius = DefaultBodyRadius * size;
        var rootComponents = rootSlot.Components;
        for (int i = 0; i < rootComponents.Count; i++)
        {
            if (rootComponents[i] is CapsuleCollider { IsDestroyed: false } capsule && capsule.Type.Value == ColliderType.CharacterController
                && capsule.Radius.Value > 0f)
            {
                radius = capsule.Radius.Value * size;
                break;
            }
        }
        var head = rootSlot.GlobalPointToLocal(root.HeadPosition);
        var up = rootSlot.GlobalRotation * float3.Up;
        var bottom = rootSlot.LocalPointToGlobal(new float3(head.x, 0f, head.z)) + up * radius;
        var top = rootSlot.LocalPointToGlobal(head);
        if (float3.Dot(top - bottom, up) < 0f)
            top = bottom;
        if (!(float.IsFinite(bottom.x) && float.IsFinite(bottom.y) && float.IsFinite(bottom.z) && float.IsFinite(top.x)
            && float.IsFinite(top.y) && float.IsFinite(top.z)))
            return false;

        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is Collider collider && Counts(collider)
                && Reaches(collider, bottom, top, radius + (collider.Type.Value == ColliderType.Trigger ? 0f : Skin)))
                return true;
        }
        return false;
    }

    private bool Counts(Collider? collider)
        => collider is { IsDestroyed: false } && collider.Enabled.Value
           && (TriggersOnly.Value ? collider.Type.Value == ColliderType.Trigger : collider.Type.Value != ColliderType.NoCollision);

    // Whether a segment comes within reach of the collider's shape, worked out in the slot's own frame.
    private static bool Reaches(Collider collider, in float3 a, in float3 b, float reach)
    {
        var slot = collider.Slot;
        var scale = slot.GlobalScale;
        float size = (MathF.Abs(scale.x) + MathF.Abs(scale.y) + MathF.Abs(scale.z)) / 3f;
        if (!(size > 1e-6f))
            return false;
        float localReach = reach / size;
        var la = slot.GlobalPointToLocal(a);
        var lb = slot.GlobalPointToLocal(b);
        var bounds = collider.GetLocalBounds();
        var middle = (bounds.Min + bounds.Max) * 0.5f;
        float span = (bounds.Max - bounds.Min).Length * 0.5f;
        if (SegmentPointDistance(la, lb, middle) > span + localReach)
            return false;
        var shape = new Shape(collider);
        return Closest(la, lb, shape) <= localReach;
    }

    private readonly struct Shape
    {
        private readonly int _kind;
        private readonly float3 _centre;
        private readonly float3 _half;
        private readonly float _radius;

        public Shape(Collider collider)
        {
            _centre = collider.Offset.Value;
            switch (collider)
            {
                case SphereCollider sphere:
                    _kind = 1;
                    _radius = sphere.Radius.Value;
                    _half = float3.Zero;
                    break;
                case CylinderCollider cylinder:
                    _kind = 2;
                    _radius = cylinder.Radius.Value;
                    _half = new float3(0f, cylinder.Height.Value * 0.5f, 0f);
                    break;
                case CapsuleCollider capsule:
                    _kind = 3;
                    _radius = capsule.Radius.Value;
                    _half = new float3(0f, MathF.Max(capsule.Height.Value * 0.5f - capsule.Radius.Value, 0f), 0f);
                    break;
                case BoxCollider box:
                    _kind = 0;
                    _radius = 0f;
                    _half = box.Size.Value * 0.5f;
                    break;
                default:
                    var bounds = collider.GetLocalBounds();
                    _kind = 0;
                    _radius = 0f;
                    _centre = (bounds.Min + bounds.Max) * 0.5f;
                    _half = (bounds.Max - bounds.Min) * 0.5f;
                    break;
            }
        }

        public float Distance(in float3 point)
        {
            var p = point - _centre;
            switch (_kind)
            {
                case 1:
                    return MathF.Max(p.Length - _radius, 0f);
                case 2:
                    float across = MathF.Max(MathF.Sqrt(p.x * p.x + p.z * p.z) - _radius, 0f);
                    float along = MathF.Max(MathF.Abs(p.y) - _half.y, 0f);
                    return MathF.Sqrt(across * across + along * along);
                case 3:
                    float y = System.Math.Clamp(p.y, -_half.y, _half.y);
                    return MathF.Max(new float3(p.x, p.y - y, p.z).Length - _radius, 0f);
                default:
                    float dx = MathF.Max(MathF.Abs(p.x) - MathF.Abs(_half.x), 0f);
                    float dy = MathF.Max(MathF.Abs(p.y) - MathF.Abs(_half.y), 0f);
                    float dz = MathF.Max(MathF.Abs(p.z) - MathF.Abs(_half.z), 0f);
                    return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
    }

    // Every shape here is convex, so the distance to it along a segment has one least value.
    private static float Closest(in float3 a, in float3 b, in Shape shape)
    {
        var ab = b - a;
        float low = 0f, high = 1f;
        for (int i = 0; i < 24; i++)
        {
            float m1 = low + (high - low) / 3f;
            float m2 = high - (high - low) / 3f;
            if (shape.Distance(a + ab * m1) <= shape.Distance(a + ab * m2))
                high = m2;
            else
                low = m1;
        }
        return shape.Distance(a + ab * ((low + high) * 0.5f));
    }

    private static float SegmentPointDistance(in float3 a, in float3 b, in float3 point)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared;
        float t = lengthSquared > 1e-12f ? System.Math.Clamp(float3.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return (a + ab * t - point).Length;
    }
}
