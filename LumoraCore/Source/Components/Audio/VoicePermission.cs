// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Audio;

namespace Lumora.Core.Components.Audio;

// A ceiling on how far a voice may carry. Not a gate component in the data-model sense: the permission
// engine in Warden guards who may WRITE what, and this guards how loud the result is allowed to be,
// which is a world rule rather than an ownership one. So it follows the shape the rest of the
// world-level permission surface already uses - a plain component the world finds and reads - instead
// of inventing a validator interface for one field.
//
// Scope is where it sits. Under a user root it caps that user; anywhere else it caps everybody in the
// world. The lowest cap in scope wins, because a permission that could be widened by adding another
// one of these is not a permission. -xlinka
[ComponentCategory("Audio")]
public class VoicePermission : Component
{
    public readonly Sync<VoiceMode> MaxAllowedVoiceMode;

    // Whether holding this permission lets you change somebody ELSE's mode, which is what a moderator
    // needs to push a shouting guest back down to Normal without silencing them outright.
    public readonly Sync<bool> AllowChangeOtherUsers;

    public VoicePermission()
    {
        MaxAllowedVoiceMode = new Sync<VoiceMode>(this, VoiceMode.Broadcast);
        AllowChangeOtherUsers = new Sync<bool>(this, true);
    }

    // Null means world-wide. Resolved per read rather than cached: the slot can be reparented under a
    // user root at any point and a cached answer would outlive the move.
    public User? ScopedUser => Slot?.ActiveUserRoot?.ActiveUser;

    public bool AppliesTo(User user)
    {
        if (user == null || IsDestroyed || !Enabled.Value || Slot == null || !Slot.IsActive)
            return false;
        var scoped = ScopedUser;
        return scoped == null || ReferenceEquals(scoped, user);
    }

    public override void OnStart()
    {
        base.OnStart();
        World?.Audio?.RegisterVoicePermission(this);
    }

    public override void OnDestroy()
    {
        World?.Audio?.UnregisterVoicePermission(this);
        base.OnDestroy();
    }

    // Whether the local user may retune somebody else's mode in this world. False the moment any
    // permission in scope says so: same "lowest wins" rule as the cap.
    public static bool AllowsChangingOtherUsers(World? world)
    {
        var manager = world?.Audio;
        return manager == null || manager.VoicePermissionsAllowChangingOtherUsers;
    }
}
