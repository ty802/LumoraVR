// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;

namespace Lumora.Godot.UI;

/// <summary>
/// Partial class: avatar creator button flow for imported avatars.
/// </summary>
public partial class ImportDialog
{
    private void UpdateAvatarSetupButton()
    {
        if (_btnAvatarSetup == null)
            return;

        var avatarSlot = ResolveImportedAvatarSlot();
        bool hasRig = avatarSlot != null
            && avatarSlot.GetComponentInChildren<SkeletonBuilder>() != null
            && avatarSlot.GetComponentInChildren<BipedRig>() != null;

        _btnAvatarSetup.Visible = avatarSlot != null;
        _btnAvatarSetup.Disabled = _isImporting || !hasRig;

        bool creatorOpen = avatarSlot?.World?.RootSlot?.GetComponentInChildren<AvatarCreator>() != null;
        _btnAvatarSetup.Text = creatorOpen ? "Creator Open" : "Avatar Creator";
    }

    private void OnAvatarSetupPressed()
    {
        if (_isImporting)
            return;

        var avatarSlot = ResolveImportedAvatarSlot();
        if (avatarSlot == null)
        {
            SetCompletedStatus("No imported avatar is available.", success: false);
            UpdateAvatarSetupButton();
            return;
        }

        try
        {
            if (avatarSlot.GetComponentInChildren<SkeletonBuilder>() == null ||
                avatarSlot.GetComponentInChildren<BipedRig>() == null)
            {
                SetCompletedStatus("Avatar rig is not ready yet.", success: false);
                UpdateAvatarSetupButton();
                return;
            }

            // Spawn the in-world creator on its own slot (it stands a figure of grabbable markers in
            // front of you). Line the markers up over the imported avatar and aim at Create; Create
            // finds the rig by overlap and makes the avatar click-to-equip.
            var world = avatarSlot.World;
            if (world?.RootSlot == null)
            {
                SetCompletedStatus("Avatar world is not available.", success: false);
                UpdateAvatarSetupButton();
                return;
            }
            if (world.RootSlot.GetComponentInChildren<AvatarCreator>() != null)
            {
                SetCompletedStatus("Creator is already open - line the markers up over your avatar and aim at the green Create button.");
                UpdateAvatarSetupButton();
                return;
            }

            world.RootSlot.AddSlot("Avatar Creator").AttachComponent<AvatarCreator>();
            SetSubtitle("Avatar creator open");
            SetCompletedStatus("In-world creator spawned. Scale and slide the markers over your avatar, then aim at the green Create button.");
            UpdateAvatarSetupButton();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ImportDialog: Avatar creator action failed: {ex.Message}");
            SetCompletedStatus($"Avatar action failed: {ex.Message}", success: false);
            UpdateAvatarSetupButton();
        }
    }

    private Slot? ResolveImportedAvatarSlot()
    {
        if (_lastImportedAvatarSlot == null || _lastImportedAvatarSlot.IsDestroyed)
        {
            _lastImportedAvatarSlot = null!;
            return null;
        }

        return _lastImportedAvatarSlot;
    }

}
