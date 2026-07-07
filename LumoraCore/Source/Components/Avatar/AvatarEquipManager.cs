// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Input;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Per-user avatar coordinator. Dispatches IAvatarEquippables from a target avatar
// slot tree onto the user's AvatarSockets by matching BodyNode. Allows
// a custom avatar's hands + default headset + default view to coexist on
// the same user since each body node is filled independently. - xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarEquipManager : UserRootComponent
{
    public readonly SyncRef<Slot> CurrentAvatar = new();
    public readonly SyncRef<UserRoot> UserRoot = new();
    public readonly SyncRef<Slot> DefaultAvatarSlot = new();
    public readonly Sync<bool> IsUsingDefaultAvatar = new();

    // Plug-in supplier for default head/hands/view pieces when a body-node
    // slot has nothing equipped. AvatarAssembler installs itself here on
    // user spawn. Set Target to null to disable auto-filling.
    [OldName("EmptySlotHandler")]
    public readonly SyncRef<IAvatarSocketFiller> SocketFiller = new();

    private CancellationTokenSource _fillCts = null!;
    private bool _isFillingEmptySlots;

    // Display name/color values used by name badges/tags. Per-user state that
    // NameBadgeDriver pushes into avatar text components.
    [OldName("NameTagText")]
    public readonly Sync<string> BadgeText = new();
    [OldName("NameTagColor")]
    public readonly Sync<color> BadgeColor = new();
    [OldName("NameTagOutline")]
    public readonly Sync<color> BadgeOutline = new();
    [OldName("NameTagBackground")]
    public readonly Sync<color> BadgeBackground = new();

    // Auto-composed name badge (mesh text + assigner + position/face
    // components). Suppressed when an equipped avatar carries its own
    // NameBadgeDriver.
    public readonly Sync<bool> AutoAddNameBadge = new();
    private readonly SyncRef<Slot> _autoNameBadge = new();

    public readonly SyncRefList<Slot> AvailableAvatars;

    public event Action<Slot> OnAvatarChanged = null!;

    public bool IsEquippingManually { get; private set; }

    public AvatarEquipManager()
    {
        AvailableAvatars = new SyncRefList<Slot>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        BadgeColor.Value = color.White;
        BadgeOutline.Value = color.Black;
        BadgeBackground.Value = new color(0f, 0f, 0f, 0.6f);
        AutoAddNameBadge.Value = true;
        BadgeText.OnChanged += _ => UpdateBadges();
        BadgeColor.OnChanged += _ => UpdateBadges();
        BadgeOutline.OnChanged += _ => UpdateBadges();
        Logger.Log($"AvatarEquipManager: Awake on slot '{Slot.SlotName.Value}'");
    }

    /// <summary>
    /// Push current name-tag data into every assigner under this manager.
    /// </summary>
    public void UpdateBadges()
    {
        if (Slot == null || World?.IsAuthority != true)
            return;

        foreach (var assigner in Slot.GetComponentsInChildren<NameBadgeDriver>())
        {
            assigner.UpdateBadge(this);
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
        foreach (var assigner in Slot.GetComponentsInChildren<NameBadgeDriver>())
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
        // Center on the above-head origin (PositionAtUser anchors here); default is Left/Top,
        // which pushes the name down-right of the head instead of sitting centered above it.
        text.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        text.VerticalAlign.Value = TextVerticalAlignment.Middle;

        var assignerNew = badge.AttachComponent<NameBadgeDriver>();
        assignerNew.LabelTargets.Add(text);

        // Readable outline around the name (a dilated coverage ring on our text atlas). The
        // outline COLOR comes from BadgeOutline via the assigner on UpdateBadges; thickness
        // is a fixed visual in atlas texels.
        text.OutlineThickness.Value = 2.5f;

        // Sit clearly above the head, not on it. Anchored at the tracking head
        // (PositionAtUser -> UserRoot.HeadSlot); 0.35 up clears the head visual.
        var position = badge.AttachComponent<PositionAtUser>();
        position.VerticalOffset.Value = 0.35f;
        badge.AttachComponent<FaceLocalUser>();

        _autoNameBadge.Target = badge;
        UpdateBadges();
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
        // isn't attached yet (SimpleUserSpawn sets it after AvatarEquipManager
        // attach), the fill is a no-op and EquipDefaultAvatar can re-trigger
        // it later. - xlinka
        FillEmptySockets();
        EnsureNameBadge();
    }

    // Body-node dispatch equip. Walks target's IAvatarEquippable components,
    // pairs them with the user's AvatarSockets by BodyNode + priority,
    // calls Equip on each pair, then reparents the target tree under us.
    public bool Equip(Slot target, bool isManualEquip = false, bool forceDestroyOld = false, bool isFillingEmptySlot = false)
    {
        if (target == null || target.IsDestroyed)
            return false;

        ResolveUserRoot();
        var userRoot = UserRoot.Target ?? Slot.ActiveUserRoot;
        if (userRoot == null)
        {
            Logger.Warn("AvatarEquipManager: Cannot equip, no user root");
            return false;
        }

        IsEquippingManually = isManualEquip;

        var equipObjects = new List<IAvatarEquippable>();
        CollectEquippableObjects(target, equipObjects);
        if (equipObjects.Count == 0)
        {
            Logger.Warn($"AvatarEquipManager: No equippable IAvatarEquippable found on '{target.SlotName.Value}'");
            return false;
        }

        // Descending priority - Root last (it's MaxValue and reparents the rest).
        equipObjects.Sort((a, b) => -a.EquipPriority.CompareTo(b.EquipPriority));

        // Collect user's body-node slots, deduping by BodyNode (one per node).
        var objectSlots = new List<AvatarSocket>();
        var seenNodes = new HashSet<BodyNode>();
        foreach (var s in userRoot.GetRegisteredComponents<AvatarSocket>())
        {
            if (seenNodes.Add(s.Node.Value))
                objectSlots.Add(s);
        }

        var pairs = new List<(AvatarSocket objSlot, IAvatarEquippable obj)>();
        var dequipped = new HashSet<IAvatarEquippable>();

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
            Logger.Warn($"AvatarEquipManager: No body-node slots matched on '{target.SlotName.Value}'");
            return false;
        }

        // A full-hand avatar piece dequips the controller slot (and similar):
        // collect every exclusive node declared by the incoming objects and
        // dequip those slots from the still-unmatched set.
        var exclusiveNodes = new HashSet<BodyNode>();
        foreach (var (_, obj) in pairs)
        {
            foreach (var node in obj.ConflictingNodes)
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
        // AvatarForm.Equip already reparents itself; this handles non-root targets.
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
            FillEmptySockets(objectSlots);
        }

        EnsureNameBadge();

        Logger.Log($"AvatarEquipManager: Equipped {pairs.Count} object(s) from '{target.SlotName.Value}'");
        OnAvatarChanged?.Invoke(target);
        return true;
    }

    // Walk the user's AvatarSockets and ask SocketFiller to fill any
    // that have nothing equipped. Called automatically after every non-fill
    // Equip(); also callable directly when the handler swaps. - xlinka
    //
    // Only the authority fills: Equipped/SocketFiller replicate to every
    // peer, so without the gate each client would see "empty" slots and spawn
    // duplicate default pieces.
    public void FillEmptySockets(List<AvatarSocket> candidateSlots = null!)
    {
        if (World?.IsAuthority != true) return;
        if (_isFillingEmptySlots) return;
        if (SocketFiller.Target == null) return;

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
                    FillEmptySockets(candidateSlots);
            });
            return;
        }

        _fillCts?.Cancel();
        _fillCts?.Dispose();
        _fillCts = new CancellationTokenSource();
        var token = _fillCts.Token;

        candidateSlots ??= userRoot.GetRegisteredComponents<AvatarSocket>();
        _isFillingEmptySlots = true;
        try
        {
            foreach (var slot in candidateSlots)
            {
                if (slot == null || slot.IsDestroyed) continue;
                if (slot.HasEquipped) continue;
                var handler = SocketFiller.Target;
                if (handler == null) break;
                _ = handler.FillSocket(slot.Node.Value, this, token);
            }
        }
        finally
        {
            _isFillingEmptySlots = false;
        }
    }

    public bool CanEquip(IAvatarEquippable avatarObject)
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

    private void CollectEquippableObjects(Slot slot, List<IAvatarEquippable> output)
    {
        if (slot == null || slot.IsDestroyed) return;

        foreach (var c in slot.Components)
        {
            if (c is IAvatarEquippable obj && CanEquip(obj))
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

        var existing = Slot.GetComponent<AvatarSocket>();
        if (existing == null || existing.Node.Value != BodyNode.Root)
        {
            var rootObjSlot = Slot.AttachComponent<AvatarSocket>();
            rootObjSlot.Node.Value = BodyNode.Root;
        }
    }

    public void DequipCurrentAvatar()
    {
        var avatarSlot = CurrentAvatar.Target;
        if (avatarSlot == null) return;

        Logger.Log($"AvatarEquipManager: Dequipping '{avatarSlot.SlotName.Value}'");

        // Dequip every body-node slot whose equipped object lives under this
        // tree so OnDequip fires and pose-node drives release on all peers.
        var userRoot = UserRoot.Target ?? Slot?.ActiveUserRoot;
        if (userRoot != null)
        {
            var dequipped = new HashSet<IAvatarEquippable>();
            foreach (var objSlot in userRoot.GetRegisteredComponents<AvatarSocket>())
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

        var avatarRoot = avatarSlot.GetComponent<AvatarForm>();
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
        Logger.Log("AvatarEquipManager: Equipping default avatar via SocketFiller");

        DequipCurrentAvatar();
        ResolveUserRoot();
        FillEmptySockets();
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
        Logger.Log("AvatarEquipManager: Removed avatar");
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
    // (AvatarForm + skeleton + rig + pose nodes). If it has a rigged body,
    // wire IK; either way, dispatch through the body-node pipeline. No
    // finalize gate - matching how avatars work everywhere else. - xlinka
    public bool EquipAvatar(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return false;

        ResolveUserRoot();
        if (UserRoot.Target == null)
        {
            Logger.Warn("AvatarEquipManager: Cannot equip avatar without a UserRoot");
            return false;
        }

        if (avatarSlot.GetComponent<AvatarForm>() == null)
            avatarSlot.AttachComponent<AvatarForm>();

        // Rigged avatars get IK; unrigged trees still equip as plain
        // pose-node compositions.
        var skeleton = avatarSlot.GetComponentInChildren<SkeletonBuilder>();
        var rig = avatarSlot.GetComponentInChildren<HumanoidRig>();
        if (skeleton != null && rig != null)
        {
            // Root frame must face the body: equip resets this slot to identity, so a stale frame wears the
            // avatar yawed off the user's forward. World-invariant + reference slots are live transforms, so
            // healing an old avatar here is safe. No-op when already aligned.
            AvatarCalibration.AlignAvatarFacing(avatarSlot, rig);

            var avatarIk = avatarSlot.GetComponent<AvatarIK>() ?? avatarSlot.AttachComponent<AvatarIK>();
            if (!avatarIk.SetupFromAvatar(UserRoot.Target))
            {
                Logger.Warn("AvatarEquipManager: Avatar IK setup failed");
                return false;
            }

            // Rigs with finger bones get a per-hand poser. It drives fingers only
            // from a live source (VR tracking); without one the hand rests.
            if (rig.HasLeftFingerBones)
                EnsureHandPoser(rig, Chirality.Left);
            if (rig.HasRightFingerBones)
                EnsureHandPoser(rig, Chirality.Right);

            // Coarse body colliders so a created/equipped avatar has grab/point hitboxes (previously only
            // the model-import path generated these). Idempotent - skips bones that already have one.
            avatarIk.GenerateBodyColliders();

            // Face drivers (blink/eye-look/eye-expression/mouth/viseme). The in-world AvatarStudio wires
            // these, but an import-built or directly-equipped avatar otherwise has a dead face. Attach the
            // same set here so every equip path gets it; idempotent, so a creator-built avatar isn't doubled.
            AttachFaceDrivers(avatarSlot, rig);
        }

        DequipCurrentAvatar();
        return Equip(avatarSlot, isManualEquip: true);
    }

    // Attach the avatar's face-driver set, mirroring the in-world creator. Eye drivers gate on eye bones;
    // mouth/viseme drivers always attach (they rest when nothing is tracking). Each is added only if absent.
    // LipSyncAnalyzer has no audio pipeline yet - it's attached ready, but stays inert until one feeds it.
    private static void AttachFaceDrivers(Slot avatarSlot, HumanoidRig rig)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return;

        if (rig.TryGetBone(BodyNode.LeftEye) != null || rig.TryGetBone(BodyNode.RightEye) != null)
        {
            if (avatarSlot.GetComponent<BlinkDriver>() == null)
                avatarSlot.AttachComponent<BlinkDriver>();
            if (avatarSlot.GetComponent<EyeGazeDriver>() == null)
                avatarSlot.AttachComponent<EyeGazeDriver>();
            if (avatarSlot.GetComponent<EyeExpressionDriver>() == null)
                avatarSlot.AttachComponent<EyeExpressionDriver>();
        }

        // Mouth/lip blendshapes from replicated face tracking; rests when nothing is tracking.
        if (avatarSlot.GetComponent<MouthExpressionDriver>() == null)
            avatarSlot.AttachComponent<MouthExpressionDriver>();

        if (avatarSlot.GetComponent<LipSyncAnalyzer>() == null)
            avatarSlot.AttachComponent<LipSyncAnalyzer>();
        if (avatarSlot.GetComponent<VisemeWeightDriver>() == null)
            avatarSlot.AttachComponent<VisemeWeightDriver>();

        // Breath blendshape (chest/belly rise) on the same rhythm as the bone breathing; no-op when
        // the avatar has no breath shape. -xlinka
        if (avatarSlot.GetComponent<BreathingDriver>() == null)
            avatarSlot.AttachComponent<BreathingDriver>();
    }

    // Attach a HandPoseDriver (+ its equip-time assigner) to a hand's wrist bone, once.
    // The poser binds to the rig's finger bones itself and drives them locally. An idle finger-pose preset
    // is wired as the poser's fallback so the hand holds a relaxed shape when no live tracking is present;
    // the user-root VR finger stream still wins whenever it's actually tracking.
    private static void EnsureHandPoser(HumanoidRig rig, Chirality side)
    {
        var wrist = rig.TryGetBone(side == Chirality.Left ? BodyNode.LeftHand : BodyNode.RightHand);
        if (wrist == null)
            return;

        foreach (var comp in wrist.Components)
            if (comp is HandPoseDriver)
                return;

        var poser = wrist.AttachComponent<HandPoseDriver>();
        poser.Side.Value = side;
        poser.HandRoot.Target = wrist;
        poser.IdlePose.Target = EnsureIdleHandPose(rig);

        var assigner = wrist.AttachComponent<HandPoseBinder>();
        assigner.Side.Value = side;
        assigner.TargetPoser.Target = poser;
    }

    // One idle finger-pose source per avatar, shared by both hands. A HandPosePreset on Idle serves a
    // relaxed hand shape (synthesized per-peer, only the Shape enum syncs). Finger-pose components were
    // otherwise never attached, so hands had no idle source at all.
    private static HandPosePreset EnsureIdleHandPose(HumanoidRig rig)
    {
        var host = rig.Slot;
        var preset = host.GetComponent<HandPosePreset>();
        if (preset == null)
        {
            preset = host.AttachComponent<HandPosePreset>();
            preset.Shape.Value = HandPoseShape.Idle;
        }
        return preset;
    }

    public bool CanEquipAvatar(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return false;
        return avatarSlot.GetComponentInChildren<SkeletonBuilder>() != null
            && avatarSlot.GetComponentInChildren<HumanoidRig>() != null;
    }

    public async void ImportAndEquipAvatar(string filePath, LocalDB localDB = null!)
    {
        try
        {
            var importSlot = Slot.AddSlot("ImportedAvatar");
            var result = await ModelImporter.ImportAvatarAsync(filePath, importSlot, localDB);

            if (result.Success && result.RootSlot != null)
            {
                Logger.Log("AvatarEquipManager: Avatar imported - calibrate via the avatar studio, then equip");
            }
            else
            {
                Logger.Error($"AvatarEquipManager: Failed to import avatar: {result.ErrorMessage}");
                importSlot.Destroy();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AvatarEquipManager: Exception importing avatar: {ex.Message}");
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

        var dequipped = new HashSet<IAvatarEquippable>();
        foreach (var poseNode in avatarSlot.GetComponentsInChildren<AvatarPoseDriver>())
        {
            poseNode.CurrentSocket?.Dequip(dequipped);
        }
    }
}
