// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.GodotUI;

namespace Lumora.Godot.Hooks.GodotUI;

#nullable enable

/// <summary>
/// Godot hook for GodotPanel.
/// Creates a Panel control with background color and border styling.
/// </summary>
public class GodotPanelHook : GodotUIElementHook<GodotPanel>
{
    private Panel? _panel;
    private StyleBoxFlat? _style;

    public static IHook<GodotPanel> Constructor()
    {
        return new GodotPanelHook();
    }

    protected override Control CreateControl()
    {
        _panel = new Panel();
        _style = new StyleBoxFlat();
        _panel.AddThemeStyleboxOverride("panel", _style);
        ApplyPanelProperties();
        return _panel;
    }

    public override void ApplyChanges()
    {
        base.ApplyChanges();
        ApplyPanelProperties();
    }

    private void ApplyPanelProperties()
    {
        if (_style == null) return;

        var bg = Owner.BackgroundColor.Value;
        _style.BgColor = new Color(bg.r, bg.g, bg.b, bg.a);

        var radius = (int)Owner.CornerRadius.Value;
        _style.CornerRadiusTopLeft = radius;
        _style.CornerRadiusTopRight = radius;
        _style.CornerRadiusBottomLeft = radius;
        _style.CornerRadiusBottomRight = radius;

        var borderWidth = (int)Owner.BorderWidth.Value;
        _style.BorderWidthLeft = borderWidth;
        _style.BorderWidthTop = borderWidth;
        _style.BorderWidthRight = borderWidth;
        _style.BorderWidthBottom = borderWidth;

        var border = Owner.BorderColor.Value;
        _style.BorderColor = new Color(border.r, border.g, border.b, border.a);
    }

    public override void Destroy(bool destroyingWorld)
    {
        _style?.Dispose();
        _style = null;
        _panel = null;
        base.Destroy(destroyingWorld);
    }
}
