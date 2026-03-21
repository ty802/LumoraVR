// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.GodotUI;

/// <summary>
/// Horizontal text alignment.
/// </summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Fill
}

/// <summary>
/// Vertical text alignment.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// A text label UI element.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotLabel : GodotUIElement
{
    /// <summary>
    /// The text to display.
    /// </summary>
    public readonly Sync<string> Text;

    /// <summary>
    /// Font size in pixels.
    /// </summary>
    public readonly Sync<int> FontSize;

    /// <summary>
    /// Text color.
    /// </summary>
    public readonly Sync<color> FontColor;

    /// <summary>
    /// Horizontal text alignment.
    /// </summary>
    public readonly Sync<HorizontalAlignment> HAlign;

    /// <summary>
    /// Vertical text alignment.
    /// </summary>
    public readonly Sync<VerticalAlignment> VAlign;

    /// <summary>
    /// Whether to auto-wrap text.
    /// </summary>
    public readonly Sync<bool> AutoWrap;

    /// <summary>
    /// Whether to clip text that overflows.
    /// </summary>
    public readonly Sync<bool> ClipText;

    public override void OnAwake()
    {
        base.OnAwake();

        Text.OnChanged += _ => NotifyChanged();
        FontSize.OnChanged += _ => NotifyChanged();
        FontColor.OnChanged += _ => NotifyChanged();
        HAlign.OnChanged += _ => NotifyChanged();
        VAlign.OnChanged += _ => NotifyChanged();
        AutoWrap.OnChanged += _ => NotifyChanged();
        ClipText.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        Text.Value = "Label";
        FontSize.Value = 16;
        FontColor.Value = color.White;
        HAlign.Value = HorizontalAlignment.Left;
        VAlign.Value = VerticalAlignment.Top;
        AutoWrap.Value = false;
        ClipText.Value = false;
    }
}
