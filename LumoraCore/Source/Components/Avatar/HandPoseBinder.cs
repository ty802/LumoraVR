// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

// On equip, points the hand's pose graph at the wearer's published finger source (UserHandPoseInfo on
// the user root): into the first slot of the multiplexer when the hand has one, else straight into
// the driver. Only the authority writes the replicated refs. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public class HandPoseBinder : Component, IAvatarEquipReceiver
{
    public readonly SyncRef<HandPoseDriver> TargetPoser = null!;

    public readonly SyncRef<HandPoseMultiplexer> TargetMultiplexer = null!;

    public readonly Sync<Chirality> Side = new();

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot)
    {
        if (World?.IsAuthority != true)
            return;
        var info = slot?.Slot?.ActiveUserRoot?.GetRegisteredComponent<UserHandPoseInfo>();
        Bind(info?.HandPoseSource.Target);
    }

    public void OnDequip(AvatarSocket slot)
    {
        if (World?.IsAuthority != true)
            return;
        Bind(null);
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
