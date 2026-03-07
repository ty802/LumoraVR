// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Registers a permanent item in the user's root context menu.
///
/// Attach to any slot under the UserRoot. The ContextMenuSystem scans the
/// hierarchy for all RootContextMenuItem components each time the menu opens
/// and includes them in the root page, sorted by Priority.
///
/// Provides a data-driven way to add always-visible items without writing
/// a full ContextMenuItemSource subclass.
///
/// Example: adding a "Laser" toggle to every user session:
/// <code>
///   var item = slot.AttachComponent&lt;RootContextMenuItem&gt;();
///   item.Label.Value    = "Laser";
///   item.IconPath.Value = "res://Icons/laser.png";
///   item.IsToggle.Value = true;
///   item.Pressed        += _ => ToggleLaser();
/// </code>
/// </summary>
[ComponentCategory("UI/Context Menu")]
public class RootContextMenuItem : Component
{
    // ── Synced settings ────────────────────────────────────────────────────────

    public Sync<string> Label     { get; private set; }
    public Sync<string> IconPath  { get; private set; }
    public Sync<bool>   IsEnabled { get; private set; }
    public Sync<bool>   IsToggle  { get; private set; }
    public Sync<bool>   IsToggled { get; private set; }

    /// <summary>Sort priority. Items with higher Priority appear earlier in the menu.</summary>
    public Sync<int>    Priority  { get; private set; }

    // ── Runtime-only ──────────────────────────────────────────────────────────

    /// <summary>
    /// Optional sub-page. If set, pressing this item navigates into SubPage
    /// instead of firing Pressed.
    /// </summary>
    public ContextMenuPage? SubPage { get; set; }

    /// <summary>Fired when the item is pressed (and SubPage is null).</summary>
    public event Action<ContextMenuItem>? Pressed;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnAwake()
    {
        base.OnAwake();
        Label     = new Sync<string>(this, "");
        IconPath  = new Sync<string>(this, "");
        IsEnabled = new Sync<bool>(this, true);
        IsToggle  = new Sync<bool>(this, false);
        IsToggled = new Sync<bool>(this, false);
        Priority  = new Sync<int>(this, 0);
    }

    // ── Conversion ────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a ContextMenuItem from this component's current state.
    /// Called by ContextMenuSystem when constructing the root page.
    /// </summary>
    public ContextMenuItem ToMenuItem()
    {
        var item = new ContextMenuItem
        {
            Label     = Label.Value,
            IconPath  = string.IsNullOrEmpty(IconPath.Value) ? null : IconPath.Value,
            IsEnabled = IsEnabled.Value,
            IsToggle  = IsToggle.Value,
            IsToggled = IsToggled.Value,
            SubPage   = SubPage,
        };

        if (SubPage == null)
        {
            item.OnPressed = pressedItem =>
            {
                if (IsToggle.Value)
                    IsToggled.Value = !IsToggled.Value;
                Pressed?.Invoke(pressedItem);
            };
        }

        return item;
    }

    /// <summary>
    /// Invoke from code to fire Pressed and update toggle state.
    /// Useful for testing or scripted activation.
    /// </summary>
    public void Invoke()
    {
        if (!IsEnabled.Value) return;
        if (IsToggle.Value) IsToggled.Value = !IsToggled.Value;
        Pressed?.Invoke(ToMenuItem());
    }
}
