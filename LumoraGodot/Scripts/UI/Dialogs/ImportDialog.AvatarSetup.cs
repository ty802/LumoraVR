// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using Lumora.Core;
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

        if (_activeAvatarCreatorSession != null && !_activeAvatarCreatorSession.IsActive)
            _activeAvatarCreatorSession = null;

        var avatarSlot = ResolveImportedAvatarSlot();
        var hasAvatar = avatarSlot != null;

        _btnAvatarSetup.Visible = hasAvatar;
        _btnAvatarSetup.Disabled = _isImporting || !hasAvatar;

        if (!hasAvatar)
        {
            _btnAvatarSetup.Text = "Avatar Creator";
            return;
        }

        if (_activeAvatarCreatorSession != null && _activeAvatarCreatorSession.Matches(avatarSlot))
        {
            _btnAvatarSetup.Text = "Create Avatar";
            return;
        }

        _btnAvatarSetup.Text = IsAvatarFinalized(avatarSlot)
            ? "Equip Avatar"
            : "Open Creator";
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
            if (_activeAvatarCreatorSession != null && !_activeAvatarCreatorSession.IsActive)
                _activeAvatarCreatorSession = null;

            if (_activeAvatarCreatorSession != null && _activeAvatarCreatorSession.Matches(avatarSlot))
            {
                if (_activeAvatarCreatorSession.Finalize(out var finalizeMessage))
                {
                    _activeAvatarCreatorSession = null;
                    SetSubtitle("Avatar created");
                    SetCompletedStatus(finalizeMessage);
                }
                else
                {
                    SetCompletedStatus(finalizeMessage, success: false);
                }

                UpdateAvatarSetupButton();
                return;
            }

            if (IsAvatarFinalized(avatarSlot))
            {
                if (TryEquipAvatar(avatarSlot, out var equipMessage))
                {
                    SetSubtitle("Avatar equipped");
                    SetCompletedStatus(equipMessage);
                }
                else
                {
                    SetCompletedStatus(equipMessage, success: false);
                }

                UpdateAvatarSetupButton();
                return;
            }

            ResetAvatarCreatorState();

            var setupParent = _targetSlot ?? avatarSlot.Parent ?? avatarSlot.World?.RootSlot;
            if (setupParent == null)
            {
                SetCompletedStatus("Could not open the avatar creator.", success: false);
                UpdateAvatarSetupButton();
                return;
            }

            var session = new AvatarCreatorSession(avatarSlot, setupParent);
            if (!session.Open(out var openMessage))
            {
                session.Dispose();
                SetCompletedStatus(openMessage, success: false);
                UpdateAvatarSetupButton();
                return;
            }

            _activeAvatarCreatorSession = session;
            SetSubtitle("Avatar creator open");
            SetCompletedStatus(openMessage);
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
            _lastImportedAvatarSlot = null;
            return null;
        }

        return _lastImportedAvatarSlot;
    }

    private static bool IsAvatarFinalized(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed)
            return false;

        var descriptor = avatarSlot.GetComponent<AvatarDescriptor>();
        if (descriptor?.IsFinalized.Value == true)
            return true;

        var draft = avatarSlot.GetComponent<AvatarDraft>();
        return draft?.IsFinalized.Value == true && draft.Descriptor.Target != null;
    }

    private bool TryEquipAvatar(Slot avatarSlot, out string message)
    {
        message = string.Empty;

        var localUserRoot = avatarSlot.World?.LocalUser?.Root ?? _targetSlot?.World?.LocalUser?.Root;
        if (localUserRoot == null)
        {
            message = "Local user root is not available.";
            return false;
        }

        var manager = localUserRoot.Slot.GetComponent<AvatarManager>() ?? localUserRoot.Slot.AttachComponent<AvatarManager>();
        manager.UserRoot.Target ??= localUserRoot;

        if (manager.CurrentAvatar.Target == avatarSlot)
        {
            message = "Avatar is already equipped.";
            return true;
        }

        if (!manager.EquipAvatar(avatarSlot))
        {
            message = "Avatar could not be equipped.";
            return false;
        }

        var avatarName = avatarSlot.SlotName?.Value;
        message = string.IsNullOrWhiteSpace(avatarName)
            ? "Avatar equipped."
            : $"Equipped {avatarName}.";
        return true;
    }
}
