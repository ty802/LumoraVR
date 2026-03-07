// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Godot.UI;

/// <summary>
/// Partial class: avatar setup pedestal button logic and pedestal spawning.
/// Geometry helpers are in <see cref="AvatarSetupPedestalHelper"/>.
/// </summary>
public partial class ImportDialog
{
    private void UpdateAvatarSetupButton()
    {
        if (_btnAvatarSetup == null) return;

        var hasAvatar = _lastImportedAvatarSlot != null && !_lastImportedAvatarSlot.IsDestroyed;
        _btnAvatarSetup.Visible  = hasAvatar;
        _btnAvatarSetup.Disabled = _isImporting || !hasAvatar;

        if (!hasAvatar)
        {
            _btnAvatarSetup.Text = "Setup Imported Avatar";
            return;
        }

        var avatarName = _lastImportedAvatarSlot.SlotName?.Value;
        if (string.IsNullOrWhiteSpace(avatarName))
            avatarName = "Avatar";
        _btnAvatarSetup.Text = $"Setup {avatarName}";
    }

    private void OnAvatarSetupPressed()
    {
        if (_isImporting) return;

        if (_lastImportedAvatarSlot == null || _lastImportedAvatarSlot.IsDestroyed)
        {
            SetCompletedStatus("No imported avatar available to setup.", success: false);
            _lastImportedAvatarSlot = null;
            UpdateAvatarSetupButton();
            return;
        }

        try
        {
            if (_lastAvatarSetupPedestal != null && !_lastAvatarSetupPedestal.IsDestroyed)
            {
                SetSubtitle("Avatar setup pedestal already spawned");
                SetCompletedStatus("Use the existing pedestal and markers. Re-import the avatar to create a fresh setup station.");
                return;
            }

            var pedestal = SpawnAvatarSetupPedestal(_lastImportedAvatarSlot);
            if (pedestal == null)
            {
                SetCompletedStatus("Failed to spawn avatar setup pedestal.", success: false);
                return;
            }

            _lastAvatarSetupPedestal = pedestal;
            SetSubtitle("Avatar setup pedestal spawned");
            SetCompletedStatus("Move the wrist/viewpoint markers (balls). Arrows show facing direction.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ImportDialog: Failed to spawn avatar setup pedestal: {ex.Message}");
            SetCompletedStatus($"Avatar setup failed: {ex.Message}", success: false);
        }
    }

    /// <summary>
    /// Spawn the avatar setup pedestal near the imported avatar, populate it with
    /// bone-aligned grab markers and an AvatarMount slot.
    /// </summary>
    private Slot SpawnAvatarSetupPedestal(Slot avatarSlot)
    {
        if (avatarSlot == null || avatarSlot.IsDestroyed) return null;

        var setupParent = _targetSlot ?? avatarSlot.Parent ?? avatarSlot.World?.RootSlot;
        if (setupParent == null) return null;

        // Remove any previous pedestal with the same name.
        var pedestalName = $"{avatarSlot.SlotName.Value}_SetupPedestal";
        var existing = setupParent.FindChild(pedestalName, recursive: false);
        if (existing != null && !existing.IsDestroyed)
            existing.Destroy();

        var avatarGlobalPos = avatarSlot.GlobalPosition;
        var avatarGlobalRot = avatarSlot.GlobalRotation;

        // ── Pedestal root ────────────────────────────────────────────────────
        var pedestalRoot = setupParent.AddSlot(pedestalName);
        pedestalRoot.GlobalPosition = avatarGlobalPos;
        pedestalRoot.GlobalRotation = floatQ.Identity;
        pedestalRoot.AttachComponent<Grabbable>();

        var pedestalCollider = pedestalRoot.AttachComponent<BoxCollider>();
        pedestalCollider.Size.Value   = new float3(1.0f, 0.2f, 1.0f);
        pedestalCollider.Offset.Value = new float3(0f,   0.1f, 0f);

        // ── Pedestal geometry (Base / Column / Top) ──────────────────────────
        AvatarSetupPedestalHelper.CreatePedestalPart(pedestalRoot, "Base",   new float3(0f, 0.04f, 0f), radius: 0.42f, height: 0.08f, new colorHDR(0.12f, 0.14f, 0.20f, 1f));
        AvatarSetupPedestalHelper.CreatePedestalPart(pedestalRoot, "Column", new float3(0f, 0.22f, 0f), radius: 0.10f, height: 0.28f, new colorHDR(0.20f, 0.24f, 0.34f, 1f));
        AvatarSetupPedestalHelper.CreatePedestalPart(pedestalRoot, "Top",    new float3(0f, 0.36f, 0f), radius: 0.33f, height: 0.05f, new colorHDR(0.28f, 0.34f, 0.48f, 1f));

        // ── Avatar mount ────────────────────────────────────────────────────
        var avatarMount = pedestalRoot.AddSlot("AvatarMount");
        avatarMount.LocalPosition.Value = new float3(0f, 0.385f, 0f);
        avatarMount.LocalRotation.Value = floatQ.Identity;

        avatarSlot.SetParent(avatarMount, preserveGlobalTransform: true);
        avatarSlot.GlobalRotation = avatarGlobalRot;
        avatarSlot.GlobalPosition = pedestalRoot.GlobalPosition + new float3(0f, 0.385f, 0f);

        // ── Setup markers ───────────────────────────────────────────────────
        var markersRoot = avatarSlot.FindChild(AvatarSetupMarkersRootName, recursive: false);
        if (markersRoot != null && !markersRoot.IsDestroyed)
            markersRoot.Destroy();

        markersRoot = avatarSlot.AddSlot(AvatarSetupMarkersRootName);
        markersRoot.LocalPosition.Value = float3.Zero;
        markersRoot.LocalRotation.Value = floatQ.Identity;

        var rig           = avatarSlot.GetComponentInChildren<BipedRig>();
        var leftHandBone  = rig?.TryGetBone(BodyNode.LeftHand);
        var rightHandBone = rig?.TryGetBone(BodyNode.RightHand);
        var headBone      = rig?.TryGetBone(BodyNode.Head);

        var leftMarker  = AvatarSetupPedestalHelper.CreateSetupMarker(markersRoot, "LeftWrist",  new colorHDR(0.25f, 0.65f, 1.0f, 0.9f), new float3(-0.22f, 1.25f, 0.08f));
        var rightMarker = AvatarSetupPedestalHelper.CreateSetupMarker(markersRoot, "RightWrist", new colorHDR(1.0f,  0.45f, 0.25f, 0.9f), new float3( 0.22f, 1.25f, 0.08f));
        var viewMarker  = AvatarSetupPedestalHelper.CreateSetupMarker(markersRoot, "Viewpoint",  new colorHDR(0.45f, 1.0f,  0.65f, 0.9f), new float3( 0f,    1.58f, 0.06f));

        AvatarSetupPedestalHelper.ApplyMarkerFromBone(avatarSlot, leftMarker,  leftHandBone,  float3.Backward);
        AvatarSetupPedestalHelper.ApplyMarkerFromBone(avatarSlot, rightMarker, rightHandBone, float3.Backward);
        AvatarSetupPedestalHelper.ApplyMarkerFromBone(avatarSlot, viewMarker,  headBone,      float3.Backward, forwardOffset: 0.06f);

        return pedestalRoot;
    }
}
