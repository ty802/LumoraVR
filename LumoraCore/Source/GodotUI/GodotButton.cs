// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.GodotUI;

/// <summary>
/// A clickable button UI element.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotButton : GodotUIElement
{
    /// <summary>
    /// Button text.
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
    /// Normal background color.
    /// </summary>
    public readonly Sync<color> NormalColor;

    /// <summary>
    /// Background color when hovered.
    /// </summary>
    public readonly Sync<color> HoverColor;

    /// <summary>
    /// Background color when pressed.
    /// </summary>
    public readonly Sync<color> PressedColor;

    /// <summary>
    /// Background color when disabled.
    /// </summary>
    public readonly Sync<color> DisabledColor;

    /// <summary>
    /// Whether the button is disabled.
    /// </summary>
    public readonly Sync<bool> Disabled;

    /// <summary>
    /// Corner radius for rounded corners.
    /// </summary>
    public readonly Sync<float> CornerRadius;

    /// <summary>
    /// Event fired when button is pressed.
    /// </summary>
    public event Action? OnPressed;

    public override void OnAwake()
    {
        base.OnAwake();

        Text.OnChanged += _ => NotifyChanged();
        FontSize.OnChanged += _ => NotifyChanged();
        FontColor.OnChanged += _ => NotifyChanged();
        NormalColor.OnChanged += _ => NotifyChanged();
        HoverColor.OnChanged += _ => NotifyChanged();
        PressedColor.OnChanged += _ => NotifyChanged();
        DisabledColor.OnChanged += _ => NotifyChanged();
        Disabled.OnChanged += _ => NotifyChanged();
        CornerRadius.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        Text.Value = "Button";
        FontSize.Value = 16;
        FontColor.Value = color.White;
        NormalColor.Value = new color(0.25f, 0.25f, 0.3f, 1f);
        HoverColor.Value = new color(0.35f, 0.35f, 0.4f, 1f);
        PressedColor.Value = new color(0.2f, 0.2f, 0.25f, 1f);
        DisabledColor.Value = new color(0.15f, 0.15f, 0.15f, 1f);
        Disabled.Value = false;
        CornerRadius.Value = 4f;
    }

    /// <summary>
    /// Called by hook when button is pressed.
    /// </summary>
    public void TriggerPressed()
    {
        if (!Disabled.Value)
        {
            OnPressed?.Invoke();
        }
    }
}
