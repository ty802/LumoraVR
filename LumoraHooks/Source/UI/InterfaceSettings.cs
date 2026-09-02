// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core;

namespace Lumora.Source.Godot.UI;

// Platform-side view of the interface settings. The VALUES live in EngineSettings (core): the settings
// screen is a core component and cannot see this assembly, and two independent stores meant the mouse
// sliders moved GodotMouseDriver but not DesktopCameraController, which read this one. Everything here
// forwards, so there is a single source of truth and one Changed event to hang the cursor redraw off. -xlinka
public static class InterfaceSettings
{
    public enum ReticleStyle
    {
        Ring,
        Dot,
        Crosshair,
        Off
    }

    // Hover tint is platform-only: nothing persists or configures it yet, so it stays a local default
    // rather than pretending to be a setting.
    private static Color _reticleHoverColor = new(0.4f, 1f, 0.4f, 0.85f);

    public static event Action Changed = null!;

    static InterfaceSettings()
    {
        EngineSettings.Changed += () => Changed?.Invoke();
    }

    public static float ReticleSize
    {
        get => EngineSettings.ReticleSize;
        set => EngineSettings.ReticleSize = value;
    }

    public static float ReticleThickness
    {
        get => EngineSettings.ReticleThickness;
        set => EngineSettings.ReticleThickness = value;
    }

    // No user-facing control yet; the drawer reads it, so it stays a constant white rather than a
    // slider that writes nowhere.
    public static Color ReticleColor => new(1f, 1f, 1f, 0.6f);

    public static Color ReticleHoverColor
    {
        get => _reticleHoverColor;
        set { _reticleHoverColor = value; Changed?.Invoke(); }
    }

    public static ReticleStyle Style
    {
        get => EngineSettings.ReticleStyle switch
        {
            EngineSettings.ReticleShape.Dot => ReticleStyle.Dot,
            EngineSettings.ReticleShape.Crosshair => ReticleStyle.Crosshair,
            EngineSettings.ReticleShape.Off => ReticleStyle.Off,
            _ => ReticleStyle.Ring,
        };
        set => EngineSettings.ReticleStyle = value switch
        {
            ReticleStyle.Dot => EngineSettings.ReticleShape.Dot,
            ReticleStyle.Crosshair => EngineSettings.ReticleShape.Crosshair,
            ReticleStyle.Off => EngineSettings.ReticleShape.Off,
            _ => EngineSettings.ReticleShape.Ring,
        };
    }

    // 1.0 = engine default
    public static float MouseSensitivity
    {
        get => EngineSettings.MouseSensitivity;
        set => EngineSettings.MouseSensitivity = value;
    }

    // 0 = raw input, recommended at high refresh rates
    public static float MouseSmoothing
    {
        get => EngineSettings.MouseSmoothing;
        set => EngineSettings.MouseSmoothing = value;
    }
}
