// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Receives pose data from an AvatarSocket and drives a slot's transform.
/// This is the component that bridges tracking data to avatar bones.
/// </summary>
// Equip only assigns the synced refs (_objectSlot/_source). Drive links are
// derived from those refs in RefreshDriveLinks on EVERY peer, and the drives
// write local-only values: each peer computes bone poses itself from the
// body-node transforms (which already replicate via tracking streams), so the
// results never generate sync traffic. Writing the fields directly here would
// double-send every bone every frame and fight the remote peer's own
// computation. - xlinka
[ComponentCategory("Users/Avatar")]
[DefaultUpdateOrder(-7500)]
public class AvatarPoseDriver : UserRootComponent, IAvatarEquippable, IInputUpdateReceiver
{
    /// <summary>
    /// The body node this pose node corresponds to.
    /// </summary>
    public readonly Sync<BodyNode> Node = new();

    /// <summary>
    /// Priority for equipping order (higher = later).
    /// </summary>
    [OldName("EquipOrderPriority_")]
    public readonly Sync<int> EquipPriority_ = new();

    /// <summary>
    /// Whether to run the update after input update instead of before.
    /// </summary>
    public readonly Sync<bool> RunAfterInputUpdate = new();

    /// <summary>
    /// Body nodes that cannot be equipped simultaneously.
    /// </summary>
    public SyncFieldList<BodyNode> ConflictingNodes_ { get; private set; } = null!;

    /// <summary>
    /// Output: Whether tracking is currently active.
    /// </summary>
    public readonly Sync<bool> IsTracking = new();

    /// <summary>
    /// Output: Whether the source slot is tracking.
    /// </summary>
    public readonly Sync<bool> SourceIsTracking = new();

    /// <summary>
    /// Output: Whether the source slot is active.
    /// </summary>
    public readonly Sync<bool> SourceIsActive = new();

    // Replicated equip state. Drive links below are derived from these.
    protected readonly SyncRef<AvatarSocket> _objectSlot = null!;
    protected readonly SyncRef<Slot> _source = null!;

    // Local drive links, established per-peer from the synced refs.
    protected FieldDrive<float3> _position = null!;
    protected FieldDrive<floatQ> _rotation = null!;
    protected FieldDrive<float3> _scale = null!;
    protected FieldDrive<bool> _active = null!;

    private bool _isRegistered;

    // IAvatarEquippable implementation
    BodyNode IAvatarEquippable.Node => Node.Value;
    public bool IsEquipped => CurrentSocket != null;
    public int EquipPriority => EquipPriority_.Value;
    public AvatarSocket CurrentSocket => (_objectSlot?.Target) ?? null!;
    public IEnumerable<BodyNode> ConflictingNodes => ConflictingNodes_;
    public User AllowedEquipUser { get; private set; } = null!;

    /// <summary>
    /// Whether equipped and the source is active.
    /// </summary>
    public bool IsEquippedAndActive => IsEquipped && SourceIsActive.Value;

    /// <summary>
    /// Whether tracking and the source is active.
    /// </summary>
    public bool IsTrackingAndActive => IsTracking.Value && SourceIsActive.Value;

    /// <summary>
    /// Whether this node can be equipped (slot position/rotation not already driven).
    /// </summary>
    public bool CanEquip => !Slot.LocalPosition.IsDriven && !Slot.LocalRotation.IsDriven;

    public override void OnAwake()
    {
        base.OnAwake();

        ConflictingNodes_ = new SyncFieldList<BodyNode>();

        _position = new FieldDrive<float3>(World) { LocalValueOnly = true };
        _rotation = new FieldDrive<floatQ>(World) { LocalValueOnly = true };
        _scale = new FieldDrive<float3>(World) { LocalValueOnly = true };
        _active = new FieldDrive<bool>(World) { LocalValueOnly = true };
    }

    public override void OnInit()
    {
        base.OnInit();
        // BodyNode.NONE may not be enum value 0 - set explicitly
        Node.Value = BodyNode.NONE;
    }

    public override void OnStart()
    {
        base.OnStart();

        var input = Engine.Current?.InputInterface;
        if (input != null)
        {
            input.RegisterInputEventReceiver(this);
            _isRegistered = true;
        }

        EnsureCorrectUpdateOrder();
        RefreshDriveLinks();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // _source replicates; every peer derives its local drive links from it
        // so remote bones are driven (and excluded from inbound field writes)
        // exactly like local ones.
        RefreshDriveLinks();
    }

    public override void OnDestroy()
    {
        if (_isRegistered)
        {
            var input = Engine.Current?.InputInterface;
            input?.UnregisterInputEventReceiver(this);
            _isRegistered = false;
        }

        ReleaseDriveLinks();
        AllowedEquipUser = null!;
        base.OnDestroy();
    }

    /// <summary>
    /// Ensure update order is correct relative to parent AvatarPoseDrivers.
    /// </summary>
    private void EnsureCorrectUpdateOrder(bool updateChildren = true)
    {
        var parent = Slot.Parent;
        while (parent != null)
        {
            var parentNode = parent.GetComponent<AvatarPoseDriver>();
            if (parentNode != null && parentNode != this)
            {
                UpdateOrder = parentNode.UpdateOrder + 10;
                break;
            }
            parent = parent.Parent;
        }

        if (updateChildren)
        {
            foreach (var child in Slot.Children)
            {
                var childNode = child.GetComponent<AvatarPoseDriver>();
                childNode?.EnsureCorrectUpdateOrder(updateChildren: false);
            }
        }
    }

    /// <summary>
    /// Equip this pose node to an AvatarSocket. Only assigns the synced
    /// refs; drive links follow on every peer via OnChanges.
    /// </summary>
    public void Equip(AvatarSocket slot)
    {
        _objectSlot.Target = slot;
        _source.Target = slot.Slot;
        RefreshDriveLinks();
    }

    /// <summary>
    /// Dequip this pose node from its current slot.
    /// </summary>
    public void Dequip()
    {
        _objectSlot.Target = null!;
        _source.Target = null!;
        RefreshDriveLinks();
    }

    private void RefreshDriveLinks()
    {
        if (Slot == null || IsDestroyed)
            return;

        if (_source?.Target != null && _objectSlot?.Target != null)
        {
            if (!_position.IsLinkValid)
                _position.DriveTarget(Slot.LocalPosition);
            if (!_rotation.IsLinkValid)
                _rotation.DriveTarget(Slot.LocalRotation);

            var objSlot = _objectSlot.Target;
            if (objSlot.DriveScale.Value)
            {
                if (!_scale.IsLinkValid)
                    _scale.DriveTarget(Slot.LocalScale);
            }
            else if (_scale.IsLinkValid)
            {
                _scale.ReleaseLink();
            }

            if (objSlot.DriveActive.Value)
            {
                if (!_active.IsLinkValid)
                    _active.DriveTarget(Slot.ActiveSelf);
            }
            else if (_active.IsLinkValid)
            {
                _active.ReleaseLink();
            }
        }
        else
        {
            ReleaseDriveLinks();
        }
    }

    private void ReleaseDriveLinks()
    {
        _position?.ReleaseLink();
        _rotation?.ReleaseLink();
        _scale?.ReleaseLink();
        _active?.ReleaseLink();
    }

    /// <summary>
    /// Explicitly allow a user to equip this node.
    /// </summary>
    public void AllowEquip(User user)
    {
        if (AllowedEquipUser != null && user != AllowedEquipUser)
        {
            throw new InvalidOperationException("Another user has already been assigned!");
        }
        AllowedEquipUser = user;
    }

    /// <summary>
    /// Run the pose update - called by BeforeInputUpdate or AfterInputUpdate.
    /// Runs on every peer; results are written through local-only drives.
    /// </summary>
    private void RunUpdate()
    {
        var equippingSlot = CurrentSocket;
        if (_source?.Target != null && equippingSlot != null && !equippingSlot.IsDestroyed)
        {
            if (!_position.IsLinkValid || !_rotation.IsLinkValid)
                RefreshDriveLinks();

            var space = equippingSlot.GetFilteredPose(out var position, out var rotation, out var isTracking);

            SetOutput(IsTracking, isTracking);
            SetOutput(SourceIsTracking, equippingSlot.IsTracking.Value);
            SetOutput(SourceIsActive, equippingSlot.IsActive.Value);

            // Pose is in user-root-local space. Full space conversion (not
            // position + rotation composition) so user scale is respected.
            var parent = Slot.Parent;
            if (parent != null)
            {
                _position.SetValue(parent.GlobalPointToLocal(space.LocalPointToGlobal(position)));
                _rotation.SetValue(parent.GlobalRotationToLocal(space.LocalRotationToGlobal(rotation)));
            }
            else
            {
                _position.SetValue(position);
                _rotation.SetValue(rotation);
            }

            if (_scale.IsLinkValid)
            {
                _scale.SetValue(equippingSlot.Slot.LocalScale.Value);
            }

            if (_active.IsLinkValid)
            {
                _active.SetValue(equippingSlot.Slot.ActiveSelf.Value);
            }
        }
        else
        {
            SetOutput(IsTracking, false);
            SetOutput(SourceIsTracking, false);
            ReleaseDriveLinks();
        }
    }

    // Sync<bool> writes invalidate sync data unconditionally; these outputs
    // flip rarely, so only write on actual change.
    private static void SetOutput(Sync<bool> field, bool value)
    {
        if (field.Value != value)
            field.Value = value;
    }

    /// <summary>
    /// Called before input update.
    /// </summary>
    public void BeforeInputUpdate()
    {
        if (!RunAfterInputUpdate.Value)
        {
            RunUpdate();
        }
    }

    /// <summary>
    /// Called after input update.
    /// </summary>
    public void AfterInputUpdate()
    {
        if (RunAfterInputUpdate.Value)
        {
            RunUpdate();
        }
    }
}
