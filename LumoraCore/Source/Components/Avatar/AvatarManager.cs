// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Manages avatar equipping and swapping for a user.
/// </summary>
[ComponentCategory("Avatar")]
public class AvatarManager : ImplementableComponent
{
    public readonly SyncRef<Slot> CurrentAvatar = new();
    public readonly SyncRef<UserRoot> UserRoot = new();
    public readonly SyncRef<Slot> DefaultAvatarSlot = new();
    public readonly Sync<bool> IsUsingDefaultAvatar = new();

    public SyncRefList<Slot> AvailableAvatars { get; private set; }

    public event Action<Slot> OnAvatarChanged;

    public override void OnAwake()
    {
        base.OnAwake();
        AvailableAvatars = new SyncRefList<Slot>(this);
        Logger.Log($"AvatarManager: Awake on slot '{Slot.SlotName.Value}'");
    }

    public override void OnInit()
    {
        base.OnInit();
        IsUsingDefaultAvatar.Value = true;
    }

    public override void OnStart()
    {
        base.OnStart();
        ResolveUserRoot();
    }

    public bool CanEquipAvatar(Slot avatarSlot)
    {
        return TryResolveFinalizedAvatar(
            avatarSlot,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    /// <summary>
    /// Equip a finalized avatar from a slot.
    /// </summary>
    public bool EquipAvatar(Slot avatarSlot)
    {
        if (!TryResolveFinalizedAvatar(
                avatarSlot,
                out var descriptor,
                out var avatarRoot,
                out var skeleton,
                out var rig,
                out var reason))
        {
            Logger.Warn($"AvatarManager: Cannot equip avatar - {reason}");
            return false;
        }

        ResolveUserRoot();
        if (UserRoot.Target == null)
        {
            Logger.Warn("AvatarManager: Cannot equip avatar without a UserRoot");
            return false;
        }

        Logger.Log($"AvatarManager: Equipping avatar from slot '{avatarSlot.SlotName.Value}'");

        var ikAvatar = EnsureGodotIKAvatar(avatarSlot);
        ikAvatar.Skeleton.Target = skeleton;
        ikAvatar.UserRoot.Target = UserRoot.Target;
        ikAvatar.Enabled.Value = true;

        var vrikAvatar = avatarSlot.GetComponent<VRIKAvatar>() ?? avatarSlot.AttachComponent<VRIKAvatar>();
        vrikAvatar.Descriptor.Target = descriptor;
        if (!vrikAvatar.SetupFromDescriptor(descriptor, UserRoot.Target))
        {
            Logger.Warn("AvatarManager: Avatar runtime setup failed");
            return false;
        }

        DequipCurrentAvatar();

        avatarSlot.SetParent(Slot);
        avatarSlot.LocalPosition.Value = float3.Zero;
        avatarSlot.LocalRotation.Value = floatQ.Identity;
        avatarSlot.ActiveSelf.Value = true;

        avatarRoot.Owner.Target = UserRoot.Target;
        avatarRoot.IsActive.Value = true;

        descriptor.Root.Target = avatarRoot;
        descriptor.Skeleton.Target = skeleton;
        descriptor.Rig.Target = rig;
        descriptor.IsFinalized.Value = true;

        CurrentAvatar.Target = avatarSlot;
        IsUsingDefaultAvatar.Value = false;

        if (!AvailableAvatars.Contains(avatarSlot))
            AvailableAvatars.Add(avatarSlot);

        Logger.Log("AvatarManager: Avatar equipped successfully");
        OnAvatarChanged?.Invoke(avatarSlot);
        return true;
    }

    /// <summary>
    /// Equip the default avatar.
    /// </summary>
    public void EquipDefaultAvatar()
    {
        Logger.Log("AvatarManager: Equipping default avatar");

        DequipCurrentAvatar();

        if (DefaultAvatarSlot.Target != null)
        {
            DefaultAvatarSlot.Target.ActiveSelf.Value = true;
            CurrentAvatar.Target = DefaultAvatarSlot.Target;
        }
        else
        {
            var avatarSlot = Slot.AddSlot("DefaultAvatar");
            DefaultAVI.CreateDefaultAvatar(avatarSlot, UserRoot.Target);
            DefaultAvatarSlot.Target = avatarSlot;
            CurrentAvatar.Target = avatarSlot;
        }

        IsUsingDefaultAvatar.Value = true;
        OnAvatarChanged?.Invoke(CurrentAvatar.Target);
    }

    /// <summary>
    /// Dequip the current avatar and release its tracking slots.
    /// </summary>
    public void DequipCurrentAvatar()
    {
        if (CurrentAvatar.Target == null)
            return;

        Logger.Log($"AvatarManager: Dequipping avatar '{CurrentAvatar.Target.SlotName.Value}'");

        ReleaseAvatarTracking(CurrentAvatar.Target);

        var avatarRoot = CurrentAvatar.Target.GetComponent<AvatarRoot>();
        if (avatarRoot != null)
            avatarRoot.IsActive.Value = false;

        CurrentAvatar.Target.ActiveSelf.Value = false;
        CurrentAvatar.Target = null;
    }

    /// <summary>
    /// Remove an avatar from available avatars and destroy it.
    /// </summary>
    public void RemoveAvatar(Slot avatarSlot)
    {
        if (avatarSlot == null)
            return;

        if (CurrentAvatar.Target == avatarSlot)
            EquipDefaultAvatar();

        if (avatarSlot == DefaultAvatarSlot.Target)
            return;

        AvailableAvatars.Remove(avatarSlot);
        avatarSlot.Destroy();

        Logger.Log("AvatarManager: Removed avatar");
    }

    /// <summary>
    /// Get the index of an avatar in the available list.
    /// </summary>
    public int GetAvatarIndex(Slot avatarSlot)
    {
        for (int i = 0; i < AvailableAvatars.Count; i++)
        {
            if (AvailableAvatars[i] == avatarSlot)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Cycle to the next available avatar.
    /// </summary>
    public void CycleNextAvatar()
    {
        if (AvailableAvatars.Count == 0)
        {
            EquipDefaultAvatar();
            return;
        }

        int currentIndex = GetAvatarIndex(CurrentAvatar.Target);
        int nextIndex = (currentIndex + 1) % AvailableAvatars.Count;
        EquipAvatar(AvailableAvatars[nextIndex]);
    }

    /// <summary>
    /// Cycle to the previous available avatar.
    /// </summary>
    public void CyclePreviousAvatar()
    {
        if (AvailableAvatars.Count == 0)
        {
            EquipDefaultAvatar();
            return;
        }

        int currentIndex = GetAvatarIndex(CurrentAvatar.Target);
        int prevIndex = currentIndex - 1;
        if (prevIndex < 0)
            prevIndex = AvailableAvatars.Count - 1;

        EquipAvatar(AvailableAvatars[prevIndex]);
    }

    /// <summary>
    /// Import an avatar draft from disk. The imported avatar must be finalized before equip.
    /// </summary>
    public async void ImportAndEquipAvatar(string filePath, LocalDB localDB = null)
    {
        Logger.Warn("AvatarManager: ImportAndEquipAvatar now imports a draft avatar that must be finalized before equip");

        try
        {
            var importSlot = Slot.AddSlot("ImportedAvatar");
            var result = await ModelImporter.ImportAvatarAsync(filePath, importSlot, localDB);

            if (result.Success && result.RootSlot != null)
            {
                Logger.Log("AvatarManager: Avatar imported as draft");
            }
            else
            {
                Logger.Error($"AvatarManager: Failed to import avatar: {result.ErrorMessage}");
                importSlot.Destroy();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AvatarManager: Exception importing avatar: {ex.Message}");
        }
    }

    private void ResolveUserRoot()
    {
        if (UserRoot.Target != null)
            return;

        var userRoot = Slot.GetComponentInParent<UserRoot>();
        if (userRoot != null)
            UserRoot.Target = userRoot;
    }

    private bool TryResolveFinalizedAvatar(
        Slot avatarSlot,
        out AvatarDescriptor descriptor,
        out AvatarRoot avatarRoot,
        out SkeletonBuilder skeleton,
        out BipedRig rig,
        out string reason)
    {
        descriptor = null;
        avatarRoot = null;
        skeleton = null;
        rig = null;
        reason = string.Empty;

        if (avatarSlot == null || avatarSlot.IsDestroyed)
        {
            reason = "avatar slot is missing";
            return false;
        }

        var draft = avatarSlot.GetComponent<AvatarDraft>();
        if (draft != null)
        {
            draft.RefreshResolvedReferences();
            if (!draft.IsFinalized.Value)
            {
                reason = "avatar draft has not been finalized";
                return false;
            }
        }

        descriptor = avatarSlot.GetComponent<AvatarDescriptor>();
        avatarRoot = avatarSlot.GetComponent<AvatarRoot>();
        if (descriptor == null || avatarRoot == null)
        {
            reason = "avatar is missing finalized metadata";
            return false;
        }

        descriptor.ResolveAvatarData(avatarSlot);
        if (!descriptor.IsFinalized.Value)
        {
            reason = "avatar descriptor is not finalized";
            return false;
        }

        skeleton = descriptor.Skeleton.Target;
        rig = descriptor.Rig.Target;
        if (skeleton == null || rig == null)
        {
            reason = "avatar descriptor is missing skeleton or rig data";
            return false;
        }

        return true;
    }

    private static GodotIKAvatar EnsureGodotIKAvatar(Slot avatarSlot)
    {
        var ikAvatar = avatarSlot.GetComponentInChildren<GodotIKAvatar>();
        if (ikAvatar != null)
            return ikAvatar;

        var ikSlot = avatarSlot.AddSlot("IK");
        return ikSlot.AttachComponent<GodotIKAvatar>();
    }

    private static void ReleaseAvatarTracking(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return;

        var dequippedObjects = new HashSet<IAvatarObject>();
        foreach (var poseNode in avatarSlot.GetComponentsInChildren<AvatarPoseNode>())
        {
            poseNode.EquippingSlot?.Dequip(dequippedObjects);
        }
    }
}
