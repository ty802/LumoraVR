// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// Spawns users at this slot's position when they join the world. Creates the
// user slot + UserRoot, then delegates the full setup to a CommonAvatarBuilder
// (found/created on this slot). Per-world build config lives on the builder.
// - xlinka
[ComponentCategory("Users")]
public class SimpleUserSpawn : Component, IWorldEventReceiver
{
    private readonly Dictionary<User, Slot> _userSlots = new();
    // Local user's own equipment build is deferred until the (host-built, replicated) scaffold has synced in
    // and the User<->UserRoot link is set, then retried from OnUpdate. -xlinka
    private readonly HashSet<User> _pendingOwnedEquip = new();
    private readonly List<User> _retryScratch = new();

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
    {
        return eventType == World.WorldEvent.OnUserJoined ||
               eventType == World.WorldEvent.OnUserLeft;
    }

    public override void OnUserJoined(User user)
    {
        if (user == null) return;

        // The authority builds the shared avatar scaffold (body nodes, collider, locomotion, head output,
        // avatar manager) for EVERY user and replicates it. Clients receive it via sync - they never build it. -xlinka
        if (World.IsAuthority)
            BuildSharedScaffoldFor(user);

        // Each peer builds ITS OWN equipment (hand tool rig + context menu) under its own (replicated) avatar
        // root, minted in its own RefID byte so the owner owns the writes - no system bypass needed. A client
        // never builds a remote user's equipment, that replicates in from its owner. -xlinka
        if (ReferenceEquals(user, World.LocalUser))
            TryBuildOwnedEquipment(user);
    }

    private void BuildSharedScaffoldFor(User user)
    {
        if (_userSlots.TryGetValue(user, out var existing))
        {
            if (existing != null && !existing.IsRemoved)
                return; // already spawned
            _userSlots.Remove(user); // stale, rebuild
        }

        LumoraLogger.Log($"SimpleUserSpawn: [Authority] Building scaffold for '{user.UserName.Value ?? "(null)"}', RefID={user.ReferenceID}");
        try
        {
            var userName = user.UserName.Value;
            if (string.IsNullOrEmpty(userName))
                userName = $"User_{user.ReferenceID}";

            var userSlot = World.RootSlot.AddSlot($"User {userName}");
            userSlot.Persistent.Value = false;
            userSlot.LocalPosition.Value = Slot.LocalPosition.Value;
            userSlot.LocalRotation.Value = Slot.LocalRotation.Value;
            _userSlots[user] = userSlot;

            var userRoot = userSlot.AttachComponent<UserRoot>();
            userRoot.Initialize(user);
            GetBuilder().BuildSharedScaffold(userRoot);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SimpleUserSpawn: Failed to build scaffold for '{user.UserName.Value}': {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Build the local user's own equipment once its (host-built) scaffold has synced in and the
    // User<->UserRoot link is set. Re-queues for an OnUpdate retry if the scaffold/controllers/root aren't
    // ready or a transient join-window denial throws. -xlinka
    private void TryBuildOwnedEquipment(User user)
    {
        var root = user.Root;
        var leftController = root?.Slot?.FindChild("Body Nodes", recursive: false)?.FindChild("LeftController", recursive: false);
        if (root == null || root.Slot == null || leftController == null)
        {
            _pendingOwnedEquip.Add(user); // scaffold/root not ready yet
            return;
        }

        try
        {
            // Pin the allocation context to the local user's OWN byte for the whole build so every slot the
            // equipment rig mints (hand tool, grabber, laser beam, context menu) lands in the user's namespace
            // and reads as owned by the permission gate - both for this local build and on the host when the add
            // replicates. The joiner's ambient context is already its own byte after JoinGrant, but pinning here
            // makes the build robust to any stray allocation scope that didn't restore. Host = authority byte
            // (no-op scope). -xlinka
            using (World.EnterLocalUserAllocation())
            {
                GetBuilder().BuildOwnedEquipment(root);
            }
            _pendingOwnedEquip.Remove(user);
        }
        catch (Exception)
        {
            // Transient ownership lag - the write authorizes once the User<->UserRoot link resolves. Keep
            // retrying silently from OnUpdate. -xlinka
            _pendingOwnedEquip.Add(user);
        }
    }

    public override void OnUpdate(float delta)
    {
        if (_pendingOwnedEquip.Count == 0)
            return;

        _retryScratch.Clear();
        _retryScratch.AddRange(_pendingOwnedEquip);
        foreach (var u in _retryScratch)
        {
            if (u == null || u.IsDestroyed)
            {
                _pendingOwnedEquip.Remove(u!);
                continue;
            }
            if (ReferenceEquals(u, World.LocalUser))
                TryBuildOwnedEquipment(u);
            else
                _pendingOwnedEquip.Remove(u); // no longer ours
        }
    }

    public override void OnUserLeft(User user)
    {
        if (user == null) return;
        _pendingOwnedEquip.Remove(user);
        if (!World.IsAuthority) return;
        if (_userSlots.TryGetValue(user, out var slot))
        {
            slot.Destroy();
            _userSlots.Remove(user);
        }
    }

    public override void OnFocusChanged(World.WorldFocus focus) { }
    public override void OnWorldDestroy() { }

    private IAvatarBuilder GetBuilder()
    {
        return Slot.GetComponent<CommonAvatarBuilder>() ?? Slot.AttachComponent<CommonAvatarBuilder>();
    }
}
