// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Container component that marks a slot as belonging to a user.
/// Provides access to avatar body nodes and manages user transforms.
/// </summary>
[ComponentCategory("Users")]
public class UserRoot : Component
{
    /// <summary>
    /// User node types for positioning and targeting.
    /// </summary>
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

    // ===== USER REFERENCE =====
    /// <summary>
    /// Synced reference to the user that owns this UserRoot.
    /// This syncs over the network so clients can identify their own UserRoot.
    /// </summary>
    public readonly SyncRef<User> TargetUser = null!;

    private bool _isRegistered = false;

    /// <summary>
    /// The User that owns this UserRoot.
    /// </summary>
    public User ActiveUser => (TargetUser?.Target) ?? null!;

    /// <summary>
    /// Check if this UserRoot belongs to the local user.
    /// Uses direct object reference comparison.
    /// </summary>
    public bool IsLocalUserRoot => TargetUser?.Target != null && TargetUser.Target == World?.LocalUser;

    // ===== CACHED BODY NODES =====
    private Slot _cachedHeadSlot = null!;
    private Slot _cachedBodySlot = null!;
    private Slot _cachedLeftHandSlot = null!;
    private Slot _cachedRightHandSlot = null!;
    private Slot _cachedLeftFootSlot = null!;
    private Slot _cachedRightFootSlot = null!;

    // ===== BODY NODE ACCESSORS =====
    //
    // Resolved from the typed UserRootComponent registry rather than string-
    // named child lookups. Any TrackedDevicePositioner under the user that
    // declares AutoBodyNode == HeadNode shows up here automatically â€” slots
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
        if (cache != null && !cache.IsDestroyed)
            return cache;
        if (IsDestroyed)
            return null!;

        var positioner = GetRegisteredComponent<TrackedDevicePositioner>(p => p.AutoBodyNode.Value == node);
        var resolved = positioner?.BodyNodeRoot?.Target ?? positioner?.Slot;
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

    // ===== POSITION ACCESSORS =====

    /// <summary>
    /// Head position in world space.
    /// </summary>
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

    /// <summary>
    /// Head rotation in world space.
    /// </summary>
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

    /// <summary>
    /// Feet position in world space (center between feet).
    /// </summary>
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

    /// <summary>
    /// Global scale of this UserRoot.
    /// </summary>
    public float GlobalScale
    {
        get => Slot.Scale.x;
        set
        {
            Slot.Scale = float3.One * value;
        }
    }

    /// <summary>
    /// Rotate the user root while keeping the current head world position fixed.
    /// This matches VR comfort expectations for snap and smooth turning.
    /// </summary>
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

    /// <summary>
    /// Rotate around world up while preserving the head world position.
    /// </summary>
    public void RotateYawAroundHead(float yawRadians)
    {
        if (System.Math.Abs(yawRadians) < 0.000001f)
            return;

        RotateAroundHead(floatQ.AxisAngle(float3.Up, yawRadians));
    }

    /// <summary>
    /// Check if we've received first positional tracking data.
    /// Used to know when VR tracking has started.
    /// </summary>
    public bool ReceivedFirstPositionalData
    {
        get
        {
            if (HeadSlot != null)
            {
                var headPos = HeadSlot.LocalPosition.Value;
                var headRot = HeadSlot.LocalRotation.Value;
                // If head has moved from default position, we have tracking
                if (headPos != float3.Zero || headRot != floatQ.Identity)
                    return true;
            }

            // Check if we have a TrackedDevicePositioner that is tracking
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

    // ===== INITIALIZATION =====

    /// <summary>
    /// Initialize this UserRoot with a User.
    /// Called by SimpleUserSpawn after attaching the component.
    /// Sets TargetUser which syncs to clients.
    /// </summary>
    public void Initialize(User user)
    {
        if (user == null)
        {
            LumoraLogger.Error("UserRoot: Cannot initialize with null user");
            return;
        }

        // Set the synced reference - this will sync to clients
        TargetUser.Target = user;

        // Register with user on authority
        if (World?.IsAuthority == true)
        {
            user.Root = this;
            LumoraLogger.Log($"User: Registered UserRoot for authority user '{user.UserName.Value}'");
        }

        LumoraLogger.Log($"UserRoot: Initialized for user '{user.UserName.Value}' (RefID: {user.ReferenceID})");
    }

    /// <summary>
    /// Called when synced fields change. Handles client-side local user detection.
    /// Simple direct object reference comparison.
    /// </summary>
    public override void OnAwake()
    {
        base.OnAwake();
        Slot?.RegisterUserRoot(this);
    }

    // Typed component cache. Every UserRootComponent in the user's slot
    // hierarchy registers here. Lookups like "find the VRIKAvatar attached
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

        // Simple direct reference comparison
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

    /// <summary>
    /// Get the global position of a user node.
    /// </summary>
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

    /// <summary>
    /// Get the global rotation of a user node.
    /// </summary>
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

    /// <summary>
    /// Forward direction the user is facing (from head), flattened for locomotion.
    /// </summary>
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

    /// <summary>
    /// Full head-facing rotation (uses head slot if available).
    /// </summary>
    public floatQ HeadFacingRotation
    {
        get
        {
            if (HeadSlot != null)
                return HeadSlot.GlobalRotation;
            return Slot.GlobalRotation;
        }
    }

    /// <summary>
    /// Set the global position of a user node.
    /// </summary>
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

    /// <summary>
    /// Called every frame to update the UserRoot.
    /// </summary>
    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Validate scale to prevent invalid transforms
        var scale = Slot.Scale;
        if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
            float.IsNaN(scale.x) || float.IsNaN(scale.y) || float.IsNaN(scale.z) ||
            float.IsInfinity(scale.x) || float.IsInfinity(scale.y) || float.IsInfinity(scale.z))
        {
            LumoraLogger.Warn($"UserRoot: Invalid scale detected ({scale}), resetting to (1,1,1)");
            Slot.LocalScale.Value = float3.One;
        }

        // Ensure uniform scale (all axes same)
        if (System.Math.Abs(scale.x - scale.y) > 0.0001f || System.Math.Abs(scale.y - scale.z) > 0.0001f)
        {
            var avgScale = (scale.x + scale.y + scale.z) / 3f;
            Slot.Scale = float3.One * avgScale;
        }
    }

    /// <summary>
    /// Called when the component is destroyed.
    /// </summary>
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

        // Clear cached references
        _cachedHeadSlot = null!;
        _cachedBodySlot = null!;
        _cachedLeftHandSlot = null!;
        _cachedRightHandSlot = null!;
        _cachedLeftFootSlot = null!;
        _cachedRightFootSlot = null!;

        base.OnDestroy();
    }
}

