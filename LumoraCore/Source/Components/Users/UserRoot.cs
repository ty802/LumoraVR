// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

[ComponentCategory("Users")]
public class UserRoot : Component
{
    public enum UserNode
    {
        None,
        Root,
        Head,
        Body,
        LeftHand,
        RightHand,
        LeftFoot,
        RightFoot
    }

    // USER REFERENCE
    // so clients can identify their own
    public readonly SyncRef<User> TargetUser = null!;

    private bool _isRegistered = false;

    public User ActiveUser => (TargetUser?.Target) ?? null!;

    public bool IsLocalUserRoot => TargetUser?.Target != null && TargetUser.Target == World?.LocalUser;

    // CACHED BODY NODES
    private Slot _cachedHeadSlot = null!;
    private Slot _cachedBodySlot = null!;
    private Slot _cachedLeftHandSlot = null!;
    private Slot _cachedRightHandSlot = null!;
    private Slot _cachedLeftFootSlot = null!;
    private Slot _cachedRightFootSlot = null!;

    // BODY NODE ACCESSORS
    //
    // Resolved from the typed UserRootComponent registry rather than string-
    // named child lookups. Any TrackedDevicePositioner under the user that
    // declares AutoBodyNode == HeadNode shows up here automatically - slots
    // can be renamed or moved without breaking the lookup. - xlinka

    public Slot HeadSlot => GetBodyNodeSlot(ref _cachedHeadSlot, Input.BodyNode.Head)
                            ?? FallbackChildLookup(ref _cachedHeadSlot, "Head");

    public Slot LeftHandSlot => GetBodyNodeSlot(ref _cachedLeftHandSlot, Input.BodyNode.LeftHand)
                                ?? FallbackChildLookup(ref _cachedLeftHandSlot, "LeftHand");

    public Slot RightHandSlot => GetBodyNodeSlot(ref _cachedRightHandSlot, Input.BodyNode.RightHand)
                                 ?? FallbackChildLookup(ref _cachedRightHandSlot, "RightHand");

    public Slot LeftFootSlot => GetBodyNodeSlot(ref _cachedLeftFootSlot, Input.BodyNode.LeftFoot)
                                ?? FallbackChildLookup(ref _cachedLeftFootSlot, "LeftFoot");

    public Slot RightFootSlot => GetBodyNodeSlot(ref _cachedRightFootSlot, Input.BodyNode.RightFoot)
                                 ?? FallbackChildLookup(ref _cachedRightFootSlot, "RightFoot");

    // Body component (avatar torso) - not a tracked device, still needs string lookup.
    public Slot BodySlot
    {
        get
        {
            if ((_cachedBodySlot == null || _cachedBodySlot.IsDestroyed) && !IsDestroyed)
            {
                _cachedBodySlot = Slot.FindChild("Avatar", recursive: false)?.FindChild("Body", recursive: false)!;
            }
            return _cachedBodySlot!;
        }
    }

    private Slot GetBodyNodeSlot(ref Slot cache, Input.BodyNode node)
    {
        if (IsDestroyed)
            return null!;

        var positioner = GetRegisteredComponent<TrackedDevicePositioner>(p => p.AutoBodyNode.Value == node);
        var resolved = positioner?.Slot;

        if (cache != null && !cache.IsDestroyed)
        {
            // A tracked device positioner has two relevant slots:
            // - Slot: the tracked body transform (head/controller/etc.)
            // - BodyNodeRoot: an equipment/object child that only carries offsets
            // User-space consumers need the tracked transform. Older code cached the
            // child, which double-applied desktop head height and moved nametags/IK.
            if (resolved == null || ReferenceEquals(cache, resolved))
                return cache;
        }

        // Not every body node is device-tracked: hands are AvatarSockets
        // riding under the controller positioners. Resolve through the
        // registered object slots before giving up.
        if (resolved == null)
        {
            resolved = GetRegisteredComponent<Avatar.AvatarSocket>(s => s.Node.Value == node)?.Slot;
        }

        if (resolved != null)
            cache = resolved;
        return resolved!;
    }

    // Old string-named child lookup. Kept as a fallback for slots that exist
    // before the matching TrackedDevicePositioner registers, and for avatars
    // built by templates that don't use tracked-device positioners at all.
    // - xlinka
    private Slot FallbackChildLookup(ref Slot cache, string name)
    {
        if (cache != null && !cache.IsDestroyed)
            return cache;
        if (IsDestroyed)
            return null!;

        cache = (Slot.FindChild("Body Nodes", recursive: false)?.FindChild(name, recursive: false) ??
                 Slot.FindChild(name, recursive: false) ??
                 Slot.FindChild("Avatar", recursive: false)?.FindChild(name, recursive: false))!;
        return cache!;
    }

    // POSITION ACCESSORS

    public float3 HeadPosition
    {
        get => HeadSlot?.GlobalPosition ?? Slot.GlobalPosition;
        set
        {
            if (HeadSlot != null)
            {
                var offset = value - HeadPosition;
                Slot.GlobalPosition += offset;
            }
        }
    }

    public floatQ HeadRotation
    {
        get => HeadSlot?.GlobalRotation ?? Slot.GlobalRotation;
        set
        {
            if (HeadSlot != null)
            {
                // TODO: Platform driver - Rotation delta calculation
                // var currentPos = HeadPosition;
                // var rotationDelta = value * HeadRotation.Inverse();
                // Slot.GlobalTransform = new Transform3D(
                // 	Slot.GlobalTransform.Basis * new Basis(rotationDelta),
                // 	Slot.GlobalPosition
                // );
                // HeadPosition = currentPos; // Restore head position
                HeadSlot.GlobalRotation = value;
            }
        }
    }

    public float3 FeetPosition
    {
        get
        {
            if (LeftFootSlot != null && RightFootSlot != null)
            {
                return (LeftFootSlot.GlobalPosition + RightFootSlot.GlobalPosition) / 2f;
            }
            // Fallback: project head position to ground
            var headPos = HeadPosition;
            return new float3(headPos.x, Slot.GlobalPosition.y, headPos.z);
        }
        set
        {
            var offset = value - FeetPosition;
            Slot.GlobalPosition += offset;
        }
    }

    public float GlobalScale
    {
        get => Slot.Scale.x;
        set
        {
            Slot.Scale = float3.One * value;
        }
    }

    // keeps the head world position fixed, matching VR comfort expectations for snap and smooth turning
    public void RotateAroundHead(floatQ deltaRotation)
    {
        if (Slot == null)
            return;

        var headBefore = HeadPosition;
        Slot.GlobalRotation = (deltaRotation * Slot.GlobalRotation).Normalized;
        var headAfter = HeadPosition;

        var offset = headBefore - headAfter;
        if (offset.LengthSquared > 0f)
            Slot.GlobalPosition += offset;
    }

    public void RotateYawAroundHead(float yawRadians)
    {
        if (System.Math.Abs(yawRadians) < 0.000001f)
            return;

        RotateAroundHead(floatQ.AxisAngle(float3.Up, yawRadians));
    }

    public bool ReceivedFirstPositionalData
    {
        get
        {
            if (HeadSlot != null)
            {
                var headPos = HeadSlot.LocalPosition.Value;
                var headRot = HeadSlot.LocalRotation.Value;
                if (headPos != float3.Zero || headRot != floatQ.Identity)
                    return true;
            }

            var headPositioner = HeadSlot?.GetComponent<TrackedDevicePositioner>();
            if (headPositioner != null)
                return headPositioner.IsTracking.Value;

            // For desktop mode, we consider it always tracked
            if (ActiveUser != null && Engine.Current?.InputInterface != null)
            {
                var headDevice = Engine.Current.InputInterface.HeadDevice;
                return headDevice == null || !headDevice.IsTracked; // Desktop mode
            }

            return false;
        }
    }

    // INITIALIZATION

    // called by SimpleUserSpawn after attaching the component
    public void Initialize(User user)
    {
        if (user == null)
        {
            LumoraLogger.Error("UserRoot: Cannot initialize with null user");
            return;
        }

        TargetUser.Target = user;

        if (World?.IsAuthority == true)
        {
            user.Root = this;
            LumoraLogger.Log($"User: Registered UserRoot for authority user '{user.UserName.Value}'");
        }

        LumoraLogger.Log($"UserRoot: Initialized for user '{user.UserName.Value}' (RefID: {user.ReferenceID})");
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Slot?.RegisterUserRoot(this);
    }

    // Typed component cache. Every UserRootComponent in the user's slot
    // hierarchy registers here. Lookups like "find the AvatarIK attached
    // anywhere under this user" are O(1) by type instead of walking the
    // slot tree + GetComponents per slot. - xlinka
    private readonly HashSet<Component> _registeredComponents = new();
    private readonly Dictionary<Type, IList> _perTypeComponents = new();

    internal void RegisterComponent(Component component)
    {
        if (component == null || !_registeredComponents.Add(component))
            return;

        var type = component.GetType();
        foreach (var kvp in _perTypeComponents)
        {
            if (kvp.Key.IsAssignableFrom(type))
                kvp.Value.Add(component);
        }
    }

    internal void UnregisterComponent(Component component)
    {
        if (component == null || !_registeredComponents.Remove(component))
            return;

        var type = component.GetType();
        foreach (var kvp in _perTypeComponents)
        {
            if (kvp.Key.IsAssignableFrom(type))
                kvp.Value.Remove(component);
        }
    }

    private List<T> GetComponentsOfType<T>() where T : class
    {
        if (_perTypeComponents.TryGetValue(typeof(T), out var existing))
            return (List<T>)existing;

        var list = new List<T>();
        foreach (var c in _registeredComponents)
        {
            if (c is T match)
                list.Add(match);
        }
        _perTypeComponents[typeof(T)] = list;
        return list;
    }

    public T GetRegisteredComponent<T>(Predicate<T> filter = null!) where T : class
    {
        foreach (var item in GetComponentsOfType<T>())
        {
            if (item is Component c && c.IsDestroyed) continue;
            if (filter == null || filter(item))
                return item;
        }
        return null!;
    }

    public void GetRegisteredComponents<T>(List<T> output, Predicate<T> filter = null!) where T : class
    {
        foreach (var item in GetComponentsOfType<T>())
        {
            if (item is Component c && c.IsDestroyed) continue;
            if (filter == null || filter(item))
                output.Add(item);
        }
    }

    public List<T> GetRegisteredComponents<T>(Predicate<T> filter = null!) where T : class
    {
        var list = new List<T>();
        GetRegisteredComponents(list, filter);
        return list;
    }

    public void ForeachRegisteredComponent<T>(Action<T> action) where T : class
    {
        foreach (var item in GetComponentsOfType<T>())
        {
            if (item is Component c && c.IsDestroyed) continue;
            action(item);
        }
    }

    public override void OnChanges()
    {
        base.OnChanges();

        if (TargetUser.Target == World?.LocalUser && !_isRegistered)
        {
            World.LocalUser.Root = this;
            _isRegistered = true;
            LumoraLogger.Log($"UserRoot: Registered as Root for local user '{TargetUser.Target?.UserName?.Value}'");
        }

        if (TargetUser.Target != World?.LocalUser && _isRegistered)
        {
            if (World?.LocalUser?.Root == this)
            {
                World.LocalUser.Root = null!;
            }
            _isRegistered = false;
        }
    }

    public float3 GetGlobalPosition(UserNode node)
    {
        return node switch
        {
            UserNode.None => float3.Zero,
            UserNode.Root => Slot.GlobalPosition,
            UserNode.Head => HeadSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.Body => BodySlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.LeftHand => LeftHandSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.RightHand => RightHandSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.LeftFoot => LeftFootSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.RightFoot => RightFootSlot?.GlobalPosition ?? Slot.GlobalPosition,
            _ => throw new ArgumentException($"Invalid UserNode: {node}")
        };
    }

    public floatQ GetGlobalRotation(UserNode node)
    {
        return node switch
        {
            UserNode.None => floatQ.Identity,
            UserNode.Root => Slot.GlobalRotation,
            UserNode.Head => HeadSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.Body => BodySlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.LeftHand => LeftHandSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.RightHand => RightHandSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.LeftFoot => LeftFootSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.RightFoot => RightFootSlot?.GlobalRotation ?? floatQ.Identity,
            _ => throw new ArgumentException($"Invalid UserNode: {node}")
        };
    }

    // flattened for locomotion
    public float3 HeadFacingDirection
    {
        get
        {
            floatQ headRot = HeadSlot?.GlobalRotation ?? Slot.GlobalRotation;
            // Godot uses -Z as forward; align locomotion basis accordingly
            float3 forward = headRot * float3.Backward;
            forward.y = 0;
            return forward.LengthSquared < 1e-6f ? float3.Backward : forward.Normalized;
        }
    }

    public floatQ HeadFacingRotation
    {
        get
        {
            if (HeadSlot != null)
                return HeadSlot.GlobalRotation;
            return Slot.GlobalRotation;
        }
    }

    public void SetGlobalPosition(UserNode node, float3 position)
    {
        switch (node)
        {
            case UserNode.Root:
                Slot.GlobalPosition = position;
                break;
            case UserNode.Head:
                HeadPosition = position;
                break;
            case UserNode.Body:
                if (BodySlot != null)
                    BodySlot.GlobalPosition = position;
                break;
            case UserNode.LeftHand:
                if (LeftHandSlot != null)
                    LeftHandSlot.GlobalPosition = position;
                break;
            case UserNode.RightHand:
                if (RightHandSlot != null)
                    RightHandSlot.GlobalPosition = position;
                break;
            case UserNode.LeftFoot:
                if (LeftFootSlot != null)
                    LeftFootSlot.GlobalPosition = position;
                break;
            case UserNode.RightFoot:
                if (RightFootSlot != null)
                    RightFootSlot.GlobalPosition = position;
                break;
        }
    }

    // Rotates the ROOT, not the node's own slot: every body node except Root is written each frame by
    // whatever drives it (tracking positioner, IK), so a rotation stamped on the node itself survives
    // exactly until the next update. Turning the root is the only write that sticks, and pinning the
    // node's position across the turn is what stops the rig from swinging out from under the user -
    // the same pivot trick RotateAroundHead uses, generalized to any node. - xlinka
    public void SetGlobalRotation(UserNode node, floatQ rotation)
    {
        if (Slot == null || node == UserNode.None)
            return;

        var current = GetGlobalRotation(node);
        var delta = (rotation * current.Inverse).Normalized;

        var pivot = GetGlobalPosition(node);
        Slot.GlobalRotation = (delta * Slot.GlobalRotation).Normalized;
        var moved = pivot - GetGlobalPosition(node);
        if (moved.LengthSquared > 0f)
            Slot.GlobalPosition += moved;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Validate scale to prevent invalid transforms. These corrective writes run on EVERY peer (incl. an
        // observer holding a REMOTE user's root), and use the THROWING .Value/Scale setters - so a foreign-write
        // denial would throw per frame. It's engine sanitization of a body transform, not a user edit, so bypass
        // the gate. Only entered when a correction is actually needed (rare). -xlinka
        var scale = Slot.Scale;
        if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
            float.IsNaN(scale.x) || float.IsNaN(scale.y) || float.IsNaN(scale.z) ||
            float.IsInfinity(scale.x) || float.IsInfinity(scale.y) || float.IsInfinity(scale.z))
        {
            LumoraLogger.Warn($"UserRoot: Invalid scale detected ({scale}), resetting to (1,1,1)");
            using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
            Slot.LocalScale.Value = float3.One;
        }

        if (System.Math.Abs(scale.x - scale.y) > 0.0001f || System.Math.Abs(scale.y - scale.z) > 0.0001f)
        {
            var avgScale = (scale.x + scale.y + scale.z) / 3f;
            using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
            Slot.Scale = float3.One * avgScale;
        }
    }

    public override void OnDestroy()
    {
        LumoraLogger.Log($"UserRoot: Destroying UserRoot for user '{ActiveUser?.UserName.Value ?? "Unknown"}'");

        Slot?.UnregisterUserRootHierarchy(this);

        // During world disposal users are disposed before slots/components.
        // Their Root setter is no longer valid, and the user object is going away anyway.
        if (World?.IsDisposed != true && _isRegistered && World?.LocalUser?.Root == this)
        {
            World.LocalUser.Root = null!;
        }
        _isRegistered = false;

        _cachedHeadSlot = null!;
        _cachedBodySlot = null!;
        _cachedLeftHandSlot = null!;
        _cachedRightHandSlot = null!;
        _cachedLeftFootSlot = null!;
        _cachedRightFootSlot = null!;

        base.OnDestroy();
    }
}
