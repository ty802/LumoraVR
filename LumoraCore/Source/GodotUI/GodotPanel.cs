// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.GodotUI;

/// <summary>
/// A panel container that can hold child UI elements.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotPanel : GodotUIElement
{
    /// <summary>
    /// Background color of the panel.
    /// </summary>
    public readonly Sync<color> BackgroundColor;

    /// <summary>
    /// Corner radius for rounded corners.
    /// </summary>
    public readonly Sync<float> CornerRadius;

    /// <summary>
    /// Border width.
    /// </summary>
    public readonly Sync<float> BorderWidth;

    /// <summary>
    /// Border color.
    /// </summary>
    public readonly Sync<color> BorderColor;

    public override void OnAwake()
    {
        base.OnAwake();

        BackgroundColor.OnChanged += _ => NotifyChanged();
        CornerRadius.OnChanged += _ => NotifyChanged();
        BorderWidth.OnChanged += _ => NotifyChanged();
        BorderColor.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        BackgroundColor.Value = new color(0.15f, 0.15f, 0.2f, 1f);
        CornerRadius.Value = 4f;
        BorderWidth.Value = 0f;
        BorderColor.Value = new color(0.3f, 0.3f, 0.4f, 1f);
    }
}
