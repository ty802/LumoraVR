// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

// An IWorldElement so equip state can replicate through a SyncRef.
public interface IAvatarEquippable : IWorldElement
{
    BodyNode Node { get; }

    bool IsEquipped { get; }

    // Higher equips later.
    int EquipPriority { get; }

    AvatarSocket CurrentSocket { get; }

    IEnumerable<BodyNode> ConflictingNodes { get; }

    User AllowedEquipUser { get; }

    void Equip(AvatarSocket slot);

    void Dequip();

    void AllowEquip(User user);
}

public interface IAvatarEquipReceiver
{
    void OnPreEquip(AvatarSocket slot);

    void OnEquip(AvatarSocket slot);

    void OnDequip(AvatarSocket slot);
}

public interface IPoseFilter : IWorldElement
{
    void ProcessPose(AvatarSocket slot, Slot space, ref Math.float3 position, ref Math.floatQ rotation, ref bool isTracking);
}
