// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Marks an imported avatar that has not yet been finalized into a reusable avatar definition.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public sealed class AvatarDraft : Component
{
    public readonly Sync<bool> IsFinalized = new();
    public readonly Sync<string> SourcePathOrUri = new();
    public readonly Sync<string> ImportProfile = new();
    public readonly SyncRef<SkeletonBuilder> Skeleton = new();
    public readonly SyncRef<BipedRig> Rig = new();
    public readonly SyncRef<AvatarDescriptor> Descriptor = new();

    public override void OnInit()
    {
        base.OnInit();
        IsFinalized.Value = false;
        SourcePathOrUri.Value = string.Empty;
        ImportProfile.Value = string.Empty;
    }

    /// <summary>
    /// Refresh cached references to the imported skeleton and rig.
    /// </summary>
    public bool RefreshResolvedReferences()
    {
        if (Skeleton.Target == null)
            Skeleton.Target = Slot.GetComponentInChildren<SkeletonBuilder>();

        if (Rig.Target == null)
            Rig.Target = Slot.GetComponentInChildren<BipedRig>();

        if (Descriptor.Target == null)
            Descriptor.Target = Slot.GetComponent<AvatarDescriptor>();

        return Skeleton.Target != null && Rig.Target != null;
    }

    public bool IsReady
    {
        get
        {
            var modelData = Slot.GetComponent<ModelData>();
            var isLoaded = modelData?.IsLoaded.Value ?? true;
            return isLoaded && RefreshResolvedReferences();
        }
    }
}
