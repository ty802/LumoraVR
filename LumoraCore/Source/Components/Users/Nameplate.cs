// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// User nameplate data + behavior. Sets sync field state from the target user
// and keeps the slot positioned above the head.
//
// NOTE: not the live nameplate. The shipping name badge is composed by
// AvatarEquipManager.EnsureNameBadge (mesh text + NameBadgeDriver) and
// positioned by PositionAtUser (anchored at UserRoot.HeadSlot). Nothing
// attaches this component; it stays only as the data-driven reference shape.
//
// Visual rendering is intentionally not hooked. Nameplates render as standard
// mesh + UI primitives (text + image components on child slots) routed through
// MeshRenderer / material connectors, not a dedicated platform connector. The
// earlier NameplateHook violated that pattern by loading a Godot .tscn into a
// SubViewport, so it was removed.
// To restore visuals: in OnInit, build the slot hierarchy with Helio UI
// (Canvas + RectTransform + Image background + Text label) and let the
// MeshRenderer hook do the actual rendering. - xlinka
[ComponentCategory("Users")]
public class Nameplate : ImplementableComponent
{
    public readonly SyncRef<User> TargetUser = new();

    public readonly Sync<string> DisplayName = new();

    // white = normal logged in, grey = not logged in, colored = patreon tier
    public readonly Sync<color> RimColor = new();

    public readonly Sync<bool> IsLoggedIn = new();

    // empty string means no patreon tier
    public readonly Sync<string> PatreonColorHex = new();

    // world units
    public readonly Sync<float2> Size = new();

    public readonly Sync<float> HeadOffset = new();

    public readonly Sync<bool> Billboard = new();

    // Patreon tier colors
    public static readonly color ColorNotLoggedIn = new color(0.5f, 0.5f, 0.5f, 1f); // Grey
    public static readonly color ColorNormal = new color(1f, 1f, 1f, 1f); // White
    public static readonly color ColorEnthusiast = new color(0f, 1f, 0f, 1f); // Green #00FF00
    public static readonly color ColorCollaborator = new color(0.255f, 0.412f, 0.882f, 1f); // Royal Blue #4169E1
    public static readonly color ColorInsider = new color(0.58f, 0f, 0.827f, 1f); // Purple #9400D3
    public static readonly color ColorVisionary = new color(1f, 0.843f, 0f, 1f); // Gold #FFD700

    public override void OnInit()
    {
        base.OnInit();

        RimColor.Value = ColorNormal;
        IsLoggedIn.Value = true;
        Size.Value = new float2(0.45f, 0.12f); // 45cm x 12cm default
        HeadOffset.Value = 0.35f; // above head, matching the live auto badge
        Billboard.Value = true;

        TargetUser.OnChanged += _ => UpdateFromUser();
        IsLoggedIn.OnChanged += _ => UpdateRimColor();
        PatreonColorHex.OnChanged += _ => UpdateRimColor();
        DisplayName.OnChanged += _ => NotifyChanged();
        RimColor.OnChanged += _ => NotifyChanged();
        Size.OnChanged += _ => NotifyChanged();
    }

    private User _subscribedUser = null!;

    // called on authority when creating the nameplate; clients receive TargetUser via sync and
    // UpdateFromUser handles subscription
    public void Initialize(User user)
    {
        TargetUser.Target = user;
        Logging.Logger.Log($"Nameplate: Initialized for user '{user?.UserName?.Value ?? "(null)"}' RefID={user?.ReferenceID}");
        // UpdateFromUser is called via TargetUser.OnChanged
    }

    private void OnUserNameChanged(IChangeable _)
    {
        UpdateFromUser();
    }

    private void UpdateFromUser()
    {
        var user = TargetUser.Target;
        if (user == null)
        {
            if (_subscribedUser != null)
            {
                _subscribedUser.UserName.Changed -= OnUserNameChanged;
                _subscribedUser = null!;
            }
            DisplayName.Value = "";
            return;
        }

        // handles both Initialize() calls (authority) and sync receives (client)
        if (_subscribedUser != user)
        {
            if (_subscribedUser != null)
            {
                _subscribedUser.UserName.Changed -= OnUserNameChanged;
            }
            _subscribedUser = user;
            user.UserName.Changed += OnUserNameChanged;
            Logging.Logger.Log($"Nameplate: Subscribed to UserName changes for '{user.UserName.Value ?? "(null)"}' RefID={user.ReferenceID}");
        }

        var newName = user.UserName.Value;
        if (string.IsNullOrEmpty(newName))
        {
            newName = "Unknown";
        }

        if (DisplayName.Value != newName)
        {
            Logging.Logger.Log($"Nameplate: DisplayName changed from '{DisplayName.Value}' to '{newName}'");
            DisplayName.Value = newName;
        }

        IsLoggedIn.Value = !string.IsNullOrEmpty(user.UserID.Value);

        // Get patreon color from user metadata (if available)
        // For now, we'll use empty string until patreon data is synced
        // PatreonColorHex will be set by the server/cloud when user data is fetched

        UpdateRimColor();
    }

    private void UpdateRimColor()
    {
        if (!IsLoggedIn.Value)
        {
            RimColor.Value = ColorNotLoggedIn;
            return;
        }

        var hexColor = PatreonColorHex.Value;
        if (string.IsNullOrEmpty(hexColor) || hexColor == "#FFFFFF")
        {
            RimColor.Value = ColorNormal;
            return;
        }

        RimColor.Value = ParseHexColor(hexColor);
    }

    public void SetPatreonColor(string hexColor)
    {
        PatreonColorHex.Value = hexColor ?? "";
    }

    private static color ParseHexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return ColorNormal;

        hex = hex.TrimStart('#');
        if (hex.Length != 6) return ColorNormal;

        try
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return new color(r / 255f, g / 255f, b / 255f, 1f);
        }
        catch
        {
            return ColorNormal;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var user = TargetUser.Target;
        if (user != null && DisplayName.Value != user.UserName.Value)
        {
            DisplayName.Value = user.UserName.Value ?? "";
        }

        UpdateFollowTransform(user!);
    }

    private void UpdateFollowTransform(User user)
    {
        if (Slot == null) return;

        var userRoot = user?.UserRootRef.Target;
        var headSlot = userRoot?.HeadSlot ?? Slot.Parent;
        if (headSlot == null) return;

        var desiredPos = headSlot.GlobalPosition + float3.Up * HeadOffset.Value;
        Slot.GlobalPosition = desiredPos;
        Slot.GlobalRotation = floatQ.Identity;
    }

    public override void OnDestroy()
    {
        // Unsubscribe from user events
        if (_subscribedUser != null)
        {
            _subscribedUser.UserName.Changed -= OnUserNameChanged;
            _subscribedUser = null!;
        }

        base.OnDestroy();
    }
}
