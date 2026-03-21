// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.GodotUI;

/// <summary>
/// Root component for Godot UI. Creates a SubViewport that renders UI to a texture.
/// The texture is then displayed on a 3D quad in the world.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotUICanvas : ImplementableComponent
{
    /// <summary>
    /// Size of the UI canvas in pixels.
    /// </summary>
    public readonly Sync<float2> Size;

    /// <summary>
    /// Pixels per unit (affects how large the UI appears in world space).
    /// Higher values = smaller UI in world.
    /// </summary>
    public readonly Sync<float> PixelsPerUnit;

    /// <summary>
    /// Whether the canvas is interactive (receives input).
    /// </summary>
    public readonly Sync<bool> Interactive;

    /// <summary>
    /// Background color of the canvas.
    /// </summary>
    public readonly Sync<color> BackgroundColor;

    /// <summary>
    /// Whether the background is transparent.
    /// </summary>
    public readonly Sync<bool> TransparentBackground;

    public override void OnAwake()
    {
        base.OnAwake();

        Size.OnChanged += _ => NotifyChanged();
        PixelsPerUnit.OnChanged += _ => NotifyChanged();
        BackgroundColor.OnChanged += _ => NotifyChanged();
        TransparentBackground.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = new float2(800, 600);
        PixelsPerUnit.Value = 1000f;  // 1000 pixels = 1 world unit
        Interactive.Value = true;
        BackgroundColor.Value = new color(0.1f, 0.1f, 0.15f, 1f);
        TransparentBackground.Value = false;
    }
}
