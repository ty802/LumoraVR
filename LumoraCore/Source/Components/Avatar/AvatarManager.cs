// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Input;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Per-user avatar coordinator. Dispatches IAvatarObjects from a target avatar
// slot tree onto the user's AvatarObjectSlots by matching BodyNode. Allows
// a custom avatar's hands + default headset + default view to coexist on
// the same user since each body node is filled independently. - xlinka
[ComponentCategory("Avatar")]
public class AvatarManager : UserRootComponent
{
    public readonly SyncRef<Slot> CurrentAvatar = new();
    public readonly SyncRef<UserRoot> UserRoot = new();
    public readonly SyncRef<Slot> DefaultAvatarSlot = new();
    public readonly Sync<bool> IsUsingDefaultAvatar = new();

    // Plug-in supplier for default head/hands/view pieces when a body-node
    // slot has nothing equipped. CommonAvatarBuilder installs itself here on
    // user spawn. Set Target to null to disable auto-filling.
    public readonly SyncRef<IEmptyAvatarSlotHandler> EmptySlotHandler = new();

    private CancellationTokenSource _fillCts = null!;
    private bool _isFillingEmptySlots;

    // Display name/color values used by name badges/tags. Per-user state that
    // AvatarNameTagAssigner (when added) pushes into avatar text components.
    public readonly Sync<string> NameTagText = new();
    public readonly Sync<color> NameTagColor = new();
    public readonly Sync<color> NameTagOutline = new();
    public readonly Sync<color> NameTagBackground = new();

    public SyncRefList<Slot> AvailableAvatars { get; private set; } = null!;

    public event Action<Slot> OnAvatarChanged = null!;

    public bool IsEquippingManually { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        AvailableAvatars = new SyncRefList<Slot>(this);
        NameTagColor.Value = color.White;
        NameTagOutline.Value = color.Black;
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
        EnsureRootObjectSlot();

        // Fill default head/hands/view once everything's wired. If the handler
        // isn't attached yet (SimpleUserSpawn sets it after AvatarManager
        // attach), the fill is a no-op and EquipDefaultAvatar can re-trigger
        // it later. - xlinka
        FillEmptySlots();
    }

    // Body-node dispatch equip. Walks target's IAvatarObject components,
    // pairs them with the user's AvatarObjectSlots by BodyNode + priority,
    // calls Equip on each pair, then reparents the target tree under us.
    public bool Equip(Slot target, bool isManualEquip = false, bool forceDestroyOld = false, bool isFillingEmptySlot = false)
    {
        if (target == null || target.IsDestroyed)
            return false;

        ResolveUserRoot();
        var userRoot = UserRoot.Target ?? Slot.ActiveUserRoot;
        if (userRoot == null)
        {
            Logger.Warn("AvatarManager: Cannot equip, no user root");
            return false;
        }

        IsEquippingManually = isManualEquip;

        var equipObjects = new List<IAvatarObject>();
        CollectEquippableObjects(target, equipObjects);
        if (equipObjects.Count == 0)
        {
            Logger.Warn($"AvatarManager: No equippable IAvatarObject found on '{target.SlotName.Value}'");
            return false;
        }

        // Descending priority â€” Root last (it's MaxValue and reparents the rest).
        equipObjects.Sort((a, b) => -a.EquipOrderPriority.CompareTo(b.EquipOrderPriority));

        // Collect user's body-node slots, deduping by BodyNode (one per node).
        var objectSlots = new List<AvatarObjectSlot>();
        var seenNodes = new HashSet<BodyNode>();
        foreach (var s in userRoot.GetRegisteredComponents<AvatarObjectSlot>())
        {
            if (seenNodes.Add(s.Node.Value))
                objectSlots.Add(s);
        }

        var pairs = new List<(AvatarObjectSlot objSlot, IAvatarObject obj)>();
        var dequipped = new HashSet<IAvatarObject>();

        foreach (var obj in equipObjects)
        {
            for (int i = 0; i < objectSlots.Count; i++)
            {
                var objSlot = objectSlots[i];
                if (!objSlot.PreEquip(obj, dequipped)) continue;
                pairs.Add((objSlot, obj));
                objectSlots.RemoveAt(i);
                break;
            }
        }

        if (pairs.Count == 0)
        {
            Logger.Warn($"AvatarManager: No body-node slots matched on '{target.SlotName.Value}'");
            return false;
        }

        if (forceDestroyOld)
        {
            foreach (var dq in dequipped)
            {
                if (dq is Component dqc && dqc.Slot != null && !dqc.Slot.IsDestroyed)
                    dqc.Slot.Destroy();
            }
        }

        foreach (var (objSlot, obj) in pairs)
        {
            objSlot.Equip(obj);
        }

        // Reparent the target under us so the dequipped tree doesn't leak in the world.
        // AvatarRoot.Equip already reparents itself; this handles non-root targets.
        if (target != Slot && !target.IsDescendantOf(Slot))
        {
            target.SetParent(Slot);
        }

        CurrentAvatar.Target = target;
        IsUsingDefaultAvatar.Value = false;

        if (!AvailableAvatars.Contains(target))
            AvailableAvatars.Add(target);

        if (!isFillingEmptySlot)
        {
            FillEmptySlots(objectSlots);
        }

        Logger.Log($"AvatarManager: Equipped {pairs.Count} object(s) from '{target.SlotName.Value}'");
        OnAvatarChanged?.Invoke(target);
        return true;
    }

    // Walk the user's AvatarObjectSlots and ask EmptySlotHandler to fill any
    // that have nothing equipped. Called automatically after every non-fill
    // Equip(); also callable directly when the handler swaps. - xlinka
    public void FillEmptySlots(List<AvatarObjectSlot> candidateSlots = null!)
    {
        if (_isFillingEmptySlots) return;
        if (EmptySlotHandler.Target == null) return;

        var userRoot = UserRoot.Target ?? Slot?.ActiveUserRoot;
        if (userRoot == null) return;

        _fillCts?.Cancel();
        _fillCts?.Dispose();
        _fillCts = new CancellationTokenSource();
        var token = _fillCts.Token;

        candidateSlots ??= userRoot.GetRegisteredComponents<AvatarObjectSlot>();
        _isFillingEmptySlots = true;
        try
        {
            foreach (var slot in candidateSlots)
            {
                if (slot == null || slot.IsDestroyed) continue;
                if (slot.HasEquipped) continue;
                var handler = EmptySlotHandler.Target;
                if (handler == null) break;
                _ = handler.FillEmptySlot(slot.Node.Value, this, token);
            }
        }
        finally
        {
            _isFillingEmptySlots = false;
        }
    }

    public bool CanEquip(IAvatarObject avatarObject)
    {
        if (avatarObject == null) return false;
        if (avatarObject.IsEquipped) return false;
        if (avatarObject is Component c)
        {
            if (c.Slot == null || c.Slot.IsDestroyed) return false;
            // Already under a user â€” can't equip something already worn.
            if (c.Slot.ActiveUserRoot != null) return false;
        }
        if (Slot.ActiveUserRoot == null) return false;
        return true;
    }

    private void CollectEquippableObjects(Slot slot, List<IAvatarObject> output)
    {
        if (slot == null || slot.IsDestroyed) return;

        foreach (var c in slot.Components)
        {
            if (c is IAvatarObject obj && CanEquip(obj))
                output.Add(obj);
        }
        foreach (var child in slot.Children)
        {
            CollectEquippableObjects(child, output);
        }
    }

    private void EnsureRootObjectSlot()
    {
        if (Slot == null || Slot.ActiveUserRoot == null) return;

        var existing = Slot.GetComponent<AvatarObjectSlot>();
        if (existing == null || existing.Node.Value != BodyNode.Root)
        {
            var rootObjSlot = Slot.AttachComponent<AvatarObjectSlot>();
            rootObjSlot.Node.Value = BodyNode.Root;
        }
    }

    public void DequipCurrentAvatar()
    {
        if (CurrentAvatar.Target == null) return;

        Logger.Log($"AvatarManager: Dequipping '{CurrentAvatar.Target.SlotName.Value}'");

        ReleaseAvatarTracking(CurrentAvatar.Target);

        var avatarRoot = CurrentAvatar.Target.GetComponent<AvatarRoot>();
        if (avatarRoot != null)
            avatarRoot.IsActive.Value = false;

        CurrentAvatar.Target.ActiveSelf.Value = false;
        CurrentAvatar.Target = null!;
    }

    public void EquipDefaultAvatar()
    {
        Logger.Log("AvatarManager: Equipping default avatar via EmptySlotHandler");

        DequipCurrentAvatar();
        ResolveUserRoot();
        FillEmptySlots();
        IsUsingDefaultAvatar.Value = true;
    }

    public void RemoveAvatar(Slot avatarSlot)
    {
        if (avatarSlot == null) return;

        if (CurrentAvatar.Target == avatarSlot)
            EquipDefaultAvatar();

        if (avatarSlot == DefaultAvatarSlot.Target) return;

        AvailableAvatars.Remove(avatarSlot);
        avatarSlot.Destroy();
        Logger.Log("AvatarManager: Removed avatar");
    }

    public int GetAvatarIndex(Slot avatarSlot)
    {
        for (int i = 0; i < AvailableAvatars.Count; i++)
        {
            if (AvailableAvatars[i] == avatarSlot)
                return i;
        }
        return -1;
    }

    public void CycleNextAvatar()
    {
        if (AvailableAvatars.Count == 0)
        {
            EquipDefaultAvatar();
            return;
        }
        int currentIndex = GetAvatarIndex(CurrentAvatar.Target);
        int nextIndex = (currentIndex + 1) % AvailableAvatars.Count;
        EquipAvatar(AvailableAvatars[nextIndex]!);
    }

    public void CyclePreviousAvatar()
    {
        if (AvailableAvatars.Count == 0)
        {
            EquipDefaultAvatar();
            return;
        }
        int currentIndex = GetAvatarIndex(CurrentAvatar.Target);
        int prevIndex = currentIndex - 1;
        if (prevIndex < 0) prevIndex = AvailableAvatars.Count - 1;
        EquipAvatar(AvailableAvatars[prevIndex]!);
    }

    // Legacy single-avatar equip path: resolves the descriptor, sets up IK,
    // then runs the new body-node dispatch. Kept so existing callers
    // (Settings UI, ImportDialog) don't need to change. - xlinka
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

        var vrikAvatar = avatarSlot.GetComponent<VRIKAvatar>() ?? avatarSlot.AttachComponent<VRIKAvatar>();
        vrikAvatar.Descriptor.Target = descriptor;
        if (!vrikAvatar.SetupFromDescriptor(descriptor, UserRoot.Target))
        {
            Logger.Warn("AvatarManager: Avatar runtime setup failed");
            return false;
        }

        DequipCurrentAvatar();

        descriptor.Root.Target = avatarRoot;
        descriptor.Skeleton.Target = skeleton;
        descriptor.Rig.Target = rig;
        descriptor.IsFinalized.Value = true;

        return Equip(avatarSlot, isManualEquip: true);
    }

    public bool CanEquipAvatar(Slot avatarSlot)
    {
        return TryResolveFinalizedAvatar(avatarSlot, out _, out _, out _, out _, out _);
    }

    public async void ImportAndEquipAvatar(string filePath, LocalDB localDB = null!)
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
        if (UserRoot.Target != null) return;
        UserRoot.Target = Slot?.ActiveUserRoot ?? Slot?.GetComponentInParent<UserRoot>()!;
    }

    private bool TryResolveFinalizedAvatar(
        Slot avatarSlot,
        out AvatarDescriptor descriptor,
        out AvatarRoot avatarRoot,
        out SkeletonBuilder skeleton,
        out BipedRig rig,
        out string reason)
    {
        descriptor = null!;
        avatarRoot = null!;
        skeleton = null!;
        rig = null!;
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

    private static void ReleaseAvatarTracking(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed) return;

        var dequipped = new HashSet<IAvatarObject>();
        foreach (var poseNode in avatarSlot.GetComponentsInChildren<AvatarPoseNode>())
        {
            poseNode.EquippingSlot?.Dequip(dequipped);
        }
    }
}
