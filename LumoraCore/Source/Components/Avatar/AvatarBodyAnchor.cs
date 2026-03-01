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
[ComponentCategory("Users/Common Avatar System")]
public class AvatarBodyAnchor : Component, IAvatarObject, IAvatarObjectComponent
{
    /// <summary>Which body node this slot represents in the avatar.</summary>
    public Sync<BodyNode> Node { get; private set; }

    /// <summary>If true, destroys this slot when dequipped from an AvatarObjectSlot.</summary>
    public Sync<bool> DestroyOnDequip { get; private set; }

    // IAvatarObject state
    private AvatarObjectSlot _equippingSlot;

    // ===== IAvatarObject =====

    BodyNode IAvatarObject.Node => Node.Value;
    public bool IsEquipped => _equippingSlot != null;
    public int EquipOrderPriority => 0;
    public AvatarObjectSlot EquippingSlot => _equippingSlot;
    public IEnumerable<BodyNode> MutuallyExclusiveNodes => System.Array.Empty<BodyNode>();
    public User ExplicitlyAllowedUser { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Node = new Sync<BodyNode>(this, BodyNode.NONE);
        DestroyOnDequip = new Sync<bool>(this, false);
    }

    public void Equip(AvatarObjectSlot slot)
    {
        _equippingSlot = slot;
        LumoraLogger.Log($"AvatarBodyAnchor: Equipped {Node.Value} to slot on '{slot.Slot.SlotName.Value}'");
    }

    public void Dequip()
    {
        _equippingSlot = null;
        LumoraLogger.Log($"AvatarBodyAnchor: Dequipped {Node.Value}");
        if (DestroyOnDequip.Value)
            Slot.Destroy();
    }

    public void ExplicitlyAllowEquip(User user)
    {
        ExplicitlyAllowedUser = user;
    }

    // ===== IAvatarObjectComponent =====

    public void OnPreEquip(AvatarObjectSlot slot) { }

    public void OnEquip(AvatarObjectSlot slot) { }

    public void OnDequip(AvatarObjectSlot slot) { }
}
