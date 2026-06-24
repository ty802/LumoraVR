// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Avatar root tag + IAvatarObject implementation. Lives on the root slot of
// an avatar hierarchy. When the user equips this avatar through AvatarManager,
// the root reparents under the user's AvatarObjectSlot tagged with
// BodyNode.Root (always present on AvatarManager). MaxValue priority means
// the root is processed last after all per-body-node IAvatarObjects on the
// tree are already dispatched. - xlinka
[ComponentCategory("Users/Common Avatar System")]
public class AvatarRoot : Component, IAvatarObject
{
    public readonly SyncRef<UserRoot> Owner = new();
    public readonly Sync<bool> IsActive = new();

    public readonly Sync<float3> Scale = new();

    public BodyNode Node => BodyNode.Root;
    public int EquipOrderPriority => int.MaxValue;
    public bool IsEquipped => EquippingSlot != null;

    public AvatarObjectSlot EquippingSlot
    {
        get
        {
            // When equipped via the new dispatch, our slot is reparented under
            // the AvatarObjectSlot.Slot. Walk parents looking for one.
            var current = Slot?.Parent;
            while (current != null)
            {
                var objSlot = current.GetComponent<AvatarObjectSlot>();
                if (objSlot != null && objSlot.Node.Value == BodyNode.Root)
                    return objSlot;
                current = current.Parent;
            }
            return null!;
        }
    }

    public IEnumerable<BodyNode> MutuallyExclusiveNodes
    {
        get { yield break; }
    }

    public User ExplicitlyAllowedUser { get; private set; } = null!;

    public override void OnInit()
    {
        base.OnInit();
        IsActive.Value = true;
        Scale.Value = float3.One;
    }

    public void Equip(AvatarObjectSlot slot)
    {
        if (slot == null || slot.Slot == null) return;

        Slot.SetParent(slot.Slot);
        Slot.LocalPosition.Value = float3.Zero;
        Slot.LocalRotation.Value = floatQ.Identity;
        Slot.LocalScale.Value = Scale.Value;

        Owner.Target = slot.Slot.ActiveUserRoot;
        IsActive.Value = true;
    }

    public void Dequip()
    {
        Owner.Target = null!;
        IsActive.Value = false;
    }

    public void ExplicitlyAllowEquip(User user)
    {
        if (ExplicitlyAllowedUser != null && user != ExplicitlyAllowedUser)
            throw new System.InvalidOperationException("Another user has already been assigned!");
        ExplicitlyAllowedUser = user;
    }
}
