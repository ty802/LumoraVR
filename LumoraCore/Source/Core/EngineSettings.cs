// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

/// <summary>
/// Engine-owned user settings. The settings UI writes these; subsystems read
/// them directly (mouse look) or the platform layer subscribes to Changed and
/// applies what only it can (vsync, window mode, render scale, audio bus).
/// Persisted as JSON in the user's application data folder.
/// </summary>
public static class EngineSettings
{
    public static event Action? Changed;

    private static bool _dirty;
    private static bool _loaded;

    // INPUT

    private static float _mouseSensitivity = 1f;
    public static float MouseSensitivity
    {
        get => _mouseSensitivity;
        set => SetValue(ref _mouseSensitivity, System.Math.Clamp(value, 0.05f, 10f));
    }

    private static float _mouseSmoothing;
    public static float MouseSmoothing
    {
        get => _mouseSmoothing;
        set => SetValue(ref _mouseSmoothing, System.Math.Clamp(value, 0f, 0.95f));
    }

    // Noclip flight speed in m/s. Only affects the noclip locomotion module.
    private static float _noclipSpeed = 6f;
    public static float NoclipSpeed
    {
        get => _noclipSpeed;
        set => SetValue(ref _noclipSpeed, System.Math.Clamp(value, 1f, 30f));
    }

    // AUDIO

    private static float _masterVolume = 1f;
    public static float MasterVolume
    {
        get => _masterVolume;
        set => SetValue(ref _masterVolume, System.Math.Clamp(value, 0f, 1f));
    }

    // VIDEO

    private static bool _vsync = true;
    public static bool VSync
    {
        get => _vsync;
        set => SetValue(ref _vsync, value);
    }

    /// <summary>Frame cap; 0 = uncapped.</summary>
    private static int _maxFps;
    public static int MaxFps
    {
        get => _maxFps;
        set => SetValue(ref _maxFps, value <= 0 ? 0 : System.Math.Clamp(value, 30, 480));
    }

    private static bool _fullscreen;
    public static bool Fullscreen
    {
        get => _fullscreen;
        set => SetValue(ref _fullscreen, value);
    }

    private static float _renderScale = 1f;
    public static float RenderScale
    {
        get => _renderScale;
        set => SetValue(ref _renderScale, System.Math.Clamp(value, 0.5f, 2f));
    }

    // PERSISTENCE - values live-apply for preview; they are written to the shared binary config
    // store (Settings -> config.dat) only on Commit, which the exit screen's "Exit and Save" calls.

    private const string KeyMouseSensitivity = "Engine.Input.MouseSensitivity";
    private const string KeyMouseSmoothing = "Engine.Input.MouseSmoothing";
    private const string KeyNoclipSpeed = "Engine.Input.NoclipSpeed";
    private const string KeyMasterVolume = "Engine.Audio.MasterVolume";
    private const string KeyVSync = "Engine.Video.VSync";
    private const string KeyMaxFps = "Engine.Video.MaxFps";
    private const string KeyFullscreen = "Engine.Video.Fullscreen";
    private const string KeyRenderScale = "Engine.Video.RenderScale";

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            _mouseSensitivity = System.Math.Clamp(Settings.ReadValue(KeyMouseSensitivity, _mouseSensitivity), 0.05f, 10f);
            _mouseSmoothing = System.Math.Clamp(Settings.ReadValue(KeyMouseSmoothing, _mouseSmoothing), 0f, 0.95f);
            _noclipSpeed = System.Math.Clamp(Settings.ReadValue(KeyNoclipSpeed, _noclipSpeed), 1f, 30f);
            _masterVolume = System.Math.Clamp(Settings.ReadValue(KeyMasterVolume, _masterVolume), 0f, 1f);
            _vsync = Settings.ReadValue(KeyVSync, _vsync);
            int fps = Settings.ReadValue(KeyMaxFps, _maxFps);
            _maxFps = fps <= 0 ? 0 : System.Math.Clamp(fps, 30, 480);
            _fullscreen = Settings.ReadValue(KeyFullscreen, _fullscreen);
            _renderScale = System.Math.Clamp(Settings.ReadValue(KeyRenderScale, _renderScale), 0.5f, 2f);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"EngineSettings: failed to load: {ex.Message}");
        }

        _dirty = false;
    }

    /// <summary>True if values have changed (live-applied) but not yet committed to disk.</summary>
    public static bool HasUnsavedChanges => _dirty;

    /// <summary>
    /// Persist current values. Changes apply live for preview but are only saved here -
    /// the exit screen's "Exit and Save" calls this.
    /// </summary>
    public static void Commit()
    {
        try
        {
            Settings.WriteValue(KeyMouseSensitivity, _mouseSensitivity);
            Settings.WriteValue(KeyMouseSmoothing, _mouseSmoothing);
            Settings.WriteValue(KeyNoclipSpeed, _noclipSpeed);
            Settings.WriteValue(KeyMasterVolume, _masterVolume);
            Settings.WriteValue(KeyVSync, _vsync);
            Settings.WriteValue(KeyMaxFps, _maxFps);
            Settings.WriteValue(KeyFullscreen, _fullscreen);
            Settings.WriteValue(KeyRenderScale, _renderScale);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"EngineSettings: failed to save: {ex.Message}");
        }

        _dirty = false;
    }

    private static void SetValue<T>(ref T field, T value) where T : IEquatable<T>
    {
        if (field.Equals(value))
            return;
        field = value;
        _dirty = true;
        Changed?.Invoke();
    }
}

