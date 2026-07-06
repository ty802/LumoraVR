// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Tracks which users are inside the colliders on this slot (trigger volumes, capture zones, doors).
/// Each peer detects and replicates ONLY its own membership: the local user's root point is tested
/// against this slot's collider bounds a few times a second, and the synced list carries everyone's
/// self-reported state. The host prunes entries for users who leave. -xlinka
/// </summary>
[ComponentCategory("Physics/Utility")]
public class ColliderUserTracker : Component
{
    /// <summary>Users currently inside. Each user's own peer manages its entry.</summary>
    public readonly SyncRefList<User> UsersInside;

    public bool IsLocalUserInside { get; private set; }
    public bool IsAnyUserInside => UsersInside.Count > 0;
    public int NumberOfUsersInside => UsersInside.Count;

    private const int CheckInterval = 5;   // updates between containment tests
    private int _updateCounter;

    public ColliderUserTracker()
    {
        UsersInside = new SyncRefList<User>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        World?.RegisterEventReceiver(this);
    }

    public override void OnDestroy()
    {
        World?.UnregisterEventReceiver(this);
        base.OnDestroy();
    }

    public new bool HasEventHandler(World.WorldEvent eventType)
        => eventType == World.WorldEvent.OnUserLeft;

    public override void OnUserLeft(User user)
    {
        // The leaving peer can't remove its own entry anymore.
        if (World.IsAuthority && user != null)
            UsersInside.Remove(user);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (++_updateCounter < CheckInterval)
            return;
        _updateCounter = 0;

        var localUser = World?.LocalUser;
        if (localUser == null)
            return;

        bool inside = TestLocalUserInside(localUser);
        if (inside == IsLocalUserInside)
            return;

        IsLocalUserInside = inside;
        if (inside)
        {
            if (!UsersInside.Contains(localUser))
                UsersInside.Add(localUser);
        }
        else
        {
            UsersInside.Remove(localUser);
        }
    }

    // Point-in-bounds against each enabled collider on this slot, in that collider's local space.
    // Approximation of a full capsule-vs-shape contact: the test point is the user root lifted a
    // little so standing exactly on a floor-level volume still counts.
    private bool TestLocalUserInside(User localUser)
    {
        var rootSlot = localUser.Root?.Slot;
        if (rootSlot == null)
            return false;

        float3 point = rootSlot.GlobalPosition + new float3(0f, 0.1f, 0f);

        foreach (var collider in Slot.GetComponents<Collider>())
        {
            if (collider == null || !collider.Enabled)
                continue;
            float3 local = collider.Slot.GlobalPointToLocal(point);
            var bounds = collider.GetLocalBounds();
            if (local.x >= bounds.Min.x && local.x <= bounds.Max.x &&
                local.y >= bounds.Min.y && local.y <= bounds.Max.y &&
                local.z >= bounds.Min.z && local.z <= bounds.Max.z)
                return true;
        }
        return false;
    }
}
