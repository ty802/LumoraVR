// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Avatar root tag + IAvatarEquippable implementation. Lives on the root slot of
// an avatar hierarchy. When the user equips this avatar through AvatarEquipManager,
// the root reparents under the user's AvatarSocket tagged with
// BodyNode.Root (always present on AvatarEquipManager). MaxValue priority means
// the root is processed last after all per-body-node IAvatarEquippables on the
// tree are already dispatched. - xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarForm : Component, IAvatarEquippable
{
    public readonly SyncRef<UserRoot> Owner = new();
    public readonly Sync<bool> IsActive = new();

    public readonly Sync<float3> Scale = new();

    public BodyNode Node => BodyNode.Root;
    public int EquipPriority => int.MaxValue;
    public bool IsEquipped => CurrentSocket != null;

    public AvatarSocket CurrentSocket
    {
        get
        {
            // When equipped via the new dispatch, our slot is reparented under
            // the AvatarSocket.Slot. Walk parents looking for one.
            var current = Slot?.Parent;
            while (current != null)
            {
                var objSlot = current.GetComponent<AvatarSocket>();
                if (objSlot != null && objSlot.Node.Value == BodyNode.Root)
                    return objSlot;
                current = current.Parent;
            }
            return null!;
        }
    }

    public IEnumerable<BodyNode> ConflictingNodes
    {
        get { yield break; }
    }

    public User AllowedEquipUser { get; private set; } = null!;

    public override void OnInit()
    {
        base.OnInit();
        IsActive.Value = true;
        Scale.Value = float3.One;
    }

    public void Equip(AvatarSocket slot)
    {
        if (slot == null || slot.Slot == null) return;

        Slot.SetParent(slot.Slot);
        Slot.LocalPosition.Value = float3.Zero;
        Slot.LocalRotation.Value = floatQ.Identity;
        // Only restore a CALIBRATED scale. Scale defaults to One and is only set once AvatarIK.MaybeRescaleAvatar
        // has fit the avatar to the user. On the FIRST equip it's still One - writing that here would discard the
        // import-compensation scale (which cancels the FBX armature's ~54x) and the avatar renders massive. Keep
        // the existing LocalScale until the rescale provides a real value. -xlinka
        var sc = Scale.Value;
        bool calibrated = System.MathF.Abs(sc.x - 1f) > 1e-5f
                       || System.MathF.Abs(sc.y - 1f) > 1e-5f
                       || System.MathF.Abs(sc.z - 1f) > 1e-5f;
        if (calibrated)
            Slot.LocalScale.Value = sc;

        Owner.Target = slot.Slot.ActiveUserRoot;
        IsActive.Value = true;
    }

    public void Dequip()
    {
        Owner.Target = null!;
        IsActive.Value = false;
    }

    public void AllowEquip(User user)
    {
        if (AllowedEquipUser != null && user != AllowedEquipUser)
            throw new System.InvalidOperationException("Another user has already been assigned!");
        AllowedEquipUser = user;
    }
}
