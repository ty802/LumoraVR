// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Components.Interaction;

// A surface things are set down on. Let go of something receivable near it and it comes to rest there, lying
// flat: looked for along Direction (this slot's own down unless set), then along each of ExtraDirections,
// from the middle of the thing's bounds pulled back by CheckOffset, against this surface's own colliders
// only. It turns so whichever face of its bounds already faces the surface most lies flat on it, and that
// face comes to rest on the hit, Lift above it. Reach is how far the surface may be from that middle, in
// this slot's scale; past it the thing still lands if the far corner of its bounds (the maximum on every
// one of its own axes) reaches the surface within Reach. With ParentDropped it goes under this slot as
// well, or under ParentUnder when set, so it rides along when the surface moves. TagWhitelist, when it
// lists anything, takes only things whose slot carries one of those tags; TagBlacklist refuses its tags.
//
// It eases into place over GlideTime. The glide is local to the machine of whoever let go and only the
// resting pose replicates, written when the glide ends: that hand is gone by then, so the surface claims
// the placing on the grabbable during the release (Grabbable.BeginPlacing) and gives it up once the thing
// is down. A thing taken off and let go of anywhere else does not come back under it (IReleaseReturnBlock,
// this slot and its children).
//
// The surface needs a collider, on its own slot or a child: a release finds drop targets by overlapping a
// sphere around the hand. Received tells anything listening on that machine. -xlinka
[ComponentCategory("Interaction/Grabbables")]
public sealed class DropSurface : Component, IGrabbableReceiver, IReleaseReturnBlock
{
    private const int MaxCasts = 8;
    private const float StepPast = 0.001f;
    private const float FarCast = 10000f;

    public readonly Sync<float> Reach;
    public readonly Sync<float> Lift;
    public readonly Sync<bool> ParentDropped;

    // In this slot's space. Zero means down.
    public readonly Sync<float3> Direction;

    // Tried after Direction, in order, each from where the one before left it.
    public readonly SyncFieldList<float3> ExtraDirections;

    // In this slot's scale.
    public readonly Sync<float> CheckOffset;

    // Seconds. Zero, the default, puts it down at once.
    public readonly Sync<float> GlideTime;

    public readonly SyncRef<Slot> ParentUnder;

    public readonly SyncFieldList<string> TagWhitelist;
    public readonly SyncFieldList<string> TagBlacklist;

    public event Action<IGrabbable, Grabber>? Received;

    private readonly List<Slot> _exclude = new(1);

    private struct Glide
    {
        public Slot Slot;
        public Slot? Parent;
        public Grabbable? Claim;
        public float3 FromPosition;
        public floatQ FromRotation;
        public float3 ToPosition;
        public floatQ ToRotation;
        public float Elapsed;
    }

    private readonly List<Glide> _glides = new();

    public DropSurface()
    {
        Reach = new Sync<float>(this, 1f);
        Lift = new Sync<float>(this, 0f);
        ParentDropped = new Sync<bool>(this, false);
        Direction = new Sync<float3>(this, float3.Down);
        ExtraDirections = new SyncFieldList<float3>(this);
        CheckOffset = new Sync<float>(this, 0f);
        GlideTime = new Sync<float>(this, 0f);
        ParentUnder = new SyncRef<Slot>(this);
        TagWhitelist = new SyncFieldList<string>(this);
        TagBlacklist = new SyncFieldList<string>(this);
    }

    bool IReleaseReturnBlock.BlocksReturn => true;

    int IReleaseReturnBlock.ReturnBlockDepth => 1;

    // How far the thing would move to rest here: the least over every direction that takes it.
    public float? GetReceiveDistance(IGrabbable grabbable, Grabber grabber)
    {
        if (grabbable is not Component { Slot: { IsDestroyed: false } slot } || !CanTake(slot))
            return null;
        var position = slot.GlobalPosition;
        var rotation = slot.GlobalRotation;
        float best = float.MaxValue;
        for (int i = 0; i <= ExtraDirections.Count; i++)
        {
            if (TryPlace(slot, DirectionAt(i), position, rotation, out var placed, out _))
                best = MathF.Min(best, float3.Distance(placed, position));
        }
        return best < float.MaxValue ? best : null;
    }

    public void Receive(IGrabbable grabbable, Grabber grabber)
    {
        if (grabbable is not Component { Slot: { IsDestroyed: false } slot } || !CanTake(slot))
            return;
        var fromPosition = slot.GlobalPosition;
        var fromRotation = slot.GlobalRotation;
        var position = fromPosition;
        var rotation = fromRotation;
        bool placed = false;
        for (int i = 0; i <= ExtraDirections.Count; i++)
        {
            if (TryPlace(slot, DirectionAt(i), position, rotation, out var nextPosition, out var nextRotation))
            {
                position = nextPosition;
                rotation = nextRotation;
                placed = true;
            }
        }
        if (!placed)
            return;

        // Inside the hand's release, which offers everything it let go of in turn: a refused write ends
        // here instead of taking the rest of that release with it.
        try
        {
            var parent = ParentUnder.Target is { IsDestroyed: false } under ? under : Slot;
            if (ParentDropped.Value && !ReferenceEquals(slot.Parent, parent) && !ReferenceEquals(slot, parent) && !parent.IsDescendantOf(slot))
                slot.SetParent(parent, preserveGlobalTransform: true);

            if (GlideTime.Value > 0f)
                StartGlide(slot, grabbable as Grabbable, fromPosition, fromRotation, position, rotation);
            else
            {
                slot.GlobalPosition = position;
                slot.GlobalRotation = rotation;
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"DropSurface on {ParentHierarchyToString()} could not take {slot.Name.Value}: {ex.Message}");
            return;
        }
        try
        {
            Received?.Invoke(grabbable, grabber);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"DropSurface on {ParentHierarchyToString()}: a listener threw: {ex.Message}");
        }
    }

    private float3 DirectionAt(int index)
    {
        var local = index == 0 ? Direction.Value : ExtraDirections[index - 1];
        return local.LengthSquared > 1e-12f ? local.Normalized : float3.Down;
    }

    private bool CanTake(Slot dropped)
        => World?.Physics != null && !IsDestroyed && Enabled.Value && Slot.IsActive
           && !ReferenceEquals(dropped, Slot) && !Slot.IsDescendantOf(dropped) && Takes(dropped);

    // Where the thing comes to rest along one direction, from the given world pose. Pure: nothing is
    // written, so asking costs a few casts and moves nothing.
    private bool TryPlace(Slot dropped, float3 localDirection, float3 position, floatQ rotation, out float3 placedPosition, out floatQ placedRotation)
    {
        placedPosition = position;
        placedRotation = rotation;
        var world = World;
        if (world?.Physics == null)
            return false;
        if (!SlotBoundsHelper.TryComputeLocalBounds(dropped, out var bounds) || bounds.IsEmpty)
            return false;

        var down = Slot.GlobalRotation * localDirection;
        if (!(down.LengthSquared > 1e-12f))
            return false;
        down = down.Normalized;
        float reach = Slot.LocalScaleToGlobal(MathF.Max(Reach.Value, 0f));
        if (float.IsNaN(reach))
            return false;
        if (float.IsInfinity(reach))
            reach = float.MaxValue;
        float lift = Slot.LocalScaleToGlobal(Lift.Value);
        float pullBack = Slot.LocalScaleToGlobal(CheckOffset.Value);
        var scale = dropped.GlobalScale;
        var localCenter = bounds.Center;
        var middle = position + rotation * (localCenter * scale);
        if (!Finite(middle))
            return false;

        _exclude.Clear();
        _exclude.Add(dropped);
        if (!CastToSurface(middle - down * pullBack, down, FarCast, out var hit))
            return false;
        if (hit.Distance > reach)
        {
            var far = position + rotation * (bounds.Max * scale);
            if (!CastToSurface(far, down, MathF.Min(reach, FarCast), out _))
                return false;
        }

        var normal = hit.Normal;
        if (!(normal.LengthSquared > 1e-12f))
            normal = -down;
        normal = normal.Normalized;
        var into = -normal;

        // The face of the bounds already turned most toward the surface is the one that lies on it.
        int face = 0;
        float bestDot = float.MinValue;
        for (int i = 0; i < 6; i++)
        {
            float dot = float3.Dot(rotation * FaceDirection(i), into);
            if (dot > bestDot)
            {
                bestDot = dot;
                face = i;
            }
        }
        var turned = FromTo(rotation * FaceDirection(face), into) * rotation;

        // Turned about its middle, then carried along the normal until that face rests Lift above the hit.
        var shifted = middle - turned * (localCenter * scale);
        var half = (bounds.Max - bounds.Min) * 0.5f;
        float halfAlong = MathF.Abs(Axis(half * scale, face % 3));
        float travel = hit.Distance - halfAlong - lift;
        placedRotation = turned;
        placedPosition = shifted + into * travel;
        return Finite(placedPosition);
    }

    // This surface's colliders only: anything else in the way is stepped past.
    private bool CastToSurface(float3 origin, float3 direction, float distance, out PhysicsRaycastHit hit)
    {
        hit = default;
        var physics = World!.Physics;
        float travelled = 0f;
        for (int cast = 0; cast < MaxCasts && travelled < distance; cast++)
        {
            if (!physics.Raycast(origin + direction * travelled, direction, distance - travelled, _exclude, out var found, hitTriggers: true))
                return false;
            float along = travelled + found.Distance;
            if (found.Slot != null && ReferenceEquals(found.Slot.GetComponentInParents<DropSurface>(), this))
            {
                hit = found;
                hit.Distance = along;
                return true;
            }
            travelled = along + StepPast;
        }
        return false;
    }

    private bool Takes(Slot dropped)
    {
        string tag = dropped.Tag.Value ?? string.Empty;
        if (TagWhitelist.Count > 0 && !Listed(TagWhitelist, tag))
            return false;
        return TagBlacklist.Count == 0 || !Listed(TagBlacklist, tag);
    }

    private static bool Listed(SyncFieldList<string> list, string tag)
    {
        foreach (var entry in list)
        {
            if (string.Equals(entry, tag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // GLIDE
    // Every frame of it goes in through the silent setters, which move the thing for this machine without
    // queueing a delta; the resting pose is the one write that replicates, at the end. Other machines see
    // the drop pose for the glide's length and then the resting pose.
    private void StartGlide(Slot slot, Grabbable? grabbable, float3 fromPosition, floatQ fromRotation, float3 toPosition, floatQ toRotation)
    {
        var parent = slot.Parent;
        var glide = new Glide
        {
            Slot = slot,
            Parent = parent,
            Claim = grabbable != null && grabbable.BeginPlacing() ? grabbable : null,
            FromPosition = ToLocal(parent, fromPosition),
            FromRotation = ToLocal(parent, fromRotation),
            ToPosition = ToLocal(parent, toPosition),
            ToRotation = ToLocal(parent, toRotation),
        };
        for (int i = _glides.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_glides[i].Slot, slot))
            {
                Finish(_glides[i], write: false);
                _glides.RemoveAt(i);
            }
        }
        _glides.Add(glide);
        slot.LocalPosition.SetValueSilently(glide.FromPosition, change: true);
        slot.LocalRotation.SetValueSilently(glide.FromRotation, change: true);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_glides.Count == 0)
            return;
        float duration = MathF.Max(GlideTime.Value, 1e-4f);
        for (int i = _glides.Count - 1; i >= 0; i--)
        {
            var glide = _glides[i];
            var slot = glide.Slot;
            if (slot == null || slot.IsDestroyed || !ReferenceEquals(slot.Parent, glide.Parent) || glide.Claim is { IsGrabbed: true })
            {
                // Taken, moved or gone mid-glide: where it is now is someone else's business.
                Finish(glide, write: false);
                _glides.RemoveAt(i);
                continue;
            }
            glide.Elapsed += delta;
            float t = System.Math.Clamp(glide.Elapsed / duration, 0f, 1f);
            if (t >= 1f)
            {
                Finish(glide, write: true);
                _glides.RemoveAt(i);
                continue;
            }
            float eased = MathF.Sin(t * MathF.PI * 0.5f);
            slot.LocalPosition.SetValueSilently(float3.Lerp(glide.FromPosition, glide.ToPosition, eased), change: true);
            slot.LocalRotation.SetValueSilently(floatQ.Slerp(glide.FromRotation, glide.ToRotation, eased), change: true);
            _glides[i] = glide;
        }
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        FinishAll();
    }

    public override void OnDestroy()
    {
        FinishAll();
        base.OnDestroy();
    }

    // Stopping early still posts where each thing was headed, or the other machines keep the drop pose.
    private void FinishAll()
    {
        for (int i = 0; i < _glides.Count; i++)
        {
            var glide = _glides[i];
            bool still = glide.Slot is { IsDestroyed: false } slot && ReferenceEquals(slot.Parent, glide.Parent) && glide.Claim is not { IsGrabbed: true };
            Finish(glide, write: still);
        }
        _glides.Clear();
    }

    private static void Finish(in Glide glide, bool write)
    {
        try
        {
            if (write && glide.Slot is { IsDestroyed: false } slot)
            {
                slot.LocalPosition.Value = glide.ToPosition;
                slot.LocalRotation.Value = glide.ToRotation;
            }
        }
        finally
        {
            glide.Claim?.EndPlacing();
        }
    }

    private static float3 ToLocal(Slot? parent, float3 point) => parent == null || parent.IsRootSlot ? point : parent.GlobalPointToLocal(point);

    private static floatQ ToLocal(Slot? parent, floatQ rotation) => parent == null || parent.IsRootSlot ? rotation : parent.GlobalRotation.Inverse * rotation;

    private static float3 FaceDirection(int index)
    {
        float sign = index < 3 ? -1f : 1f;
        return (index % 3) switch
        {
            0 => new float3(sign, 0f, 0f),
            1 => new float3(0f, sign, 0f),
            _ => new float3(0f, 0f, sign),
        };
    }

    private static float Axis(in float3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

    private static bool Finite(in float3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    // The shortest turn carrying one unit direction onto another.
    private static floatQ FromTo(float3 from, float3 to)
    {
        float dot = float3.Dot(from, to);
        if (dot > 0.999999f)
            return floatQ.Identity;
        if (dot < -0.999999f)
        {
            var side = float3.Cross(float3.Right, from);
            if (side.LengthSquared < 1e-8f)
                side = float3.Cross(float3.Up, from);
            return floatQ.AxisAngleRad(side.Normalized, MathF.PI);
        }
        var axis = float3.Cross(from, to);
        return new floatQ(axis.x, axis.y, axis.z, 1f + dot).Normalized;
    }
}
