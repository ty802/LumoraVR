// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.GodotUI;

/// <summary>
/// Scroll bar visibility mode.
/// </summary>
public enum ScrollMode
{
    Disabled,
    Auto,
    AlwaysShow,
    AlwaysHide
}

/// <summary>
/// A scrollable container for UI elements.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotScrollContainer : GodotUIElement
{
    /// <summary>
    /// Horizontal scroll value (0-1).
    /// </summary>
    public readonly Sync<float> ScrollHorizontal;

    /// <summary>
    /// Vertical scroll value (0-1).
    /// </summary>
    public readonly Sync<float> ScrollVertical;

    /// <summary>
    /// Horizontal scroll bar visibility.
    /// </summary>
    public readonly Sync<ScrollMode> HorizontalScrollMode;

    /// <summary>
    /// Vertical scroll bar visibility.
    /// </summary>
    public readonly Sync<ScrollMode> VerticalScrollMode;

    /// <summary>
    /// Whether to follow focus (scroll to focused element).
    /// </summary>
    public readonly Sync<bool> FollowFocus;

    /// <summary>
    /// Scroll deadzone in pixels.
    /// </summary>
    public readonly Sync<int> ScrollDeadzone;

    public override void OnAwake()
    {
        base.OnAwake();

        ScrollHorizontal.OnChanged += _ => NotifyChanged();
        ScrollVertical.OnChanged += _ => NotifyChanged();
        HorizontalScrollMode.OnChanged += _ => NotifyChanged();
        VerticalScrollMode.OnChanged += _ => NotifyChanged();
        FollowFocus.OnChanged += _ => NotifyChanged();
        ScrollDeadzone.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        ScrollHorizontal.Value = 0f;
        ScrollVertical.Value = 0f;
        HorizontalScrollMode.Value = ScrollMode.Auto;
        VerticalScrollMode.Value = ScrollMode.Auto;
        FollowFocus.Value = true;
        ScrollDeadzone.Value = 0;
    }

    /// <summary>
    /// Scroll to top.
    /// </summary>
    public void ScrollToTop()
    {
        ScrollVertical.Value = 0f;
    }

    /// <summary>
    /// Scroll to bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        ScrollVertical.Value = 1f;
    }
}
