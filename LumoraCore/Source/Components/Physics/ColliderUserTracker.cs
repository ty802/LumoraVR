// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Tracks which users are inside the colliders on this slot (trigger volumes, capture zones, doors).
// Each peer detects and replicates ONLY its own membership: the local user's root point is tested
// against this slot's collider bounds a few times a second, and the synced list carries everyone's
// self-reported state. The host prunes entries for users who leave. -xlinka
[ComponentCategory("Physics")]
public class ColliderUserTracker : Component
{
    // Each user's own peer manages its entry.
    public readonly SyncRefList<User> UsersInside;

    public bool IsLocalUserInside { get; private set; }
    public bool IsAnyUserInside => UsersInside.Count > 0;
    public int NumberOfUsersInside => UsersInside.Count;

    public ColliderUserTracker()
    {
        UsersInside = new SyncRefList<User>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        World?.RegisterEventReceiver(this);
    }

    // Looked at by the world's round, every fifth update, instead of counting frames in its own update.
    public override void OnStart()
    {
        base.OnStart();
        if (World != null)
            ColliderUserRound.For(World).Add(this);
    }

    public override void OnDestroy()
    {
        World?.UnregisterEventReceiver(this);
        if (World != null)
            ColliderUserRound.For(World).Remove(this);
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

    // What its update did on every fifth call: switched off or out of the tree, it is not looked at.
    internal void Look()
    {
        if (IsDestroyed || !Enabled.Value || Slot is not { IsActive: true })
            return;

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

        var components = Slot.Components;
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is not Collider collider || !collider.Enabled)
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
