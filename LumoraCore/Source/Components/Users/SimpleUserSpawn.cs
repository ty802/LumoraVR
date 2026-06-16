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
        if (!World.IsAuthority)
        {
            LumoraLogger.Log($"SimpleUserSpawn: Client ignoring OnUserJoined for '{user.UserName.Value}' - authority spawns and syncs");
            return;
        }
        if (_userSlots.ContainsKey(user))
        {
            LumoraLogger.Warn($"SimpleUserSpawn: User '{user.UserName.Value}' already spawned, ignoring duplicate");
            return;
        }

        LumoraLogger.Log($"SimpleUserSpawn: [Authority] Spawning user '{user.UserName.Value ?? "(null)"}', RefID={user.ReferenceID}");

        try
        {
            var userName = user.UserName.Value;
            if (string.IsNullOrEmpty(userName))
                userName = $"User_{user.ReferenceID}";

            var userSlot = World.RootSlot.AddSlot($"User {userName}");
            userSlot.Persistent.Value = false;
            userSlot.LocalPosition.Value = Slot.LocalPosition.Value;
            userSlot.LocalRotation.Value = Slot.LocalRotation.Value;

            _userSlots.Add(user, userSlot);

            var userRoot = userSlot.AttachComponent<UserRoot>();
            userRoot.Initialize(user);

            GetBuilder().BuildAvatar(userRoot);

            LumoraLogger.Log($"SimpleUserSpawn: [Authority] Spawned '{userName}' - sync to clients");
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SimpleUserSpawn: Failed to spawn '{user.UserName.Value}': {ex.Message}\n{ex.StackTrace}");
        }
    }

    public override void OnUserLeft(User user)
    {
        if (user == null) return;
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
