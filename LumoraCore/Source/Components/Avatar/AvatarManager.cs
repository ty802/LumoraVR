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
    // AvatarNameTagAssigner pushes into avatar text components.
    public readonly Sync<string> NameTagText = new();
    public readonly Sync<color> NameTagColor = new();
    public readonly Sync<color> NameTagOutline = new();
    public readonly Sync<color> NameTagBackground = new();

    // Auto-composed name badge (mesh text + assigner + position/face
    // components). Suppressed when an equipped avatar carries its own
    // AvatarNameTagAssigner.
    public readonly Sync<bool> AutoAddNameBadge = new();
    private readonly SyncRef<Slot> _autoNameBadge = new();

    public SyncRefList<Slot> AvailableAvatars { get; private set; } = null!;

    public event Action<Slot> OnAvatarChanged = null!;

    public bool IsEquippingManually { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        AvailableAvatars = new SyncRefList<Slot>(this);
        NameTagColor.Value = color.White;
        NameTagOutline.Value = color.Black;
        AutoAddNameBadge.Value = true;
        NameTagText.OnChanged += _ => UpdateNameTags();
        NameTagColor.OnChanged += _ => UpdateNameTags();
        Logger.Log($"AvatarManager: Awake on slot '{Slot.SlotName.Value}'");
    }

    /// <summary>
    /// Push current name-tag data into every assigner under this manager.
    /// </summary>
    public void UpdateNameTags()
    {
        if (Slot == null || World?.IsAuthority != true)
            return;

        foreach (var assigner in Slot.GetComponentsInChildren<AvatarNameTagAssigner>())
        {
            assigner.UpdateTags(this);
        }
    }

    // Compose the default badge: mesh text + assigner above the user's head,
    // each peer billboarding it locally. An equipped avatar that brings its
    // own assigner replaces the auto badge.
    private void EnsureNameBadge()
    {
        if (World?.IsAuthority != true || Slot == null)
            return;

        if (!AutoAddNameBadge.Value)
        {
            _autoNameBadge.Target?.Destroy();
            _autoNameBadge.Target = null!;
            return;
        }

        bool hasCustom = false;
        foreach (var assigner in Slot.GetComponentsInChildren<AvatarNameTagAssigner>())
        {
            if (assigner.Slot != _autoNameBadge.Target)
            {
                hasCustom = true;
                break;
            }
        }

        if (hasCustom)
        {
            _autoNameBadge.Target?.Destroy();
            _autoNameBadge.Target = null!;
            return;
        }

        if (_autoNameBadge.Target != null && !_autoNameBadge.Target.IsDestroyed)
            return;

        var badge = Slot.AddSlot("Name Badge");
        badge.Persistent.Value = false;

        var fontProvider = badge.AttachComponent<Assets.FontProvider>();
        fontProvider.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

        var text = badge.AttachComponent<TextRenderer>();
        text.Size.Value = 0.07f;
        text.Font.Target = fontProvider;

        var assignerNew = badge.AttachComponent<AvatarNameTagAssigner>();
        assignerNew.LabelTargets.Add(text);

        badge.AttachComponent<PositionAtUser>();
        badge.AttachComponent<FaceLocalUser>();

        _autoNameBadge.Target = badge;
        UpdateNameTags();
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
        EnsureNameBadge();
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

        // Descending priority - Root last (it's MaxValue and reparents the rest).
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

        // A full-hand avatar piece dequips the controller slot (and similar):
        // collect every exclusive node declared by the incoming objects and
        // dequip those slots from the still-unmatched set.
        var exclusiveNodes = new HashSet<BodyNode>();
        foreach (var (_, obj) in pairs)
        {
            foreach (var node in obj.MutuallyExclusiveNodes)
                exclusiveNodes.Add(node);
        }
        foreach (var node in exclusiveNodes)
        {
            for (int i = objectSlots.Count - 1; i >= 0; i--)
            {
                if (objectSlots[i].Node.Value == node)
                    objectSlots[i].Dequip(dequipped);
            }
        }

        // Dequipped trees must not linger under the manager - orphan them to
        // the world root so the old avatar doesn't accumulate as a hidden
        // child, then optionally destroy.
        foreach (var dq in dequipped)
        {
            if (dq is not Component dqc || dqc.Slot == null || dqc.Slot.IsDestroyed)
                continue;

            if (dqc.Slot.IsDescendantOf(Slot))
                dqc.Slot.SetParent(World.RootSlot);

            if (forceDestroyOld)
                dqc.Slot.Destroy();
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

        EnsureNameBadge();

        Logger.Log($"AvatarManager: Equipped {pairs.Count} object(s) from '{target.SlotName.Value}'");
        OnAvatarChanged?.Invoke(target);
        return true;
    }

    // Walk the user's AvatarObjectSlots and ask EmptySlotHandler to fill any
    // that have nothing equipped. Called automatically after every non-fill
    // Equip(); also callable directly when the handler swaps. - xlinka
    //
    // Only the authority fills: Equipped/EmptySlotHandler replicate to every
    // peer, so without the gate each client would see "empty" slots and spawn
    // duplicate default pieces.
    public void FillEmptySlots(List<AvatarObjectSlot> candidateSlots = null!)
    {
        if (World?.IsAuthority != true) return;
        if (_isFillingEmptySlots) return;
        if (EmptySlotHandler.Target == null) return;

        var userRoot = UserRoot.Target ?? Slot?.ActiveUserRoot;
        if (userRoot == null) return;

        // Don't equip into an untracked user - pieces would spawn at authored
        // defaults and visibly snap once the first head pose arrives. Re-check
        // every few frames until tracking is live (desktop reports true
        // immediately).
        if (!userRoot.ReceivedFirstPositionalData)
        {
            Slot?.RunInUpdates(10, () =>
            {
                if (!IsDestroyed)
                    FillEmptySlots(candidateSlots);
            });
            return;
        }

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
            // Already under a user - can't equip something already worn.
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
        var avatarSlot = CurrentAvatar.Target;
        if (avatarSlot == null) return;

        Logger.Log($"AvatarManager: Dequipping '{avatarSlot.SlotName.Value}'");

        // Dequip every body-node slot whose equipped object lives under this
        // tree so OnDequip fires and pose-node drives release on all peers.
        var userRoot = UserRoot.Target ?? Slot?.ActiveUserRoot;
        if (userRoot != null)
        {
            var dequipped = new HashSet<IAvatarObject>();
            foreach (var objSlot in userRoot.GetRegisteredComponents<AvatarObjectSlot>())
            {
                if (objSlot.Equipped?.Target is Component equipped && equipped.Slot != null &&
                    (equipped.Slot == avatarSlot || equipped.Slot.IsDescendantOf(avatarSlot)))
                {
                    objSlot.Dequip(dequipped);
                }
            }
        }
        else
        {
            ReleaseAvatarTracking(avatarSlot);
        }

        var avatarRoot = avatarSlot.GetComponent<AvatarRoot>();
        if (avatarRoot != null)
            avatarRoot.IsActive.Value = false;

        // Don't leave the dead tree hidden under the manager.
        if (Slot != null && avatarSlot.IsDescendantOf(Slot))
            avatarSlot.SetParent(World.RootSlot);

        avatarSlot.ActiveSelf.Value = false;
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

    // Single-avatar equip: an avatar is a self-describing component tree
    // (AvatarRoot + skeleton + rig + pose nodes). If it has a rigged body,
    // wire IK; either way, dispatch through the body-node pipeline. No
    // finalize gate - matching how avatars work everywhere else. - xlinka
    public bool EquipAvatar(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return false;

        ResolveUserRoot();
        if (UserRoot.Target == null)
        {
            Logger.Warn("AvatarManager: Cannot equip avatar without a UserRoot");
            return false;
        }

        if (avatarSlot.GetComponent<AvatarRoot>() == null)
            avatarSlot.AttachComponent<AvatarRoot>();

        // Rigged avatars get IK; unrigged trees still equip as plain
        // pose-node compositions.
        var skeleton = avatarSlot.GetComponentInChildren<SkeletonBuilder>();
        var rig = avatarSlot.GetComponentInChildren<BipedRig>();
        if (skeleton != null && rig != null)
        {
            var vrikAvatar = avatarSlot.GetComponent<VRIKAvatar>() ?? avatarSlot.AttachComponent<VRIKAvatar>();
            if (!vrikAvatar.SetupFromAvatar(UserRoot.Target))
            {
                Logger.Warn("AvatarManager: Avatar IK setup failed");
                return false;
            }
        }

        DequipCurrentAvatar();
        return Equip(avatarSlot, isManualEquip: true);
    }

    public bool CanEquipAvatar(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return false;
        return avatarSlot.GetComponentInChildren<SkeletonBuilder>() != null
            && avatarSlot.GetComponentInChildren<BipedRig>() != null;
    }

    public async void ImportAndEquipAvatar(string filePath, LocalDB localDB = null!)
    {
        try
        {
            var importSlot = Slot.AddSlot("ImportedAvatar");
            var result = await ModelImporter.ImportAvatarAsync(filePath, importSlot, localDB);

            if (result.Success && result.RootSlot != null)
            {
                Logger.Log("AvatarManager: Avatar imported - calibrate via the avatar creator, then equip");
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
