// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.GodotUI;

/// <summary>
/// Loads a Godot .tscn scene and renders it to a 3D quad.
/// Use this to display pre-built Godot UI scenes in-world.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotSceneCanvas : ImplementableComponent
{
    /// <summary>
    /// Path to the .tscn scene file (e.g., "res://UI/MyPanel.tscn").
    /// </summary>
    public readonly Sync<string> ScenePath;

    /// <summary>
    /// Size of the viewport in pixels.
    /// </summary>
    public readonly Sync<float2> Size;

    /// <summary>
    /// Pixels per unit (affects how large the UI appears in world space).
    /// </summary>
    public readonly Sync<float> PixelsPerUnit;

    /// <summary>
    /// Whether the canvas is interactive (receives input).
    /// </summary>
    public readonly Sync<bool> Interactive;

    /// <summary>
    /// Whether the background is transparent.
    /// </summary>
    public readonly Sync<bool> TransparentBackground;

    /// <summary>
    /// Event fired when scene is loaded. Hook can use this to notify components.
    /// </summary>
    public event Action<GodotSceneCanvas>? OnSceneLoaded;

    public override void OnAwake()
    {
        base.OnAwake();

        ScenePath.OnChanged += _ => NotifyChanged();
        Size.OnChanged += _ => NotifyChanged();
        PixelsPerUnit.OnChanged += _ => NotifyChanged();
        Interactive.OnChanged += _ => NotifyChanged();
        TransparentBackground.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        ScenePath.Value = "";
        Size.Value = new float2(800, 600);
        PixelsPerUnit.Value = 1000f;
        Interactive.Value = true;
        TransparentBackground.Value = true;
    }

    /// <summary>
    /// Called by hook when scene is loaded.
    /// </summary>
    public void NotifySceneLoaded()
    {
        OnSceneLoaded?.Invoke(this);
    }
}
