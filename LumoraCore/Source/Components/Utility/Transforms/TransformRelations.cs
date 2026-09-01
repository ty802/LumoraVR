// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Holds this slot on another slot's global transform, channel by channel.
//
// This is a DRIVER: the pose lands through FieldDrive members on this slot's own transform fields, so
// the fields read as driven, are excluded from value sync, and let go the moment the component does.
// SlotMirror is the other shape - a follower that writes a REMOTE slot's fields by hand every frame -
// and the two are not interchangeable: a follower can push a slot it does not own, a driver can only
// hold its own, and only the driver's target is protected from a second writer. -xlinka
[ComponentCategory("Utility/Transforms")]
public class CopyGlobalTransform : Component
{
    public readonly SyncRef<Slot> Source;

    public readonly Sync<bool> CopyPosition;

    public readonly Sync<bool> CopyRotation;

    public readonly Sync<bool> CopyScale;

    // Average the copied scale across the axes, for a source whose parents squash it unevenly.
    public readonly Sync<bool> UniformScale;

    protected readonly FieldDrive<float3> _position;

    protected readonly FieldDrive<floatQ> _rotation;

    protected readonly FieldDrive<float3> _scale;

    public CopyGlobalTransform()
    {
        Source = new SyncRef<Slot>(this);
        CopyPosition = new Sync<bool>(this, true);
        CopyRotation = new Sync<bool>(this, true);
        CopyScale = new Sync<bool>(this, false);
        UniformScale = new Sync<bool>(this, false);
        _position = new FieldDrive<float3>(this) { LocalValueOnly = true };
        _rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
        _scale = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        ApplyChannels();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        ApplyChannels();
    }

    // The channel switches own the LINK, not just the write. A drive that holds a field it never writes
    // still reads as driven everywhere - tinted in the inspector, refused to value sync, and blocking
    // any other driver - which is a worse lie than an off switch. -xlinka
    private void ApplyChannels()
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        SetChannel(_position, CopyPosition.Value, slot.LocalPosition);
        SetChannel(_rotation, CopyRotation.Value, slot.LocalRotation);
        SetChannel(_scale, CopyScale.Value, slot.LocalScale);
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
        var source = Source.Target;
        var slot = Slot;
        if (source == null || source.IsDestroyed || slot == null)
            return;

        var parent = slot.Parent;

        if (_position.IsLinkValid)
        {
            var global = source.GlobalPosition;
            _position.SetValue(parent != null ? parent.GlobalPointToLocal(global) : global);
        }

        if (_rotation.IsLinkValid)
        {
            var global = source.GlobalRotation;
            _rotation.SetValue(parent != null ? parent.GlobalRotationToLocal(global) : global);
        }

        if (_scale.IsLinkValid)
        {
            var global = source.GlobalScale;
            var local = parent != null ? parent.GlobalScaleToLocal(global) : global;
            if (UniformScale.Value)
            {
                float average = (local.x + local.y + local.z) / 3f;
                local = new float3(average, average, average);
            }
            _scale.SetValue(local);
        }
    }
}

// Holds this slot at another slot's pose reflected across a plane.
//
// The plane is a slot plus a normal in that slot's space, so the mirror moves with whatever it is
// mounted on. With no plane slot the normal and offset are read as world space.
//
// A reflection flips handedness, so this mirrors the POSE, not the geometry: text on the source reads
// forwards on the copy, not backwards. Negating a scale axis to flip the geometry too would invert the
// winding of every mesh under it, which is a renderer problem, not a transform one. -xlinka
[ComponentCategory("Utility/Transforms")]
public class MirrorTransform : Component
{
    public readonly SyncRef<Slot> Source;

    public readonly SyncRef<Slot> MirrorPlane;

    // A point on the plane, in the plane slot's space.
    public readonly Sync<float3> PlaneOffset;

    // In the plane slot's space.
    public readonly Sync<float3> PlaneNormal;

    public readonly Sync<bool> MirrorPosition;

    public readonly Sync<bool> MirrorRotation;

    protected readonly FieldDrive<float3> _position;

    protected readonly FieldDrive<floatQ> _rotation;

    public MirrorTransform()
    {
        Source = new SyncRef<Slot>(this);
        MirrorPlane = new SyncRef<Slot>(this);
        PlaneOffset = new Sync<float3>(this, float3.Zero);
        PlaneNormal = new Sync<float3>(this, float3.Forward);
        MirrorPosition = new Sync<bool>(this, true);
        MirrorRotation = new Sync<bool>(this, true);
        _position = new FieldDrive<float3>(this) { LocalValueOnly = true };
        _rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
    }

    public float3 PlanePoint
    {
        get
        {
            var plane = MirrorPlane.Target;
            return plane != null && !plane.IsDestroyed
                ? plane.LocalPointToGlobal(PlaneOffset.Value)
                : PlaneOffset.Value;
        }
    }

    public float3 PlaneDirection
    {
        get
        {
            var plane = MirrorPlane.Target;
            var normal = plane != null && !plane.IsDestroyed
                ? plane.LocalDirectionToGlobal(PlaneNormal.Value)
                : PlaneNormal.Value;
            return normal.LengthSquared > 0f ? normal.Normalized : float3.Forward;
        }
    }

    public override void OnStart()
    {
        base.OnStart();
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

        if (MirrorPosition.Value)
        {
            if (!ReferenceEquals(_position.Target, slot.LocalPosition))
                _position.DriveTarget(slot.LocalPosition);
        }
        else if (_position.HasTarget)
        {
            _position.ReleaseLink();
        }

        if (MirrorRotation.Value)
        {
            if (!ReferenceEquals(_rotation.Target, slot.LocalRotation))
                _rotation.DriveTarget(slot.LocalRotation);
        }
        else if (_rotation.HasTarget)
        {
            _rotation.ReleaseLink();
        }
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var slot = Slot;
        if (source == null || source.IsDestroyed || slot == null)
            return;

        var normal = PlaneDirection;
        var parent = slot.Parent;

        if (_position.IsLinkValid)
        {
            var reflected = ReflectPoint(source.GlobalPosition, normal, PlanePoint);
            _position.SetValue(parent != null ? parent.GlobalPointToLocal(reflected) : reflected);
        }

        if (_rotation.IsLinkValid)
        {
            var reflected = ReflectRotation(source.GlobalRotation, normal);
            _rotation.SetValue(parent != null ? parent.GlobalRotationToLocal(reflected) : reflected);
        }
    }

    public static float3 ReflectPoint(float3 point, float3 normal, float3 planePoint)
        => point - normal * (2f * float3.Dot(point - planePoint, normal));

    // The mirror image of a rotation is the same angle the other way about the reflected axis, which
    // falls out of the quaternion as "keep w, reflect the vector part and negate it". Building it from
    // a reflected forward and up instead would need a look rotation, and ours hands back the inverse.
    // -xlinka
    public static floatQ ReflectRotation(floatQ rotation, float3 normal)
    {
        var vector = new float3(rotation.x, rotation.y, rotation.z);
        var mirrored = normal * (2f * float3.Dot(vector, normal)) - vector;
        return new floatQ(mirrored.x, mirrored.y, mirrored.z, rotation.w);
    }
}

// Parks this slot at a fixed offset from another slot, in that slot's space.
//
// Anchoring on bounds is what makes it useful for labels and badges: "just above whatever this is",
// without hand-measuring the model. Bounds cost a walk of the reference's whole subtree, so they are
// measured on a slow clock rather than every frame - the anchor point of a thing that is not changing
// shape does not need recomputing sixty times a second. -xlinka
[ComponentCategory("Utility/Transforms")]
public class RelativePositioner : Component
{
    private const double BoundsRefreshSeconds = 0.5;

    public readonly SyncRef<Slot> Reference;

    // Where on the reference's bounds to sit, per axis, from -1 at the low face through 0 at the centre
    // to 1 at the high face. Ignored while UseBounds is off.
    public readonly Sync<float3> Anchor;

    // Added after the anchor, in the reference's space.
    public readonly Sync<float3> Offset;

    public readonly Sync<bool> UseBounds;

    protected readonly FieldDrive<float3> _position;

    private BoundingBox _bounds;
    private bool _hasBounds;
    private double _nextMeasure;

    public RelativePositioner()
    {
        Reference = new SyncRef<Slot>(this);
        Anchor = new Sync<float3>(this, float3.Zero);
        Offset = new Sync<float3>(this, float3.Zero);
        UseBounds = new Sync<bool>(this, false);
        _position = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        if (_position.ShouldApplyDefault)
            _position.DriveTarget(Slot.LocalPosition);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // A retargeted reference must not keep the old model's box for half a second.
        _hasBounds = false;
        _nextMeasure = 0d;
    }

    public override void OnUpdate(float delta)
    {
        if (!_position.IsLinkValid)
            return;

        var reference = Reference.Target;
        var slot = Slot;
        if (reference == null || reference.IsDestroyed || slot == null)
            return;

        var local = Offset.Value;
        if (UseBounds.Value)
        {
            RefreshBounds(reference);
            if (_hasBounds)
            {
                var extents = _bounds.Size * 0.5f;
                var anchor = Anchor.Value;
                local += _bounds.Center + new float3(
                    extents.x * anchor.x,
                    extents.y * anchor.y,
                    extents.z * anchor.z);
            }
        }

        var global = reference.LocalPointToGlobal(local);
        var parent = slot.Parent;
        _position.SetValue(parent != null ? parent.GlobalPointToLocal(global) : global);
    }

    private void RefreshBounds(Slot reference)
    {
        double now = UtilityClock.Seconds(World);
        if (_hasBounds && now < _nextMeasure)
            return;
        _nextMeasure = now + BoundsRefreshSeconds;
        _hasBounds = SlotBoundsHelper.TryComputeLocalBounds(reference, out _bounds);
    }
}
