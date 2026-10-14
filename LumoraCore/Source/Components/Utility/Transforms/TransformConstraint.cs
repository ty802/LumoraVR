// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Holds this slot to a weighted blend of other slots: their pose (Parent), position, rotation or scale,
// or turned to face them (Aim, LookAt).
//
// Sources blend by weight; Weight then blends the result with the rest pose, and an axis left out of the
// Affect switches stays at rest. With nothing to follow (no source, or weights adding up to nothing) the
// slot sits at rest. LocalSpace reads the sources' local poses instead of their world ones, for a rig whose
// parts move together inside one parent.
//
// FreezeToWorld pins the slot where it is in the world, sources or not, until it is let go: a prop set
// down on a table, a ball dropped from the hand. Each machine pins it where it sees it at that moment, the
// way a moving avatar is only ever where each machine last heard. Like CopyGlobalTransform this drives the
// slot's own fields, every machine works the pose out for itself, and nothing is sent per frame. -xlinka
[ComponentCategory("Utility/Transforms")]
public class TransformConstraint : Component
{
    public readonly Sync<ConstraintKind> Kind;

    public readonly Sync<float> Weight;

    public readonly SyncList<ConstraintSource> Sources;

    public readonly Sync<bool> LocalSpace;

    public readonly Sync<bool> FreezeToWorld;

    // The local pose for no weight and for axes left out.
    public readonly Sync<float3> RestPosition;
    public readonly Sync<floatQ> RestRotation;
    public readonly Sync<float3> RestScale;

    // Position: added in the parent's space. Rotation, Aim, LookAt: turned by after. Scale: multiplied.
    public readonly Sync<float3> PositionOffset;
    public readonly Sync<floatQ> RotationOffset;
    public readonly Sync<float3> ScaleOffset;

    public readonly Sync<bool> AffectPositionX, AffectPositionY, AffectPositionZ;
    public readonly Sync<bool> AffectRotationX, AffectRotationY, AffectRotationZ;
    public readonly Sync<bool> AffectScaleX, AffectScaleY, AffectScaleZ;

    public readonly Sync<float3> AimAxis;
    public readonly Sync<float3> UpAxis;
    public readonly Sync<ConstraintUp> UpMode;
    public readonly Sync<float3> WorldUp;
    public readonly SyncRef<Slot> UpObject;

    // LookAt: turn about the forward axis, in degrees.
    public readonly Sync<float> Roll;

    protected readonly FieldDrive<float3> _position;
    protected readonly FieldDrive<floatQ> _rotation;
    protected readonly FieldDrive<float3> _scale;

    private bool _frozen;
    private float3 _frozenPosition;
    private floatQ _frozenRotation;

    public TransformConstraint()
    {
        Kind = new Sync<ConstraintKind>(this, ConstraintKind.Parent);
        Weight = new Sync<float>(this, 1f);
        Sources = new SyncList<ConstraintSource>();
        LocalSpace = new Sync<bool>(this, false);
        FreezeToWorld = new Sync<bool>(this, false);
        RestPosition = new Sync<float3>(this, float3.Zero);
        RestRotation = new Sync<floatQ>(this, floatQ.Identity);
        RestScale = new Sync<float3>(this, float3.One);
        PositionOffset = new Sync<float3>(this, float3.Zero);
        RotationOffset = new Sync<floatQ>(this, floatQ.Identity);
        ScaleOffset = new Sync<float3>(this, float3.One);
        AffectPositionX = new Sync<bool>(this, true);
        AffectPositionY = new Sync<bool>(this, true);
        AffectPositionZ = new Sync<bool>(this, true);
        AffectRotationX = new Sync<bool>(this, true);
        AffectRotationY = new Sync<bool>(this, true);
        AffectRotationZ = new Sync<bool>(this, true);
        AffectScaleX = new Sync<bool>(this, true);
        AffectScaleY = new Sync<bool>(this, true);
        AffectScaleZ = new Sync<bool>(this, true);
        AimAxis = new Sync<float3>(this, new float3(0f, 0f, 1f));
        UpAxis = new Sync<float3>(this, new float3(0f, 1f, 0f));
        UpMode = new Sync<ConstraintUp>(this, ConstraintUp.World);
        WorldUp = new Sync<float3>(this, new float3(0f, 1f, 0f));
        UpObject = new SyncRef<Slot>(this);
        Roll = new Sync<float>(this, 0f);
        _position = new FieldDrive<float3>(this) { LocalValueOnly = true };
        _rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
        _scale = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public ConstraintSource AddSource(Slot source, float weight = 1f)
    {
        var entry = Sources.Add();
        entry.Source.Target = source;
        entry.Weight.Value = weight;
        return entry;
    }

    public override void OnStart()
    {
        base.OnStart();
        // Parents first. A constraint under another one that ran first worked its pose out against where the
        // parent sat last frame, and down a long chain of constrained bones that error fed itself every frame
        // until the rig flew apart the moment the body moved. -xlinka
        if (UpdateOrder == 0 && Slot != null)
            UpdateOrder = Slot.Depth;
        ApplyChannels();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        ApplyChannels();
    }

    private void ApplyChannels()
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        var kind = Kind.Value;
        bool on = Enabled;
        bool frozen = FreezeToWorld.Value;
        SetChannel(_position, on && (frozen || kind is ConstraintKind.Parent or ConstraintKind.Position), slot.LocalPosition);
        SetChannel(_rotation, on && (frozen || kind is ConstraintKind.Parent or ConstraintKind.Rotation or ConstraintKind.Aim or ConstraintKind.LookAt), slot.LocalRotation);
        SetChannel(_scale, on && !frozen && kind == ConstraintKind.Scale, slot.LocalScale);
    }

    private static void SetChannel<T>(FieldDrive<T> drive, bool wanted, IField<T> field)
    {
        if (wanted)
        {
            if (!ReferenceEquals(drive.Target, field))
                drive.DriveTarget(field);
        }
        else if (drive.HasTarget)
        {
            drive.ReleaseLink();
        }
    }

    public override void OnUpdate(float delta)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        var parent = slot.Parent;

        if (FreezeToWorld.Value)
        {
            if (!_frozen)
            {
                ApplyChannels();
                _frozenPosition = slot.GlobalPosition;
                _frozenRotation = slot.GlobalRotation;
                _frozen = true;
            }
            if (_position.IsLinkValid)
                _position.SetValue(parent != null ? parent.GlobalPointToLocal(_frozenPosition) : _frozenPosition);
            if (_rotation.IsLinkValid)
                _rotation.SetValue(parent != null ? parent.GlobalRotationToLocal(_frozenRotation) : _frozenRotation);
            return;
        }
        if (_frozen)
        {
            _frozen = false;
            ApplyChannels();
        }

        var kind = Kind.Value;
        float master = System.Math.Clamp(Weight.Value, 0f, 1f);
        bool local = LocalSpace.Value;

        float total = 0f;
        for (int i = 0; i < Sources.Count; i++)
        {
            var entry = Sources[i];
            if (entry?.Source.Target is { IsDestroyed: false } && entry.Weight.Value > 0f)
                total += entry.Weight.Value;
        }
        bool follow = total > 1e-6f;

        if (_position.IsLinkValid)
        {
            var rest = RestPosition.Value;
            var target = follow ? SolvePosition(kind, local, parent, total) : rest;
            _position.SetValue(new float3(
                AffectPositionX.Value ? Lerp(rest.x, target.x, master) : rest.x,
                AffectPositionY.Value ? Lerp(rest.y, target.y, master) : rest.y,
                AffectPositionZ.Value ? Lerp(rest.z, target.z, master) : rest.z));
        }

        if (_rotation.IsLinkValid)
        {
            var rest = RestRotation.Value;
            var target = follow ? SolveRotation(kind, local, slot, parent, total) : rest;
            _rotation.SetValue(BlendRotation(rest, target, master));
        }

        if (_scale.IsLinkValid)
        {
            var rest = RestScale.Value;
            var target = follow ? SolveScale(local, parent, total) : rest;
            _scale.SetValue(new float3(
                AffectScaleX.Value ? Lerp(rest.x, target.x, master) : rest.x,
                AffectScaleY.Value ? Lerp(rest.y, target.y, master) : rest.y,
                AffectScaleZ.Value ? Lerp(rest.z, target.z, master) : rest.z));
        }
    }

    // The blended position in this slot's parent space.
    private float3 SolvePosition(ConstraintKind kind, bool local, Slot? parent, float total)
    {
        var sum = float3.Zero;
        for (int i = 0; i < Sources.Count; i++)
        {
            var entry = Sources[i];
            if (entry?.Source.Target is not { IsDestroyed: false } source || entry.Weight.Value <= 0f)
                continue;
            float3 point;
            if (kind == ConstraintKind.Parent)
            {
                var offset = entry.PositionOffset.Value;
                point = local
                    ? source.LocalPosition.Value + source.LocalRotation.Value * (source.LocalScale.Value * offset)
                    : source.LocalPointToGlobal(offset);
            }
            else
            {
                point = local ? source.LocalPosition.Value : source.GlobalPosition;
            }
            sum += point * entry.Weight.Value;
        }
        var blended = sum / total;
        if (!local && parent != null)
            blended = parent.GlobalPointToLocal(blended);
        if (kind == ConstraintKind.Position)
            blended += PositionOffset.Value;
        return blended;
    }

    // The blended rotation in this slot's parent space.
    private floatQ SolveRotation(ConstraintKind kind, bool local, Slot slot, Slot? parent, float total)
    {
        floatQ world;
        if (kind is ConstraintKind.Aim or ConstraintKind.LookAt)
        {
            var sum = float3.Zero;
            for (int i = 0; i < Sources.Count; i++)
            {
                var entry = Sources[i];
                if (entry?.Source.Target is { IsDestroyed: false } source && entry.Weight.Value > 0f)
                    sum += source.GlobalPosition * entry.Weight.Value;
            }
            var forward = sum / total - slot.GlobalPosition;
            if (forward.LengthSquared < 1e-12f)
                return slot.LocalRotation.Value;
            bool lookAt = kind == ConstraintKind.LookAt;
            var aim = lookAt ? new float3(0f, 0f, 1f) : AimAxis.Value;
            var up = lookAt ? new float3(0f, 1f, 0f) : UpAxis.Value;
            world = AimRotation(aim, up, forward.Normalized, Up(slot));
            if (lookAt && Roll.Value != 0f)
                world = floatQ.AxisAngle(forward.Normalized, Roll.Value * (MathF.PI / 180f)) * world;
            world *= RotationOffset.Value;
            return parent != null ? parent.GlobalRotationToLocal(world) : world;
        }

        var accumulated = new floatQ(0f, 0f, 0f, 0f);
        floatQ? first = null;
        for (int i = 0; i < Sources.Count; i++)
        {
            var entry = Sources[i];
            if (entry?.Source.Target is not { IsDestroyed: false } source || entry.Weight.Value <= 0f)
                continue;
            var rotation = local ? source.LocalRotation.Value : source.GlobalRotation;
            if (kind == ConstraintKind.Parent)
                rotation *= entry.RotationOffset.Value;
            first ??= rotation;
            // Quaternions on opposite hemispheres are the same turn; flip them together before adding.
            float w = floatQ.Dot(first.Value, rotation) < 0f ? -entry.Weight.Value : entry.Weight.Value;
            accumulated = new floatQ(accumulated.x + rotation.x * w, accumulated.y + rotation.y * w,
                accumulated.z + rotation.z * w, accumulated.w + rotation.w * w);
        }
        world = accumulated.Normalized;
        if (kind == ConstraintKind.Rotation)
            world *= RotationOffset.Value;
        if (local || parent == null)
            return world;
        return parent.GlobalRotationToLocal(world);
    }

    private float3 SolveScale(bool local, Slot? parent, float total)
    {
        var sum = float3.Zero;
        for (int i = 0; i < Sources.Count; i++)
        {
            var entry = Sources[i];
            if (entry?.Source.Target is { IsDestroyed: false } source && entry.Weight.Value > 0f)
                sum += (local ? source.LocalScale.Value : source.GlobalScale) * entry.Weight.Value;
        }
        var blended = sum / total;
        if (!local && parent != null)
            blended = parent.GlobalScaleToLocal(blended);
        return blended * ScaleOffset.Value;
    }

    private float3? Up(Slot slot)
    {
        var target = UpObject.Target;
        switch (UpMode.Value)
        {
            case ConstraintUp.None:
                return null;
            case ConstraintUp.ObjectUp when target is { IsDestroyed: false }:
                var toward = target.GlobalPosition - slot.GlobalPosition;
                return toward.LengthSquared > 1e-12f ? toward.Normalized : new float3(0f, 1f, 0f);
            case ConstraintUp.ObjectRotationUp when target is { IsDestroyed: false }:
                return target.GlobalRotation * WorldUp.Value;
            case ConstraintUp.Vector:
                return WorldUp.Value;
            default:
                return new float3(0f, 1f, 0f);
        }
    }

    // Turns aim onto forward, then about forward until up points as near to wantedUp as it can.
    //
    // An up axis along the aim axis decides nothing, and the twist then came from rounding noise: a spine
    // rig aiming down -Y with up on +Y read a target a hair off true as an up half a turn away and spun the
    // hips round to face backwards. Such an up is replaced by the axis a plain look-at frame would use, the
    // one the turn from +Z onto the aim axis carries +Y to, which keeps such a rig's bones where they were
    // authored. -xlinka
    internal static floatQ AimRotation(float3 aim, float3 up, float3 forward, float3? wantedUp)
    {
        aim = aim.Normalized;
        if (float3.Cross(aim, up.Normalized).LengthSquared < 1e-8f)
            up = FromTo(new float3(0f, 0f, 1f), aim) * new float3(0f, 1f, 0f);
        var turn = FromTo(aim, forward);
        if (wantedUp is not { } wanted)
            return turn;
        var have = turn * up;
        have -= forward * float3.Dot(have, forward);
        wanted -= forward * float3.Dot(wanted, forward);
        if (have.LengthSquared < 1e-10f || wanted.LengthSquared < 1e-10f)
            return turn;
        have = have.Normalized;
        wanted = wanted.Normalized;
        float angle = MathF.Atan2(float3.Dot(float3.Cross(have, wanted), forward), float3.Dot(have, wanted));
        return floatQ.AxisAngle(forward, angle) * turn;
    }

    private static floatQ FromTo(float3 from, float3 to)
    {
        float d = System.Math.Clamp(float3.Dot(from, to), -1f, 1f);
        if (d > 0.999999f)
            return floatQ.Identity;
        if (d < -0.999999f)
        {
            var side = MathF.Abs(from.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f);
            return floatQ.AxisAngle(float3.Cross(from, side).Normalized, MathF.PI);
        }
        return floatQ.AxisAngle(float3.Cross(from, to).Normalized, MathF.Acos(d));
    }

    private floatQ BlendRotation(floatQ rest, floatQ target, float weight)
    {
        bool x = AffectRotationX.Value, y = AffectRotationY.Value, z = AffectRotationZ.Value;
        if (x && y && z)
            return floatQ.Slerp(rest, target, weight);
        if (!x && !y && !z)
            return rest;
        // Per axis, in the same Y-then-X-then-Z angles both poses are taken apart into.
        var a = Angles(rest);
        var b = Angles(floatQ.Slerp(rest, target, weight));
        return FromAngles(new float3(x ? b.x : a.x, y ? b.y : a.y, z ? b.z : a.z));
    }

    private static float3 Angles(floatQ q)
    {
        float m02 = 2f * (q.x * q.z + q.w * q.y);
        float m22 = 1f - 2f * (q.x * q.x + q.y * q.y);
        float m12 = 2f * (q.y * q.z - q.w * q.x);
        float m10 = 2f * (q.x * q.y + q.w * q.z);
        float m11 = 1f - 2f * (q.x * q.x + q.z * q.z);
        return new float3(MathF.Asin(System.Math.Clamp(-m12, -1f, 1f)), MathF.Atan2(m02, m22), MathF.Atan2(m10, m11));
    }

    private static floatQ FromAngles(float3 angles)
        => floatQ.AxisAngle(new float3(0f, 1f, 0f), angles.y)
         * floatQ.AxisAngle(new float3(1f, 0f, 0f), angles.x)
         * floatQ.AxisAngle(new float3(0f, 0f, 1f), angles.z);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
