// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.GodotUI;

/// <summary>
/// Anchor presets for UI positioning.
/// </summary>
public enum AnchorPreset
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    FullRect,
    TopWide,
    BottomWide,
    LeftWide,
    RightWide,
    Custom
}

/// <summary>
/// Base class for all Godot UI elements.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotUIElement : ImplementableComponent
{
    /// <summary>
    /// Anchor preset for quick positioning.
    /// </summary>
    public readonly Sync<AnchorPreset> Anchor;

    /// <summary>
    /// Custom anchor values (left, top, right, bottom) - 0 to 1.
    /// </summary>
    public readonly Sync<float4> AnchorRect;

    /// <summary>
    /// Offset from anchors in pixels (left, top, right, bottom).
    /// </summary>
    public readonly Sync<float4> OffsetRect;

    /// <summary>
    /// Minimum size of the element.
    /// </summary>
    public readonly Sync<float2> MinSize;

    /// <summary>
    /// Size flags for horizontal layout.
    /// </summary>
    public readonly Sync<int> SizeFlagsHorizontal;

    /// <summary>
    /// Size flags for vertical layout.
    /// </summary>
    public readonly Sync<int> SizeFlagsVertical;

    /// <summary>
    /// Whether the element is visible.
    /// </summary>
    public readonly Sync<bool> Visible;

    /// <summary>
    /// Modulate color (tint).
    /// </summary>
    public readonly Sync<color> Modulate;

    /// <summary>
    /// Path to existing Godot node within parent panel's scene.
    /// If set, hook adopts existing node instead of creating new one.
    /// </summary>
    public readonly Sync<string> SceneNodePath;

    /// <summary>
    /// Reference to parent panel (for scene node lookup).
    /// </summary>
    public readonly SyncRef<GodotUIPanel> ParentPanel;

    public override void OnAwake()
    {
        base.OnAwake();

        Anchor.OnChanged += _ => NotifyChanged();
        AnchorRect.OnChanged += _ => NotifyChanged();
        OffsetRect.OnChanged += _ => NotifyChanged();
        MinSize.OnChanged += _ => NotifyChanged();
        Visible.OnChanged += _ => NotifyChanged();
        Modulate.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        Anchor.Value = AnchorPreset.TopLeft;
        AnchorRect.Value = new float4(0, 0, 0, 0);
        OffsetRect.Value = new float4(0, 0, 100, 100);
        MinSize.Value = float2.Zero;
        SizeFlagsHorizontal.Value = 1;  // Fill
        SizeFlagsVertical.Value = 1;    // Fill
        Visible.Value = true;
        Modulate.Value = color.White;
        SceneNodePath.Value = "";
    }

    /// <summary>
    /// Set anchors and offsets to fill the entire parent.
    /// </summary>
    public void SetFullRect()
    {
        Anchor.Value = AnchorPreset.FullRect;
        AnchorRect.Value = new float4(0, 0, 1, 1);
        OffsetRect.Value = float4.Zero;
    }

    /// <summary>
    /// Set position and size using pixel coordinates.
    /// </summary>
    public void SetRect(float x, float y, float width, float height)
    {
        Anchor.Value = AnchorPreset.TopLeft;
        AnchorRect.Value = new float4(0, 0, 0, 0);
        OffsetRect.Value = new float4(x, y, x + width, y + height);
    }
}
