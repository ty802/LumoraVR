// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.GodotUI;

namespace Lumora.Godot.Hooks.GodotUI;

#nullable enable

/// <summary>
/// Godot hook for GodotScrollContainer.
/// Creates a ScrollContainer control with configurable scroll behavior.
/// </summary>
public class GodotScrollContainerHook : GodotUIElementHook<GodotScrollContainer>
{
    private ScrollContainer? _scrollContainer;

    public static IHook<GodotScrollContainer> Constructor()
    {
        return new GodotScrollContainerHook();
    }

    protected override Control CreateControl()
    {
        _scrollContainer = new ScrollContainer();
        ApplyScrollProperties();
        return _scrollContainer;
    }

    public override void ApplyChanges()
    {
        base.ApplyChanges();
        ApplyScrollProperties();
    }

    private void ApplyScrollProperties()
    {
        var sc = _scrollContainer ?? _control as ScrollContainer;
        if (sc == null) return;

        sc.HorizontalScrollMode = Owner.HorizontalScrollMode.Value switch
        {
            ScrollMode.Disabled => ScrollContainer.ScrollMode.Disabled,
            ScrollMode.Auto => ScrollContainer.ScrollMode.Auto,
            ScrollMode.AlwaysShow => ScrollContainer.ScrollMode.ShowAlways,
            ScrollMode.AlwaysHide => ScrollContainer.ScrollMode.ShowNever,
            _ => ScrollContainer.ScrollMode.Auto
        };

        sc.VerticalScrollMode = Owner.VerticalScrollMode.Value switch
        {
            ScrollMode.Disabled => ScrollContainer.ScrollMode.Disabled,
            ScrollMode.Auto => ScrollContainer.ScrollMode.Auto,
            ScrollMode.AlwaysShow => ScrollContainer.ScrollMode.ShowAlways,
            ScrollMode.AlwaysHide => ScrollContainer.ScrollMode.ShowNever,
            _ => ScrollContainer.ScrollMode.Auto
        };

        sc.FollowFocus = Owner.FollowFocus.Value;
        sc.ScrollDeadzone = Owner.ScrollDeadzone.Value;

        // Apply scroll position (0-1 mapped to pixel range)
        if (sc.GetVScrollBar() is VScrollBar vbar && vbar.MaxValue > vbar.Page)
        {
            sc.ScrollVertical = (int)(Owner.ScrollVertical.Value * (vbar.MaxValue - vbar.Page));
        }

        if (sc.GetHScrollBar() is HScrollBar hbar && hbar.MaxValue > hbar.Page)
        {
            sc.ScrollHorizontal = (int)(Owner.ScrollHorizontal.Value * (hbar.MaxValue - hbar.Page));
        }
    }

    public override void Destroy(bool destroyingWorld)
    {
        _scrollContainer = null;
        base.Destroy(destroyingWorld);
    }
}
