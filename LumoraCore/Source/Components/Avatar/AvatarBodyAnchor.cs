// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Marks a slot in an avatar hierarchy as a specific BodyNode position.
/// Other objects (accessories, hats, tools, held items) can equip themselves to an
/// AvatarBodyAnchor to attach to the correct body part even after the avatar is swapped.
/// </summary>
[ComponentCategory("Users/Avatar")]
public class AvatarBodyAnchor : Component, IAvatarEquippable, IAvatarEquipReceiver
{
    /// <summary>Which body node this slot represents in the avatar.</summary>
    public readonly Sync<BodyNode> Node = new();

    /// <summary>If true, destroys this slot when dequipped from an AvatarSocket.</summary>
    public readonly Sync<bool> DestroyOnDequip = new();

    // IAvatarEquippable state
    private AvatarSocket _equippingSlot = null!;

    // IAvatarEquippable

    BodyNode IAvatarEquippable.Node => Node.Value;
    public bool IsEquipped => _equippingSlot != null;
    public int EquipPriority => 0;
    public AvatarSocket CurrentSocket => _equippingSlot;
    public IEnumerable<BodyNode> ConflictingNodes => System.Array.Empty<BodyNode>();
    public User AllowedEquipUser { get; private set; } = null!;

    public override void OnInit()
    {
        base.OnInit();
        // BodyNode.NONE may not be enum value 0 - set explicitly
        Node.Value = BodyNode.NONE;
        // DestroyOnDequip = false (C# default, skip)
    }

    public void Equip(AvatarSocket slot)
    {
        _equippingSlot = slot;
        LumoraLogger.Log($"AvatarBodyAnchor: Equipped {Node.Value} to slot on '{slot.Slot.SlotName.Value}'");
    }

    public void Dequip()
    {
        _equippingSlot = null!;
        LumoraLogger.Log($"AvatarBodyAnchor: Dequipped {Node.Value}");
        if (DestroyOnDequip.Value)
            Slot.Destroy();
    }

    public void AllowEquip(User user)
    {
        AllowedEquipUser = user;
    }

    // IAvatarEquipReceiver

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot) { }

    public void OnDequip(AvatarSocket slot) { }
}

