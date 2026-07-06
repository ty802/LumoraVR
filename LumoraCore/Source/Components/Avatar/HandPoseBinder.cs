// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// On equip, links the hand model's <see cref="HandPoseDriver"/> to the user's current
/// finger pose source (the per-user <see cref="UserHandPoseInfo"/>). Lives on
/// the hand avatar object and reacts through the standard equip dispatch.
/// </summary>
// The poser also falls back to the user-root registry on its own, so this is
// belt-and-suspenders for the common case, but it is the seam where per-hand
// wiring (and later tip-touch / haptics) belongs. - xlinka
[ComponentCategory("Users/Avatar/Hands")]
public class HandPoseBinder : Component, IAvatarEquipReceiver
{
    /// <summary>The poser this assigner feeds. Resolved from the slot tree when unset.</summary>
    public readonly SyncRef<HandPoseDriver> TargetPoser = null!;

    /// <summary>Which hand this assigner is for.</summary>
    public readonly Sync<Chirality> Side = new();

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot)
    {
        // Only the authority drives the synced PoseSource ref; every other peer
        // receives it through sync, and the poser's own registry fallback covers
        // the gap before replication arrives.
        if (World?.IsAuthority != true)
            return;

        var poser = ResolvePoser();
        if (poser == null)
            return;

        var info = slot?.Slot?.ActiveUserRoot?.GetRegisteredComponent<UserHandPoseInfo>();
        poser.PoseSource.Target = info?.HandPoseSource.Target!;
    }

    public void OnDequip(AvatarSocket slot)
    {
        if (World?.IsAuthority != true)
            return;

        var poser = ResolvePoser();
        if (poser != null)
            poser.PoseSource.Target = null!;
    }

    private HandPoseDriver ResolvePoser()
        => TargetPoser?.Target
           ?? Slot?.GetComponent<HandPoseDriver>()
           ?? Slot?.GetComponentInChildren<HandPoseDriver>()!;
}
