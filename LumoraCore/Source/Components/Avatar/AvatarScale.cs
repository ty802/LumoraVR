// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// The scale an avatar is meant to be worn at. Put on to the wearer when it is equipped, unless told not to,
// and while worn it is the scale they go back to when their scale is reset. -xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarScale : Component, IAvatarEquipReceiver
{
    public readonly Sync<float> Scale;
    public readonly Sync<bool> ApplyOnEquip;

    // Socket state replicates; equip callbacks only run on the machine doing the equip.
    // Derive this so late joiners and scripts also read the worn avatar's default scale.
    public bool IsWorn => Slot?.GetComponentInParents<AvatarForm>()?.Wearer != null;

    public AvatarScale()
    {
        Scale = new Sync<float>(this, 1f);
        ApplyOnEquip = new Sync<bool>(this, true);
    }

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot)
    {
        if (IsWorn && Enabled.Value && Slot.IsActive && ApplyOnEquip.Value
            && float.IsFinite(Scale.Value) && Scale.Value > 0f
            && slot?.Slot?.ActiveUserRoot is { IsDestroyed: false } root)
            root.ScaleTo(Scale.Value, 0f);
    }

    public void OnDequip(AvatarSocket slot) { }

    // The scale the user under this root is meant to return to: the worn avatar's, or 1.
    public static float DefaultFor(UserRoot? root)
    {
        if (root?.Slot is not { IsDestroyed: false } slot)
            return 1f;
        foreach (var scale in slot.GetComponentsInChildren<AvatarScale>())
        {
            if (scale.IsWorn && scale.Enabled.Value && scale.Slot.IsActive
                && float.IsFinite(scale.Scale.Value) && scale.Scale.Value > 0f)
                return scale.Scale.Value;
        }
        return 1f;
    }
}
