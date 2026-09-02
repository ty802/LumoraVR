// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

// Engine-owned user settings. The settings UI writes these; subsystems read
// them directly (mouse look) or the platform layer subscribes to Changed and
// applies what only it can (vsync, window mode, render scale, audio bus).
// Persisted as JSON in the user's application data folder.
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

    // Preferred locomotion module by DisplayName ("Walk", "Blink", "Noclip", ...), set from the
    // Settings screen's locomotion picker. Empty means no preference - spawn uses the ordinary first-usable
    // pick. Applied at spawn/world join; a name that no longer matches a usable module (renamed, or denied
    // by the current world's permission gate) just falls through to that default instead of failing.
    private static string _preferredLocomotion = string.Empty;
    public static string PreferredLocomotion
    {
        get => _preferredLocomotion;
        set => SetValue(ref _preferredLocomotion, value ?? string.Empty);
    }

    // AVATAR

    // Calibrated standing/eye height in metres. Drives the avatar auto-rescale (AvatarIK reads
    // InputInterface.UserHeight, which is kept in sync with this). Default 1.75 m. -xlinka
    private static float _userHeight = 1.75f;
    public static float UserHeight
    {
        get => _userHeight;
        set => SetValue(ref _userHeight, System.Math.Clamp(value, 0.5f, 2.5f));
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

    private static int _maxFps;
    public static int MaxFps
    {
        get => _maxFps;
        set => SetValue(ref _maxFps, value <= 0 ? 0 : System.Math.Clamp(value, 30, 480));
    }

    // Frame cap applied while the window is unfocused or minimized; 0 = no background throttle. Caps a
    // loop that would otherwise free-run when the compositor stops blocking the swap. Ignored in VR
    // (the headset compositor owns frame timing). Never raises the rate above MaxFps.
    private static int _backgroundFps = 30;
    public static int BackgroundFps
    {
        get => _backgroundFps;
        set => SetValue(ref _backgroundFps, value <= 0 ? 0 : System.Math.Clamp(value, 5, 240));
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

    public static readonly int[] TextureSizeOptions = { 0, 2048, 1024, 512, 256 };

    // Longest edge, in pixels, that a URL-loaded texture is allowed to reach; 0 loads it at source
    // resolution. Providers fold this into their variant descriptor, so changing it swaps every
    // texture over to a different variant on the next change pass - no world reload, and textures
    // already resident at the new cap are simply shared rather than reloaded.
    //
    // Snapped to a generated bucket rather than free-form: a cap nobody generated a blob for would
    // silently fall back to the source and quietly do nothing, which is the worst kind of setting.
    private static int _maxTextureSize;
    public static int MaxTextureSize
    {
        get => _maxTextureSize;
        set => SetValue(ref _maxTextureSize, SnapTextureSize(value));
    }

    public static int SnapTextureSize(int value)
    {
        if (value <= 0)
            return 0;
        int best = 0;
        foreach (int option in TextureSizeOptions)
        {
            if (option > 0 && option <= value && option > best)
                best = option;
        }
        // Below the smallest bucket, clamp up to it rather than silently meaning "no cap".
        return best == 0 ? 256 : best;
    }

    public static string DescribeTextureSize(int value) => value <= 0 ? "Original" : $"{value} px";

    // Whether reflection probes render at all. Off leaves every probe in the world alone as data and
    // simply stops the renderer node existing, so glossy surfaces fall back to the sky. This is a real
    // frame-time lever: a probe in Always mode re-renders the scene from six directions.
    private static bool _reflectionsEnabled = true;
    public static bool ReflectionsEnabled
    {
        get => _reflectionsEnabled;
        set => SetValue(ref _reflectionsEnabled, value);
    }

    // There is deliberately no reflection-resolution setting. A probe's face size comes from the
    // renderer's reflection atlas, which is sized once when the renderer starts and has no runtime
    // setter, so a slider for it would move a number that changes nothing until the next launch.
    // On/off is the lever that genuinely exists, and it is a big one. -xlinka

    // Multiplier on every LOD switching distance. Above 1 keeps detailed levels alive further out
    // (prettier, heavier); below 1 drops to cheaper levels sooner. Applied by the LOD hooks when the
    // setting changes, not per frame.
    private static float _lodBias = 1f;
    public static float LodBias
    {
        get => _lodBias;
        set => SetValue(ref _lodBias, System.Math.Clamp(value, 0.25f, 4f));
    }

    // Screen-space error, in pixels, the renderer accepts before a mesh drops to a cheaper level of
    // itself. Imported meshes carry a continuous chain of index-reduced levels, so this is trading
    // triangles against a difference measured in fractions of a pixel: nothing vanishes, and the
    // coarsest level holds however far away it gets. 0 pins every mesh to full detail. Applied to the
    // live viewports on change; new viewports inherit the project default. -xlinka
    private static float _meshLodThreshold = 2f;
    public static float MeshLodThreshold
    {
        get => _meshLodThreshold;
        set => SetValue(ref _meshLodThreshold, value <= 0f ? 0f : System.Math.Clamp(value, 0.5f, 8f));
    }

    public static string DescribeMeshLodThreshold(float value) =>
        value <= 0f ? "Full" : $"{value:0.#} px";

    // There is deliberately no network tick rate here. The sync rate is a property of the SESSION, not
    // of the machine looking at it: a guest turning it down does not make the host send less, it just
    // desyncs that guest. It lives on WorldSettings, host-only, and the Session screen edits it. -xlinka

    // COMFORT

    public enum TurnStyle
    {
        Snap,
        Smooth,
    }

    // How the stick turns you. Read live by TurnSubmodule, which every smooth locomotion module owns.
    private static TurnStyle _turnMode = TurnStyle.Snap;
    public static TurnStyle TurnMode
    {
        get => _turnMode;
        set => SetEnum(ref _turnMode, value);
    }

    // Degrees per snap flick. Small angles are smoother but cost more flicks per turn; large ones are
    // the comfort option.
    private static float _snapTurnAngle = 45f;
    public static float SnapTurnAngle
    {
        get => _snapTurnAngle;
        set => SetValue(ref _snapTurnAngle, System.Math.Clamp(value, 10f, 90f));
    }

    // Degrees per second while the stick is held, in Smooth mode.
    private static float _smoothTurnSpeed = 90f;
    public static float SmoothTurnSpeed
    {
        get => _smoothTurnSpeed;
        set => SetValue(ref _smoothTurnSpeed, System.Math.Clamp(value, 30f, 360f));
    }

    // INTERFACE

    public enum ReticleShape
    {
        Ring,
        Dot,
        Crosshair,
        Off,
    }

    // Desktop cursor. The platform layer's cursor drawer reads these through InterfaceSettings, which
    // is a thin mirror over this so the Core settings UI can reach it at all (Core cannot see the
    // platform assembly). -xlinka
    private static ReticleShape _reticleStyle = ReticleShape.Ring;
    public static ReticleShape ReticleStyle
    {
        get => _reticleStyle;
        set => SetEnum(ref _reticleStyle, value);
    }

    private static float _reticleSize = 12f;
    public static float ReticleSize
    {
        get => _reticleSize;
        set => SetValue(ref _reticleSize, System.Math.Clamp(value, 2f, 48f));
    }

    private static float _reticleThickness = 2f;
    public static float ReticleThickness
    {
        get => _reticleThickness;
        set => SetValue(ref _reticleThickness, System.Math.Clamp(value, 1f, 8f));
    }

    // Multiplier on every directional light's shadow distance. Below 1 shrinks the cascade range,
    // which is the cheapest real win on the shadow pass; above 1 buys distant shadows back. Applied
    // by the light hooks on change, same shape as LodBias.
    private static float _shadowDistanceScale = 1f;
    public static float ShadowDistanceScale
    {
        get => _shadowDistanceScale;
        set => SetValue(ref _shadowDistanceScale, System.Math.Clamp(value, 0.25f, 4f));
    }

    // PERSISTENCE - values live-apply for preview; they are written to the shared binary config
    // store (Settings -> config.dat) only on Commit, which the exit screen's "Exit and Save" calls.

    private const string KeyMouseSensitivity = "Engine.Input.MouseSensitivity";
    private const string KeyMouseSmoothing = "Engine.Input.MouseSmoothing";
    private const string KeyNoclipSpeed = "Engine.Input.NoclipSpeed";
    private const string KeyPreferredLocomotion = "Engine.Input.PreferredLocomotion";
    private const string KeyUserHeight = "Engine.Avatar.UserHeight";
    private const string KeyMasterVolume = "Engine.Audio.MasterVolume";
    private const string KeyVSync = "Engine.Video.VSync";
    private const string KeyMaxFps = "Engine.Video.MaxFps";
    private const string KeyBackgroundFps = "Engine.Video.BackgroundFps";
    private const string KeyFullscreen = "Engine.Video.Fullscreen";
    private const string KeyRenderScale = "Engine.Video.RenderScale";
    private const string KeyMaxTextureSize = "Engine.Video.MaxTextureSize";
    private const string KeyReflectionsEnabled = "Engine.Video.ReflectionsEnabled";
    private const string KeyLodBias = "Engine.Video.LodBias";
    private const string KeyMeshLodThreshold = "Engine.Video.MeshLodThreshold";
    private const string KeyShadowDistanceScale = "Engine.Video.ShadowDistanceScale";
    private const string KeyTurnMode = "Engine.Comfort.TurnMode";
    private const string KeySnapTurnAngle = "Engine.Comfort.SnapTurnAngle";
    private const string KeySmoothTurnSpeed = "Engine.Comfort.SmoothTurnSpeed";
    private const string KeyReticleStyle = "Engine.Interface.ReticleStyle";
    private const string KeyReticleSize = "Engine.Interface.ReticleSize";
    private const string KeyReticleThickness = "Engine.Interface.ReticleThickness";

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
            _preferredLocomotion = Settings.ReadValue(KeyPreferredLocomotion, _preferredLocomotion) ?? string.Empty;
            _userHeight = System.Math.Clamp(Settings.ReadValue(KeyUserHeight, _userHeight), 0.5f, 2.5f);
            _masterVolume = System.Math.Clamp(Settings.ReadValue(KeyMasterVolume, _masterVolume), 0f, 1f);
            _vsync = Settings.ReadValue(KeyVSync, _vsync);
            int fps = Settings.ReadValue(KeyMaxFps, _maxFps);
            _maxFps = fps <= 0 ? 0 : System.Math.Clamp(fps, 30, 480);
            int bgFps = Settings.ReadValue(KeyBackgroundFps, _backgroundFps);
            _backgroundFps = bgFps <= 0 ? 0 : System.Math.Clamp(bgFps, 5, 240);
            _fullscreen = Settings.ReadValue(KeyFullscreen, _fullscreen);
            _renderScale = System.Math.Clamp(Settings.ReadValue(KeyRenderScale, _renderScale), 0.5f, 2f);
            _maxTextureSize = SnapTextureSize(Settings.ReadValue(KeyMaxTextureSize, _maxTextureSize));
            _reflectionsEnabled = Settings.ReadValue(KeyReflectionsEnabled, _reflectionsEnabled);
            _lodBias = System.Math.Clamp(Settings.ReadValue(KeyLodBias, _lodBias), 0.25f, 4f);
            float meshLod = Settings.ReadValue(KeyMeshLodThreshold, _meshLodThreshold);
            _meshLodThreshold = meshLod <= 0f ? 0f : System.Math.Clamp(meshLod, 0.5f, 8f);
            _shadowDistanceScale = System.Math.Clamp(Settings.ReadValue(KeyShadowDistanceScale, _shadowDistanceScale), 0.25f, 4f);
            _turnMode = ReadEnum(KeyTurnMode, _turnMode);
            _snapTurnAngle = System.Math.Clamp(Settings.ReadValue(KeySnapTurnAngle, _snapTurnAngle), 10f, 90f);
            _smoothTurnSpeed = System.Math.Clamp(Settings.ReadValue(KeySmoothTurnSpeed, _smoothTurnSpeed), 30f, 360f);
            _reticleStyle = ReadEnum(KeyReticleStyle, _reticleStyle);
            _reticleSize = System.Math.Clamp(Settings.ReadValue(KeyReticleSize, _reticleSize), 2f, 48f);
            _reticleThickness = System.Math.Clamp(Settings.ReadValue(KeyReticleThickness, _reticleThickness), 1f, 8f);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"EngineSettings: failed to load: {ex.Message}");
        }

        _dirty = false;
    }

    public static bool HasUnsavedChanges => _dirty;

    // Persist current values. Changes apply live for preview but are only saved here -
    // the exit screen's "Exit and Save" calls this.
    public static void Commit()
    {
        try
        {
            Settings.WriteValue(KeyMouseSensitivity, _mouseSensitivity);
            Settings.WriteValue(KeyMouseSmoothing, _mouseSmoothing);
            Settings.WriteValue(KeyNoclipSpeed, _noclipSpeed);
            Settings.WriteValue(KeyPreferredLocomotion, _preferredLocomotion);
            Settings.WriteValue(KeyUserHeight, _userHeight);
            Settings.WriteValue(KeyMasterVolume, _masterVolume);
            Settings.WriteValue(KeyVSync, _vsync);
            Settings.WriteValue(KeyMaxFps, _maxFps);
            Settings.WriteValue(KeyBackgroundFps, _backgroundFps);
            Settings.WriteValue(KeyFullscreen, _fullscreen);
            Settings.WriteValue(KeyRenderScale, _renderScale);
            Settings.WriteValue(KeyMaxTextureSize, _maxTextureSize);
            Settings.WriteValue(KeyReflectionsEnabled, _reflectionsEnabled);
            Settings.WriteValue(KeyLodBias, _lodBias);
            Settings.WriteValue(KeyMeshLodThreshold, _meshLodThreshold);
            Settings.WriteValue(KeyShadowDistanceScale, _shadowDistanceScale);
            Settings.WriteValue(KeyTurnMode, _turnMode.ToString());
            Settings.WriteValue(KeySnapTurnAngle, _snapTurnAngle);
            Settings.WriteValue(KeySmoothTurnSpeed, _smoothTurnSpeed);
            Settings.WriteValue(KeyReticleStyle, _reticleStyle.ToString());
            Settings.WriteValue(KeyReticleSize, _reticleSize);
            Settings.WriteValue(KeyReticleThickness, _reticleThickness);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"EngineSettings: failed to save: {ex.Message}");
        }

        _dirty = false;
    }

    // Enums persist by NAME: an int would silently re-point at a different option the day someone
    // inserts a value in the middle of the enum.
    private static T ReadEnum<T>(string key, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(Settings.ReadValue(key, fallback.ToString()), ignoreCase: true, out var parsed) ? parsed : fallback;

    private static void SetValue<T>(ref T field, T value) where T : IEquatable<T>
    {
        if (field.Equals(value))
            return;
        field = value;
        _dirty = true;
        Changed?.Invoke();
    }

    // Enums do not satisfy IEquatable<T>, so they need their own gate rather than the generic one.
    private static void SetEnum<T>(ref T field, T value) where T : struct, Enum
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        _dirty = true;
        Changed?.Invoke();
    }
}

