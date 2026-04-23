// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Persistent avatar metadata created by the avatar creator flow.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public sealed class AvatarDescriptor : Component
{
    public readonly SyncRef<AvatarRoot> Root = new();
    public readonly SyncRef<SkeletonBuilder> Skeleton = new();
    public readonly SyncRef<BipedRig> Rig = new();

    public readonly SyncRef<Slot> ViewReference = new();
    public readonly SyncRef<Slot> LeftHandReference = new();
    public readonly SyncRef<Slot> RightHandReference = new();
    public readonly SyncRef<Slot> LeftFootReference = new();
    public readonly SyncRef<Slot> RightFootReference = new();
    public readonly SyncRef<Slot> PelvisReference = new();

    public readonly Sync<bool> HasFeetCalibration = new();
    public readonly Sync<bool> HasPelvisCalibration = new();
    public readonly Sync<bool> IsFinalized = new();
    public readonly Sync<int> CreatorVersion = new();

    public override void OnInit()
    {
        base.OnInit();
        IsFinalized.Value = false;
        CreatorVersion.Value = 1;
    }

    /// <summary>
    /// Ensure the descriptor points at the avatar root, skeleton, and rig on the owning slot.
    /// </summary>
    public bool ResolveAvatarData(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return false;

        if (Root.Target == null)
            Root.Target = avatarSlot.GetComponent<AvatarRoot>();

        if (Skeleton.Target == null)
            Skeleton.Target = avatarSlot.GetComponentInChildren<SkeletonBuilder>();

        if (Rig.Target == null)
            Rig.Target = avatarSlot.GetComponentInChildren<BipedRig>();

        return Root.Target != null && Skeleton.Target != null && Rig.Target != null;
    }

    public Slot? GetReferenceSlot(AvatarReferenceKind kind)
    {
        return kind switch
        {
            AvatarReferenceKind.View => ViewReference.Target,
            AvatarReferenceKind.LeftHandGrip => LeftHandReference.Target,
            AvatarReferenceKind.RightHandGrip => RightHandReference.Target,
            AvatarReferenceKind.LeftFoot => LeftFootReference.Target,
            AvatarReferenceKind.RightFoot => RightFootReference.Target,
            AvatarReferenceKind.Pelvis => PelvisReference.Target,
            _ => null
        };
    }

    public void SetReferenceSlot(AvatarReferenceKind kind, Slot slot)
    {
        switch (kind)
        {
            case AvatarReferenceKind.View:
                ViewReference.Target = slot;
                break;
            case AvatarReferenceKind.LeftHandGrip:
                LeftHandReference.Target = slot;
                break;
            case AvatarReferenceKind.RightHandGrip:
                RightHandReference.Target = slot;
                break;
            case AvatarReferenceKind.LeftFoot:
                LeftFootReference.Target = slot;
                break;
            case AvatarReferenceKind.RightFoot:
                RightFootReference.Target = slot;
                break;
            case AvatarReferenceKind.Pelvis:
                PelvisReference.Target = slot;
                break;
        }
    }
}
