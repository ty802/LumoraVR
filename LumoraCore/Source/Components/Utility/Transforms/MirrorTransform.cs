// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
