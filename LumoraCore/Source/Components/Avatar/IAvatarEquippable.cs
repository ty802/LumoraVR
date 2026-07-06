// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Interface for avatar objects that can be equipped to body node slots.
/// Extends IWorldElement so equip state can replicate through SyncRef.
/// </summary>
public interface IAvatarEquippable : IWorldElement
{
    /// <summary>
    /// The body node this avatar object corresponds to.
    /// </summary>
    BodyNode Node { get; }

    /// <summary>
    /// Whether this avatar object is currently equipped.
    /// </summary>
    bool IsEquipped { get; }

    /// <summary>
    /// Priority for equipping order (higher = later).
    /// </summary>
    int EquipPriority { get; }

    /// <summary>
    /// The AvatarSocket this object is equipped to.
    /// </summary>
    AvatarSocket CurrentSocket { get; }

    /// <summary>
    /// Body nodes that cannot be equipped simultaneously with this object.
    /// </summary>
    IEnumerable<BodyNode> ConflictingNodes { get; }

    /// <summary>
    /// User explicitly allowed to equip this object.
    /// </summary>
    User AllowedEquipUser { get; }

    /// <summary>
    /// Equip this avatar object to the given slot.
    /// </summary>
    void Equip(AvatarSocket slot);

    /// <summary>
    /// Dequip this avatar object from its current slot.
    /// </summary>
    void Dequip();

    /// <summary>
    /// Explicitly allow a user to equip this object.
    /// </summary>
    void AllowEquip(User user);
}

/// <summary>
/// Interface for components on avatar objects that receive equip/dequip events.
/// </summary>
public interface IAvatarEquipReceiver
{
    /// <summary>
    /// Called before equipping to the slot.
    /// </summary>
    void OnPreEquip(AvatarSocket slot);

    /// <summary>
    /// Called when equipped to the slot.
    /// </summary>
    void OnEquip(AvatarSocket slot);

    /// <summary>
    /// Called when dequipped from the slot.
    /// </summary>
    void OnDequip(AvatarSocket slot);
}

/// <summary>
/// Interface for pose filters that can modify tracking data.
/// </summary>
public interface IPoseFilter
{
    /// <summary>
    /// Process and potentially modify the pose data.
    /// </summary>
    void ProcessPose(AvatarSocket slot, Slot space, ref Math.float3 position, ref Math.floatQ rotation, ref bool isTracking);
}
