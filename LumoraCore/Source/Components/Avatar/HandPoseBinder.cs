// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

// On equip, points the hand's pose graph at the wearer's published finger source (UserHandPoseInfo on
// the user root): into the first slot of the multiplexer when the hand has one, else straight into
// the driver. Written by the machine putting the avatar on or taking it off, which may always write it, since
// an avatar a user may not edit is worn as their own copy. It was the authority alone, and only the machine
// doing the equip hears it, so a guest's hands never got their fingers. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public class HandPoseBinder : Component, IAvatarEquipReceiver
{
    public readonly SyncRef<HandPoseDriver> TargetPoser = null!;

    public readonly SyncRef<HandPoseMultiplexer> TargetMultiplexer = null!;

    public readonly Sync<Chirality> Side = new();

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot)
    {
        if (!MayWriteHere())
            return;
        var info = slot?.Slot?.ActiveUserRoot?.GetRegisteredComponent<UserHandPoseInfo>();
        Bind(info?.HandPoseSource.Target);
    }

    public void OnDequip(AvatarSocket slot)
    {
        if (!MayWriteHere())
            return;
        Bind(null);
    }

    private bool MayWriteHere()
    {
        var world = World;
        if (world == null || Slot == null)
            return false;
        return world.IsAuthority || Slot.IsUnderLocalUser || Slot.IsMintedHere;
    }

    private void Bind(IHandPoseSourceComponent? source)
    {
        var multiplexer = TargetMultiplexer?.Target;
        if (multiplexer != null && !multiplexer.IsDestroyed)
        {
            if (multiplexer.Sources.Count == 0)
                multiplexer.Sources.Add(source);
            else
                multiplexer.Sources[0] = source;
            return;
        }

        var poser = TargetPoser?.Target
                    ?? Slot?.GetComponent<HandPoseDriver>()
                    ?? Slot?.GetComponentInChildren<HandPoseDriver>();
        if (poser != null)
            poser.PoseSource.Target = source!;
    }
}
