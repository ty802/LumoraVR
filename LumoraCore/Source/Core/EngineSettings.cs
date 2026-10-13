// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Media;

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
        set => SetValue(ref _mouseSensitivity, SettingsCatalog.MouseSensitivity.Clamp(value));
    }

    private static float _mouseSmoothing;
    public static float MouseSmoothing
    {
        get => _mouseSmoothing;
        set => SetValue(ref _mouseSmoothing, SettingsCatalog.MouseSmoothing.Clamp(value));
    }

    // How much a VR laser's aim is steadied, 0 for the raw controller ray. The laser reads it as it casts.
    private static float _laserSmoothing = 0.5f;
    public static float LaserSmoothing
    {
        get => _laserSmoothing;
        set => SetValue(ref _laserSmoothing, SettingsCatalog.LaserSmoothing.Clamp(value));
    }

    // Scales every controller vibration, 0 for none at all.
    private static float _hapticsStrength = 1f;
    public static float HapticsStrength
    {
        get => _hapticsStrength;
        set => SetValue(ref _hapticsStrength, SettingsCatalog.HapticsStrength.Clamp(value));
    }

    // Noclip flight speed in m/s. Only affects the noclip locomotion module.
    private static float _noclipSpeed = 6f;
    public static float NoclipSpeed
    {
        get => _noclipSpeed;
        set => SetValue(ref _noclipSpeed, SettingsCatalog.NoclipSpeed.Clamp(value));
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

    // PHOTOS

    // The sizes a photo is taken at, and the one picked.
    public static readonly (int Width, int Height)[] PhotoSizes = { (1920, 1080), (2560, 1440), (3840, 2160) };
    private static int _photoSize;
    public static int PhotoSize
    {
        get => _photoSize;
        set => SetValue(ref _photoSize, System.Math.Clamp(value, 0, PhotoSizes.Length - 1));
    }

    private static Assets.ImageEncoding _photoEncoding = Assets.ImageEncoding.Webp;
    public static Assets.ImageEncoding PhotoEncoding
    {
        get => _photoEncoding;
        set => SetEnum(ref _photoEncoding, value);
    }

    // Seconds a timed photo counts down.
    private static float _photoTimerSeconds = 5f;
    public static float PhotoTimerSeconds
    {
        get => _photoTimerSeconds;
        set => SetValue(ref _photoTimerSeconds, SettingsCatalog.PhotoTimerSeconds.Clamp(value));
    }

    // An L made with the left hand opens a viewfinder.
    private static bool _handFrame = true;
    public static bool HandFrame
    {
        get => _handFrame;
        set => SetValue(ref _handFrame, value);
    }

    // Every photo also goes into the inventory's Photos folder.
    private static bool _photosToInventory = true;
    public static bool PhotosToInventory
    {
        get => _photosToInventory;
        set => SetValue(ref _photosToInventory, value);
    }

    // The hand this user leads with, Left or Right. Everyone sees it on their user record; menus and scripts
    // ask it which hand is which.
    private static Input.Chirality _primaryHand = Input.Chirality.Right;
    public static Input.Chirality PrimaryHand
    {
        get => _primaryHand;
        set => SetEnum(ref _primaryHand, value == Input.Chirality.Left ? Input.Chirality.Left : Input.Chirality.Right);
    }

    // AVATAR

    // Calibrated standing/eye height in metres. Drives the avatar auto-rescale (AvatarIK reads
    // InputInterface.UserHeight, which is kept in sync with this). Default 1.75 m. -xlinka
    private static float _userHeight = 1.75f;
    public static float UserHeight
    {
        get => _userHeight;
        set => SetValue(ref _userHeight, SettingsCatalog.UserHeight.Clamp(value));
    }

    // While somebody's avatar is still arriving on this machine, a glowing stand-in with a progress readout
    // takes its place and the half-built avatar stays out of sight. Off shows the avatar as it builds.
    private static bool _avatarArrival = true;
    public static bool AvatarArrival
    {
        get => _avatarArrival;
        set => SetValue(ref _avatarArrival, value);
    }

    // AUDIO

    // The master gain sits on the mixer's device output; the four below are group gains an AudioOutput
    // folds into its own volume by what the sound is FOR, so turning music down does not take the voice
    // of the person talking to you with it. -xlinka
    private static float _masterVolume = 1f;
    public static float MasterVolume
    {
        get => _masterVolume;
        set => SetValue(ref _masterVolume, SettingsCatalog.MasterVolume.Clamp(value));
    }

    private static float _soundEffectVolume = 1f;
    public static float SoundEffectVolume
    {
        get => _soundEffectVolume;
        set => SetValue(ref _soundEffectVolume, SettingsCatalog.SoundEffectVolume.Clamp(value));
    }

    private static float _multimediaVolume = 1f;
    public static float MultimediaVolume
    {
        get => _multimediaVolume;
        set => SetValue(ref _multimediaVolume, SettingsCatalog.MultimediaVolume.Clamp(value));
    }

    private static float _voiceVolume = 1f;
    public static float VoiceVolume
    {
        get => _voiceVolume;
        set => SetValue(ref _voiceVolume, SettingsCatalog.VoiceVolume.Clamp(value));
    }

    private static float _uiVolume = 1f;
    public static float UIVolume
    {
        get => _uiVolume;
        set => SetValue(ref _uiVolume, SettingsCatalog.UIVolume.Clamp(value));
    }

    // Whispering is quiet in both directions: a whisperer far enough away is attenuated by this for
    // everyone else, and the whisperer's own world is turned down by it so the person they are leaning
    // in to is the thing they can hear.
    private static float _whisperAttenuationVolume = 0.5f;
    public static float WhisperAttenuationVolume
    {
        get => _whisperAttenuationVolume;
        set => SetValue(ref _whisperAttenuationVolume, SettingsCatalog.WhisperAttenuationVolume.Clamp(value));
    }

    // DEVICES
    //
    // Empty is "use whatever the system calls default", and that is a different setting from naming the
    // device that is default today: the system choice follows the machine when a headset is plugged in
    // or the desk speakers are switched off, a named one stays put and goes quiet when it is unplugged.
    // A name the machine no longer has falls back to the system default at apply time rather than
    // leaving the engine pointed at nothing. -xlinka
    private static string _audioInputDevice = string.Empty;
    public static string AudioInputDevice
    {
        get => _audioInputDevice;
        set => SetValue(ref _audioInputDevice, value ?? string.Empty);
    }

    private static string _audioOutputDevice = string.Empty;
    public static string AudioOutputDevice
    {
        get => _audioOutputDevice;
        set => SetValue(ref _audioOutputDevice, value ?? string.Empty);
    }

    // Opening bitrate for an in-world audio stream, kbit/s. Voice has its own, much lower, figure: this
    // is for streaming a device into the room, which is usually music and usually stereo.
    private static int _defaultStreamBitrateKbps = 128;
    public static int DefaultStreamBitrateKbps
    {
        get => _defaultStreamBitrateKbps;
        set => SetValue(ref _defaultStreamBitrateKbps, SettingsCatalog.DefaultStreamBitrateKbps.ClampInt(value));
    }

    // The device the last stream went out on, by id, so the picker opens on it next time. Empty leaves
    // the picker on its first entry, which is system audio wherever the machine has it.
    private static string _streamDevice = string.Empty;
    public static string StreamDevice
    {
        get => _streamDevice;
        set => SetValue(ref _streamDevice, value ?? string.Empty);
    }

    // Where a stream's sound goes: an output device by id, usually a virtual cable a streaming program
    // listens to. Empty sends it nowhere.
    private static string _streamOutputDevice = string.Empty;
    public static string StreamOutputDevice
    {
        get => _streamOutputDevice;
        set => SetValue(ref _streamOutputDevice, value ?? string.Empty);
    }

    // DESKTOP
    //
    // The desktop page: the monitor it shows (DesktopDisplay.Key, empty for the main one), whether it
    // follows the mouse onto another monitor, and how bright the picture is drawn.
    private static string _desktopMonitor = string.Empty;
    public static string DesktopMonitor
    {
        get => _desktopMonitor;
        set => SetValue(ref _desktopMonitor, value ?? string.Empty);
    }

    private static bool _desktopFollowCursor;
    public static bool DesktopFollowCursor
    {
        get => _desktopFollowCursor;
        set => SetValue(ref _desktopFollowCursor, value);
    }

    private static float _desktopBrightness = 1f;
    public static float DesktopBrightness
    {
        get => _desktopBrightness;
        set => SetValue(ref _desktopBrightness, SettingsCatalog.DesktopBrightness.Clamp(value));
    }

    // What the desktop portal hands back when screen sharing is allowed, given back next time instead of
    // asking the user again. Only a Wayland desktop has one.
    //
    // It is single use: every share spends the token it was given and hands back the next. So it goes to
    // disk the moment it arrives, from whatever thread brings it, and it is not a change: nothing applies
    // it and nobody is asked to save it. A token that waited for "Exit and Save" would be a spent one on
    // every other way out, and the user would be asked again each launch.
    private static string _desktopPortalToken = string.Empty;
    public static string DesktopPortalToken
    {
        get => System.Threading.Volatile.Read(ref _desktopPortalToken);
        set
        {
            value ??= string.Empty;
            System.Threading.Volatile.Write(ref _desktopPortalToken, value);
            try
            {
                Settings.WriteValue(KeyDesktopPortalToken, value);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"EngineSettings: failed to keep the screen-share token: {ex.Message}");
            }
        }
    }

    // VOICE
    //
    // Local only: none of this replicates, because it describes this machine's microphone and nobody
    // else's ears. The host's own silencing lives on User.IsSilenced and is a different thing entirely.
    //
    // There is deliberately no noise suppression setting. Suppression means a denoiser, we have no
    // managed one, and a checkbox that turns nothing on is worse than no checkbox. The gate below is
    // what we actually have: it cuts the room between words, it does not clean up what is said over it.
    // -xlinka
    private static bool _microphoneMuted;
    public static bool MicrophoneMuted
    {
        get => _microphoneMuted;
        set => SetValue(ref _microphoneMuted, value);
    }

    private static bool _voiceNormalization = true;
    public static bool VoiceNormalization
    {
        get => _voiceNormalization;
        set => SetValue(ref _voiceNormalization, value);
    }

    // Peak amplitude a captured frame has to reach before the gate counts it as speech.
    // Spectral suppression ahead of the gate. On by default: a gate alone passes the whole room the
    // moment it opens, and "I can hear your fan" is the most common thing anyone says about anyone
    // else's microphone.
    private static bool _noiseSuppression = true;
    public static bool NoiseSuppression
    {
        get => _noiseSuppression;
        set => SetValue(ref _noiseSuppression, value);
    }

    // THE GATE, RETUNED TO THE BAND IT ACTUALLY WORKS IN.
    //
    // These four shipped on ranges five to ten times wider than anything usable - hold and release
    // ran to two seconds where a gate lives under a fifth of one - so the whole working range was
    // squeezed into the first tenth of the slider and a normal drag threw it straight past. The
    // ranges top out at 0.2, the band these controls are designed around.
    //
    // The release default came down with them: it was 0.2 s, which is a fifth of a second of open
    // microphone after every phrase. That is exactly the window the room noise rides out on. -xlinka
    private static float _noiseGateThreshold = 0.015f;
    public static float NoiseGateThreshold
    {
        get => _noiseGateThreshold;
        set => SetValue(ref _noiseGateThreshold, SettingsCatalog.NoiseGateThreshold.Clamp(value));
    }

    // Seconds the gate takes to open. Long enough and the first consonant of every word is gone.
    private static float _noiseGateAttack = 0.02f;
    public static float NoiseGateAttack
    {
        get => _noiseGateAttack;
        set => SetValue(ref _noiseGateAttack, SettingsCatalog.NoiseGateAttack.Clamp(value));
    }

    // Seconds the gate stays open after the last loud frame, so a pause between words is not a hole.
    private static float _noiseGateHold = 0.2f;
    public static float NoiseGateHold
    {
        get => _noiseGateHold;
        set => SetValue(ref _noiseGateHold, SettingsCatalog.NoiseGateHold.Clamp(value));
    }

    private static float _noiseGateRelease = 0.02f;
    public static float NoiseGateRelease
    {
        get => _noiseGateRelease;
        set => SetValue(ref _noiseGateRelease, SettingsCatalog.NoiseGateRelease.Clamp(value));
    }

    // A peak target nothing reads: the leveller steers by VoiceTargetLevel. Kept so the saved key stays.
    private static float _normalizationThreshold = 0.05f;
    public static float NormalizationThreshold
    {
        get => _normalizationThreshold;
        set => SetValue(ref _normalizationThreshold, SettingsCatalog.NormalizationThreshold.Clamp(value));
    }

    // Voiced speech level the leveller steers a voice toward, RMS in dBFS. A new key rather than a new
    // default on the old one: Commit writes every key, so every install already has the old peak target
    // saved and a changed default there would have reached nobody. -xlinka
    private static float _voiceTargetLevel = -18.5f;
    public static float VoiceTargetLevel
    {
        get => _voiceTargetLevel;
        set => SetValue(ref _voiceTargetLevel, SettingsCatalog.VoiceTargetLevel.Clamp(value));
    }

    // Fixed gain on the microphone ahead of the gate and the leveller, dB. For a capsule quieter than the
    // leveller's own range, or for anyone running with normalizing off.
    private static float _microphoneGain;
    public static float MicrophoneGain
    {
        get => _microphoneGain;
        set => SetValue(ref _microphoneGain, SettingsCatalog.MicrophoneGain.Clamp(value));
    }

    // The gain the leveller settled at on each input device, dB, so a microphone heard before starts at
    // its own level instead of climbing from unity through the first words of every session. Machine
    // local, per device id, and written when the device closes rather than on Commit: it is state the
    // chain arrived at, not a choice anybody saves. NaN for a device never heard. -xlinka
    public static float MicrophoneGainSeed(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return float.NaN;
        try
        {
            return Settings.ReadValue(KeyMicrophoneGainSeedPrefix + deviceId, float.NaN);
        }
        catch
        {
            return float.NaN;
        }
    }

    public static void SetMicrophoneGainSeed(string deviceId, float gainDb)
    {
        if (string.IsNullOrEmpty(deviceId) || !float.IsFinite(gainDb))
            return;
        gainDb = MathF.Round(gainDb, 1);
        // Half a decibel either way is the same microphone; not worth rewriting the store for.
        if (MathF.Abs(MicrophoneGainSeed(deviceId) - gainDb) < 0.5f)
            return;
        try
        {
            Settings.WriteValue(KeyMicrophoneGainSeedPrefix + deviceId, gainDb);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"EngineSettings: failed to keep the microphone gain: {ex.Message}");
        }
    }

    // MEDIA PLAYBACK

    // Which browser's cookie jar the video service resolver may read, for age-gated or member-only
    // links. None is the default and nothing is read at all until somebody picks one: handing a
    // resolver your logged-in session is a decision, not a convenience.
    private static CookiesBrowser _videoCookiesBrowser = CookiesBrowser.None;
    public static CookiesBrowser VideoCookiesBrowser
    {
        get => _videoCookiesBrowser;
        set => SetEnum(ref _videoCookiesBrowser, value);
    }

    // The default cap a video provider asks the resolver for when it has no cap of its own. Decode cost
    // scales with pixels, and a panel on a wall does not gain anything from 4k.
    private static int _videoMaxHeight = 1080;
    public static int VideoMaxHeight
    {
        get => _videoMaxHeight;
        set => SetValue(ref _videoMaxHeight, SettingsCatalog.VideoMaxHeight.ClampInt(value));
    }

    // STORAGE

    // Gigabytes of disk for assets this machine holds only because they came from elsewhere: downloads, and
    // what peers or the cloud sent. Past it the file wanted longest ago is let go; it can be had again. Zero
    // until someone sets it, which reads as the platform's default.
    private static int _assetCacheGigabytes;
    public static int AssetCacheGigabytes
    {
        get => _assetCacheGigabytes > 0 ? _assetCacheGigabytes : DefaultAssetCacheGigabytes;
        set => SetValue(ref _assetCacheGigabytes, SettingsCatalog.AssetCacheGigabytes.ClampInt(value));
    }

    // A standalone headset or phone shares a small disk with everything else on it.
    public static int DefaultAssetCacheGigabytes => Engine.UseConstrainedResources ? 4 : 10;

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
        set => SetValue(ref _maxFps, SettingsCatalog.MaxFps.ClampOptionalInt(value));
    }

    // Frame cap applied while the window is unfocused or minimized; 0 = no background throttle. Caps a
    // loop that would otherwise free-run when the compositor stops blocking the swap. Ignored in VR
    // (the headset compositor owns frame timing). Never raises the rate above MaxFps.
    private static int _backgroundFps = 30;
    public static int BackgroundFps
    {
        get => _backgroundFps;
        set => SetValue(ref _backgroundFps, SettingsCatalog.BackgroundFps.ClampOptionalInt(value));
    }

    private static bool _fullscreen;
    public static bool Fullscreen
    {
        get => _fullscreen;
        set => SetValue(ref _fullscreen, value);
    }

    // The screen camera's vertical field of view in degrees. 90 was hard-coded before there was a setting,
    // about 120 across a 16:9 window, which shrank everything in front of the user: the radial menu
    // read as far away. -xlinka
    private static float _fieldOfView = 75f;
    public static float FieldOfView
    {
        get => _fieldOfView;
        set => SetValue(ref _fieldOfView, SettingsCatalog.FieldOfView.Clamp(value));
    }

    private static float _renderScale = 1f;
    public static float RenderScale
    {
        get => _renderScale;
        set => SetValue(ref _renderScale, SettingsCatalog.RenderScale.Clamp(value));
    }

    // RenderScale becomes a CEILING when this is on: the platform layer lowers the 3D render scale
    // when frames run long and walks it back up when they do not. A 4k window is four times the
    // pixels of 1080p and nothing was adapting, which is the whole "30fps in fullscreen" report.
    // -xlinka
    private static bool _dynamicResolution = true;
    public static bool DynamicResolution
    {
        get => _dynamicResolution;
        set => SetValue(ref _dynamicResolution, value);
    }

    // 0 bilinear, 1 FSR 1, 2 FSR 2. DESKTOP ONLY and deliberately so: both are Forward+ features, our
    // Android/standalone build runs the mobile renderer where they do not exist, and FSR2 in particular
    // is a known open Godot bug in VR (godotengine/godot#89167 - it simply does not improve the image
    // through OpenXR). The XR path keeps bilinear; offering a headset toggle that changes nothing would
    // be a placebo. Only bites below 1.0 scale anyway, which is what dynamic resolution produces. -xlinka
    private static int _upscaler;
    public static int Upscaler
    {
        get => _upscaler;
        set => SetValue(ref _upscaler, SettingsCatalog.Upscaler.ClampInt(value));
    }

    public static string DescribeUpscaler(int value) => value switch
    {
        1 => "FSR 1",
        2 => "FSR 2",
        _ => "Bilinear"
    };

    // Vertical resolution to run at, 0 meaning the display's own. Anything at or above the display is
    // treated as native. What it does depends on the window mode, which is what players expect from
    // this control: WINDOWED resizes the window to that size, FULLSCREEN keeps the window native (a
    // Godot fullscreen window is borderless at desktop resolution) and renders the 3D buffer at that
    // height instead, so the interface stays sharp while the expensive part gets cheaper. -xlinka
    public static readonly int[] ResolutionHeightOptions = { 0, 2160, 1440, 1080, 900, 720 };

    private static int _resolutionHeight;
    public static int ResolutionHeight
    {
        get => _resolutionHeight;
        set => SetValue(ref _resolutionHeight, value < 0 ? 0 : value);
    }

    public static string DescribeResolutionHeight(int value) => value <= 0 ? "Native" : $"{value}p";

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

    // Original on a desktop. 2048 on a standalone headset or phone: one memory holds pictures and
    // everything else, and 2048 keeps a big world's textures well inside a Quest 2's share of its 6 GB
    // once they are block compressed.
    public static int DefaultMaxTextureSize => Engine.UseConstrainedResources ? 2048 : 0;

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
        set => SetValue(ref _lodBias, SettingsCatalog.LodBias.Clamp(value));
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
        set => SetValue(ref _meshLodThreshold, SettingsCatalog.MeshLodThreshold.ClampOptional(value));
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
        set => SetValue(ref _snapTurnAngle, SettingsCatalog.SnapTurnAngle.Clamp(value));
    }

    // Degrees per second while the stick is held, in Smooth mode.
    private static float _smoothTurnSpeed = 90f;
    public static float SmoothTurnSpeed
    {
        get => _smoothTurnSpeed;
        set => SetValue(ref _smoothTurnSpeed, SettingsCatalog.SmoothTurnSpeed.Clamp(value));
    }

    // Where smooth movement in VR takes forward from. Read live by the locomotion modules; a desktop
    // always walks where the view faces.
    private static MovementDirection _movementDirection = MovementDirection.Head;
    public static MovementDirection MovementDirection
    {
        get => _movementDirection;
        set => SetEnum(ref _movementDirection, value);
    }

    // INTERFACE

    public enum GizmoControlStyle
    {
        ContextMenu,
        Cubes,
        AllGizmo,
    }

    private static GizmoControlStyle _gizmoControls = GizmoControlStyle.ContextMenu;
    public static GizmoControlStyle GizmoControls
    {
        get => _gizmoControls;
        set => SetEnum(ref _gizmoControls, Enum.IsDefined(value) ? value : GizmoControlStyle.ContextMenu);
    }

    public enum ReticleShape
    {
        Ring,
        Dot,
        Crosshair,
        Off,
    }

    // Shared desktop reticle settings: the dash reads them through InterfaceSettings and the world
    // laser renders the same style at half the size.
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
        set => SetValue(ref _reticleSize, SettingsCatalog.ReticleSize.Clamp(value));
    }

    private static float _reticleThickness = 2f;
    public static float ReticleThickness
    {
        get => _reticleThickness;
        set => SetValue(ref _reticleThickness, SettingsCatalog.ReticleThickness.Clamp(value));
    }

    // Lengths read in feet and inches instead of metres. Only the text changes: every length is still
    // kept in metres, so flipping this never moves a value.
    private static bool _imperialUnits;
    public static bool ImperialUnits
    {
        get => _imperialUnits;
        set
        {
            if (_imperialUnits != value)
                UnitsVersion++;
            SetValue(ref _imperialUnits, value);
        }
    }

    // Moves on every flip of the units. Text already showing a length checks it to know it has to be
    // written again, though the value under it is the same.
    public static int UnitsVersion { get; private set; }

    // The label beside the pointer naming what it is on, shown once it has stayed there TooltipDelay seconds.
    private static bool _tooltips = true;
    public static bool Tooltips
    {
        get => _tooltips;
        set => SetValue(ref _tooltips, value);
    }

    private static float _tooltipDelay = 0.6f;
    public static float TooltipDelay
    {
        get => _tooltipDelay;
        set => SetValue(ref _tooltipDelay, SettingsCatalog.TooltipDelay.Clamp(value));
    }

    // POST PROCESSING
    //
    // These four had no representation at all, and the renderer was running the platform's untouched
    // defaults: a LINEAR tonemapper with a white point of 1.0. That clips every value above 1, which
    // is precisely the range colorHDR exists to carry - the material applicator deliberately keeps
    // emissives in HDR so neon keeps its punch, and then the tonemapper threw the punch away. There
    // was also no bloom, no exposure control and no anti-aliasing anywhere in the product. -xlinka

    public static readonly string[] TonemapOptions = { "Linear", "Reinhard", "Filmic", "ACES", "AgX" };

    // Index into TonemapOptions, which is ALSO the platform's own enum order, so the applier can cast
    // straight across. AgX by default: it holds saturated brights instead of skewing them toward white
    // the way ACES does, and saturated brights are exactly what an avatar's emissives are made of.
    private static int _tonemap = 4;
    public static int Tonemap
    {
        get => _tonemap;
        set => SetValue(ref _tonemap, System.Math.Clamp(value, 0, TonemapOptions.Length - 1));
    }

    public static string DescribeTonemap(int value)
        => value >= 0 && value < TonemapOptions.Length ? TonemapOptions[value] : TonemapOptions[4];

    // White point: the luminance that maps to full white. 1.0 is the platform default and is why
    // everything clips; higher keeps more highlight range before it saturates.
    private static float _exposure = 6f;
    public static float Exposure
    {
        get => _exposure;
        set => SetValue(ref _exposure, SettingsCatalog.Exposure.Clamp(value));
    }

    // Glow intensity. 0 turns the whole glow pass off rather than running it at zero strength, so the
    // cost goes away instead of being paid for nothing.
    private static float _bloom = 0.5f;
    public static float Bloom
    {
        get => _bloom;
        set => SetValue(ref _bloom, SettingsCatalog.Bloom.Clamp(value));
    }

    // Ambient occlusion strength, 0 = the pass is off, full by default. Contact shadow is what grounds
    // a body in a room: without it feet float over the floor and every corner reads as the same flat
    // ambient. Not available on the standalone renderer, where the applier leaves it off whatever this
    // says.
    private static float _ambientOcclusion = 1f;
    public static float AmbientOcclusion
    {
        get => _ambientOcclusion;
        set => SetValue(ref _ambientOcclusion, SettingsCatalog.AmbientOcclusion.Clamp(value));
    }

    // Screen-space reflections. Off by default: a full ray march per pixel, and only glossy floors
    // show it.
    private static bool _screenSpaceReflections;
    public static bool ScreenSpaceReflections
    {
        get => _screenSpaceReflections;
        set => SetValue(ref _screenSpaceReflections, value);
    }

    // Screen-space indirect lighting: diffuse bounce between nearby surfaces, the partner effect to
    // ambient occlusion in the engine's own guide and one of the two screen-space passes that render
    // clean in a headset. Off by default; it costs a second half-resolution pass like the occlusion.
    private static bool _screenSpaceIndirectLighting;
    public static bool ScreenSpaceIndirectLighting
    {
        get => _screenSpaceIndirectLighting;
        set => SetValue(ref _screenSpaceIndirectLighting, value);
    }

    // Colour adjustment after the tonemapper. AgX keeps saturated brights but flattens the midtones on
    // purpose, which reads as bland. A touch of contrast and saturation puts the punch back without
    // giving up the highlight handling; both at 1 means the pass is skipped entirely.
    private static float _contrast = 1.1f;
    public static float Contrast
    {
        get => _contrast;
        set => SetValue(ref _contrast, SettingsCatalog.Contrast.Clamp(value));
    }

    private static float _saturation = 1.15f;
    public static float Saturation
    {
        get => _saturation;
        set => SetValue(ref _saturation, SettingsCatalog.Saturation.Clamp(value));
    }

    public static readonly string[] ShadowQualityOptions = { "Hard", "Soft Low", "Soft Medium", "Soft High" };

    // The renderer's soft-shadow filter, which nothing in this engine has ever set - so we have been
    // running on the platform default for every world, on every machine. It is one of the largest
    // single levers on shadow cost: the same geometry measured a 2.1x swing across this range. -xlinka
    private static int _shadowQuality = 1;
    public static int ShadowQuality
    {
        get => _shadowQuality;
        set => SetValue(ref _shadowQuality, System.Math.Clamp(value, 0, ShadowQualityOptions.Length - 1));
    }

    public static string DescribeShadowQuality(int value)
        => value >= 0 && value < ShadowQualityOptions.Length ? ShadowQualityOptions[value] : ShadowQualityOptions[1];

    public static readonly int[] AntiAliasingOptions = { 0, 1, 2, 4, 8 };

    // Desktop mode: 0 is off, 1 is FXAA, and 2/4/8 retain the saved MSAA sample counts.
    // FXAA is the default for a new preference; headset rendering has its own quality profile.
    private static int _antiAliasing = 1;
    public static int AntiAliasing
    {
        get => _antiAliasing;
        set => SetValue(ref _antiAliasing, SnapAntiAliasing(value));
    }

    public static int SnapAntiAliasing(int value)
    {
        int best = 0;
        foreach (int option in AntiAliasingOptions)
        {
            if (option <= value && option > best)
                best = option;
        }
        return best;
    }

    public static string DescribeAntiAliasing(int value) => SnapAntiAliasing(value) switch
    {
        0 => "Off",
        1 => "FXAA",
        int samples => $"{samples}x MSAA",
    };

    // Multiplier on every directional light's shadow distance and on how far past its range a point or
    // spot light still gets a shadow. Below 1 shrinks the cascade range, which is the cheapest real win
    // on the shadow pass; above 1 buys distant shadows back. Applied by the light hooks on change, same
    // shape as LodBias.
    private static float _shadowDistanceScale = 1f;
    public static float ShadowDistanceScale
    {
        get => _shadowDistanceScale;
        set => SetValue(ref _shadowDistanceScale, SettingsCatalog.ShadowDistanceScale.Clamp(value));
    }

    // How many point and spot lights may cast a shadow at once, picked by how near and bright each is
    // to the viewer; the rest still light, unshadowed. Each one is up to six scene passes every frame
    // something in its range moves, so this is the shadow cost of a world full of lamps. A headset gets
    // its own, lower figure: two eyes at 90 Hz on the same budget a monitor gets at 60. 0 turns point and
    // spot shadows off. -xlinka
    private static int _shadowedLights = 4;
    public static int ShadowedLights
    {
        get => _shadowedLights;
        set => SetValue(ref _shadowedLights, SettingsCatalog.ShadowedLights.ClampInt(value));
    }

    private static int _shadowedLightsVR = 2;
    public static int ShadowedLightsVR
    {
        get => _shadowedLightsVR;
        set => SetValue(ref _shadowedLightsVR, SettingsCatalog.ShadowedLights.ClampInt(value));
    }

    // Times a second the shadow of something that keeps moving (a fan, a turntable, a spinning sign) is
    // brought up to date, 0 for every frame. A point light re-renders all six faces whenever anything it
    // covers moves, so one spinner kept every lamp around it rendering every frame; at this rate the lamps
    // sit on their cached shadow in between and the moving thing's shadow steps instead. Avatars and what
    // people hold are never slowed. -xlinka
    private static int _movingShadowRate = 15;
    public static int MovingShadowRate
    {
        get => _movingShadowRate;
        set => SetValue(ref _movingShadowRate, SettingsCatalog.MovingShadowRate.ClampOptionalInt(value));
    }

    // PERSISTENCE - values live-apply for preview; they are written to the shared binary config
    // store (Settings -> config.dat) only on Commit, which the exit screen's "Exit and Save" calls.

    private const string KeyMouseSensitivity = "Engine.Input.MouseSensitivity";
    private const string KeyMouseSmoothing = "Engine.Input.MouseSmoothing";
    private const string KeyLaserSmoothing = "Engine.Input.LaserSmoothing";
    private const string KeyHapticsStrength = "Engine.Input.HapticsStrength";
    private const string KeyNoclipSpeed = "Engine.Input.NoclipSpeed";
    private const string KeyPreferredLocomotion = "Engine.Input.PreferredLocomotion";
    private const string KeyPrimaryHand = "Engine.Input.PrimaryHand";
    private const string KeyPhotoSize = "Engine.Photos.Size";
    private const string KeyPhotoEncoding = "Engine.Photos.Encoding";
    private const string KeyPhotoTimerSeconds = "Engine.Photos.TimerSeconds";
    private const string KeyHandFrame = "Engine.Photos.HandFrame";
    private const string KeyPhotosToInventory = "Engine.Photos.ToInventory";
    private const string KeyUserHeight = "Engine.Avatar.UserHeight";
    private const string KeyAvatarArrival = "Engine.Avatar.Arrival";
    private const string KeyMasterVolume = "Engine.Audio.MasterVolume";
    private const string KeySoundEffectVolume = "Engine.Audio.SoundEffectVolume";
    private const string KeyMultimediaVolume = "Engine.Audio.MultimediaVolume";
    private const string KeyVoiceVolume = "Engine.Audio.VoiceVolume";
    private const string KeyUIVolume = "Engine.Audio.UIVolume";
    private const string KeyWhisperAttenuationVolume = "Engine.Audio.WhisperAttenuationVolume";
    private const string KeyDefaultStreamBitrateKbps = "Engine.Audio.DefaultStreamBitrateKbps";
    private const string KeyAudioInputDevice = "Engine.Audio.InputDevice";
    private const string KeyAudioOutputDevice = "Engine.Audio.OutputDevice";
    private const string KeyStreamDevice = "Engine.Audio.StreamDevice";
    private const string KeyStreamOutputDevice = "Engine.Audio.StreamOutputDevice";
    // The camera keeps its own settings under this prefix, all of them following the account.
    public const string CameraKeyPrefix = "Engine.Camera.";
    private const string KeyDesktopMonitor = "Engine.Desktop.Monitor";
    private const string KeyDesktopFollowCursor = "Engine.Desktop.FollowCursor";
    private const string KeyDesktopBrightness = "Engine.Desktop.Brightness";
    private const string KeyDesktopPortalToken = "Engine.Desktop.PortalToken";
    private const string KeyMicrophoneMuted = "Engine.Audio.MicrophoneMuted";
    private const string KeyVoiceNormalization = "Engine.Audio.VoiceNormalization";
    private const string KeyNoiseSuppression = "Engine.Audio.NoiseSuppression";
    private const string KeyNoiseGateThreshold = "Engine.Audio.NoiseGateThreshold";
    private const string KeyNoiseGateAttack = "Engine.Audio.NoiseGateAttack";
    private const string KeyNoiseGateHold = "Engine.Audio.NoiseGateHold";
    private const string KeyNoiseGateRelease = "Engine.Audio.NoiseGateRelease";
    private const string KeyNormalizationThreshold = "Engine.Audio.NormalizationThreshold";
    private const string KeyVoiceTargetLevel = "Engine.Audio.VoiceTargetLevel";
    private const string KeyMicrophoneGain = "Engine.Audio.MicrophoneGain";
    private const string KeyMicrophoneGainSeedPrefix = "Engine.Audio.MicrophoneGainSeed.";
    private const string KeyVideoCookiesBrowser = "Engine.Media.VideoCookiesBrowser";
    private const string KeyVideoMaxHeight = "Engine.Media.VideoMaxHeight";
    private const string KeyAssetCacheGigabytes = "Engine.Storage.AssetCacheGigabytes";
    private const string KeyVSync = "Engine.Video.VSync";
    private const string KeyMaxFps = "Engine.Video.MaxFps";
    private const string KeyBackgroundFps = "Engine.Video.BackgroundFps";
    private const string KeyFullscreen = "Engine.Video.Fullscreen";
    private const string KeyRenderScale = "Engine.Video.RenderScale";
    private const string KeyDynamicResolution = "Engine.Video.DynamicResolution";
    private const string KeyUpscaler = "Engine.Video.Upscaler";
    private const string KeyResolutionHeight = "Engine.Video.ResolutionHeight";
    private const string KeyFieldOfView = "Engine.Video.FieldOfView";
    private const string KeyMaxTextureSize = "Engine.Video.MaxTextureSize";
    private const string KeyMobileTextureCeiling = "Engine.Video.MobileTextureCeiling";
    private const string KeyReflectionsEnabled = "Engine.Video.ReflectionsEnabled";
    private const string KeyLodBias = "Engine.Video.LodBias";
    private const string KeyMeshLodThreshold = "Engine.Video.MeshLodThreshold";
    private const string KeyShadowDistanceScale = "Engine.Video.ShadowDistanceScale";
    private const string KeyShadowedLights = "Engine.Video.ShadowedLights";
    private const string KeyShadowedLightsVR = "Engine.Video.ShadowedLightsVR";
    private const string KeyMovingShadowRate = "Engine.Video.MovingShadowRate";
    private const string KeyTonemap = "Engine.Video.Tonemap";
    private const string KeyExposure = "Engine.Video.Exposure";
    private const string KeyBloom = "Engine.Video.Bloom";
    private const string KeyAmbientOcclusion = "Engine.Video.AmbientOcclusion";
    private const string KeyScreenSpaceReflections = "Engine.Video.ScreenSpaceReflections";
    private const string KeyScreenSpaceIndirectLighting = "Engine.Video.ScreenSpaceIndirectLighting";
    private const string KeyContrast = "Engine.Video.Contrast";
    private const string KeySaturation = "Engine.Video.Saturation";
    private const string KeyAntiAliasing = "Engine.Video.AntiAliasing";
    private const string KeyShadowQuality = "Engine.Video.ShadowQuality";
    private const string KeyTurnMode = "Engine.Comfort.TurnMode";
    private const string KeySnapTurnAngle = "Engine.Comfort.SnapTurnAngle";
    private const string KeySmoothTurnSpeed = "Engine.Comfort.SmoothTurnSpeed";
    private const string KeyMovementDirection = "Engine.Comfort.MovementDirection";
    private const string KeyReticleStyle = "Engine.Interface.ReticleStyle";
    private const string KeyGizmoControls = "Engine.Interface.GizmoControls";
    private const string KeyReticleSize = "Engine.Interface.ReticleSize";
    private const string KeyReticleThickness = "Engine.Interface.ReticleThickness";
    private const string KeyImperialUnits = "Engine.Interface.ImperialUnits";
    private const string KeyTooltips = "Engine.Interface.Tooltips";
    private const string KeyTooltipDelay = "Engine.Interface.TooltipDelay";

    // Reads every value again, for when the account brought newer ones. On the main thread: what listens
    // to Changed reaches into the windowing and the renderer.
    public static void Reload()
    {
        _loaded = false;
        Load();
    }

    // Which settings follow the account to every computer it signs in on, and which stay with this one.
    // What depends on the hardware in front of you stays: devices, the graphics, master volume (headphones
    // differ), how the microphone is cleaned up, which monitor and browser. What is about how you like
    // to play goes with you.
    private static readonly HashSet<string> AccountKeys = new(StringComparer.Ordinal)
    {
        KeyMouseSensitivity, KeyMouseSmoothing, KeyNoclipSpeed, KeyPreferredLocomotion, KeyPrimaryHand,
        KeyLaserSmoothing, KeyHapticsStrength,
        KeyPhotoSize, KeyPhotoEncoding, KeyPhotoTimerSeconds, KeyHandFrame, KeyPhotosToInventory,
        KeyUserHeight, KeyAvatarArrival,
        KeySoundEffectVolume, KeyMultimediaVolume, KeyVoiceVolume, KeyUIVolume, KeyWhisperAttenuationVolume,
        KeyDefaultStreamBitrateKbps,
        KeyVideoMaxHeight,
        KeyDesktopFollowCursor, KeyDesktopBrightness,
        KeyTurnMode, KeySnapTurnAngle, KeySmoothTurnSpeed, KeyMovementDirection,
        KeyReticleStyle, KeyGizmoControls, KeyReticleSize, KeyReticleThickness,
        KeyImperialUnits, KeyTooltips, KeyTooltipDelay,
    };

    public static bool FollowsAccount(string key)
        => AccountKeys.Contains(key) || key.StartsWith(CameraKeyPrefix, StringComparison.Ordinal);

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            _mouseSensitivity = SettingsCatalog.MouseSensitivity.Clamp(Settings.ReadValue(KeyMouseSensitivity, _mouseSensitivity));
            _mouseSmoothing = SettingsCatalog.MouseSmoothing.Clamp(Settings.ReadValue(KeyMouseSmoothing, _mouseSmoothing));
            _laserSmoothing = SettingsCatalog.LaserSmoothing.Clamp(Settings.ReadValue(KeyLaserSmoothing, _laserSmoothing));
            _hapticsStrength = SettingsCatalog.HapticsStrength.Clamp(Settings.ReadValue(KeyHapticsStrength, _hapticsStrength));
            _noclipSpeed = SettingsCatalog.NoclipSpeed.Clamp(Settings.ReadValue(KeyNoclipSpeed, _noclipSpeed));
            _preferredLocomotion = Settings.ReadValue(KeyPreferredLocomotion, _preferredLocomotion) ?? string.Empty;
            _primaryHand = ReadEnum(KeyPrimaryHand, _primaryHand) == Input.Chirality.Left ? Input.Chirality.Left : Input.Chirality.Right;
            _photoSize = System.Math.Clamp(Settings.ReadValue(KeyPhotoSize, _photoSize), 0, PhotoSizes.Length - 1);
            _photoEncoding = ReadEnum(KeyPhotoEncoding, _photoEncoding);
            _photoTimerSeconds = SettingsCatalog.PhotoTimerSeconds.Clamp(Settings.ReadValue(KeyPhotoTimerSeconds, _photoTimerSeconds));
            _handFrame = Settings.ReadValue(KeyHandFrame, _handFrame);
            _photosToInventory = Settings.ReadValue(KeyPhotosToInventory, _photosToInventory);
            _userHeight = SettingsCatalog.UserHeight.Clamp(Settings.ReadValue(KeyUserHeight, _userHeight));
            _avatarArrival = Settings.ReadValue(KeyAvatarArrival, _avatarArrival);
            _masterVolume = SettingsCatalog.MasterVolume.Clamp(Settings.ReadValue(KeyMasterVolume, _masterVolume));
            _soundEffectVolume = SettingsCatalog.SoundEffectVolume.Clamp(Settings.ReadValue(KeySoundEffectVolume, _soundEffectVolume));
            _multimediaVolume = SettingsCatalog.MultimediaVolume.Clamp(Settings.ReadValue(KeyMultimediaVolume, _multimediaVolume));
            _voiceVolume = SettingsCatalog.VoiceVolume.Clamp(Settings.ReadValue(KeyVoiceVolume, _voiceVolume));
            _uiVolume = SettingsCatalog.UIVolume.Clamp(Settings.ReadValue(KeyUIVolume, _uiVolume));
            _whisperAttenuationVolume = SettingsCatalog.WhisperAttenuationVolume.Clamp(Settings.ReadValue(KeyWhisperAttenuationVolume, _whisperAttenuationVolume));
            _defaultStreamBitrateKbps = SettingsCatalog.DefaultStreamBitrateKbps.ClampInt(Settings.ReadValue(KeyDefaultStreamBitrateKbps, _defaultStreamBitrateKbps));
            _audioInputDevice = Settings.ReadValue(KeyAudioInputDevice, _audioInputDevice) ?? string.Empty;
            _audioOutputDevice = Settings.ReadValue(KeyAudioOutputDevice, _audioOutputDevice) ?? string.Empty;
            _streamDevice = Settings.ReadValue(KeyStreamDevice, _streamDevice) ?? string.Empty;
            _streamOutputDevice = Settings.ReadValue(KeyStreamOutputDevice, _streamOutputDevice) ?? string.Empty;
            _desktopMonitor = Settings.ReadValue(KeyDesktopMonitor, _desktopMonitor) ?? string.Empty;
            _desktopFollowCursor = Settings.ReadValue(KeyDesktopFollowCursor, _desktopFollowCursor);
            _desktopBrightness = SettingsCatalog.DesktopBrightness.Clamp(Settings.ReadValue(KeyDesktopBrightness, _desktopBrightness));
            _desktopPortalToken = Settings.ReadValue(KeyDesktopPortalToken, _desktopPortalToken) ?? string.Empty;
            _microphoneMuted = Settings.ReadValue(KeyMicrophoneMuted, _microphoneMuted);
            _voiceNormalization = Settings.ReadValue(KeyVoiceNormalization, _voiceNormalization);
            _noiseSuppression = Settings.ReadValue(KeyNoiseSuppression, _noiseSuppression);
            _noiseGateThreshold = SettingsCatalog.NoiseGateThreshold.Clamp(Settings.ReadValue(KeyNoiseGateThreshold, _noiseGateThreshold));
            _noiseGateAttack = SettingsCatalog.NoiseGateAttack.Clamp(Settings.ReadValue(KeyNoiseGateAttack, _noiseGateAttack));
            _noiseGateHold = SettingsCatalog.NoiseGateHold.Clamp(Settings.ReadValue(KeyNoiseGateHold, _noiseGateHold));
            _noiseGateRelease = SettingsCatalog.NoiseGateRelease.Clamp(Settings.ReadValue(KeyNoiseGateRelease, _noiseGateRelease));
            _normalizationThreshold = SettingsCatalog.NormalizationThreshold.Clamp(Settings.ReadValue(KeyNormalizationThreshold, _normalizationThreshold));
            _voiceTargetLevel = SettingsCatalog.VoiceTargetLevel.Clamp(Settings.ReadValue(KeyVoiceTargetLevel, _voiceTargetLevel));
            _microphoneGain = SettingsCatalog.MicrophoneGain.Clamp(Settings.ReadValue(KeyMicrophoneGain, _microphoneGain));
            _videoCookiesBrowser = ReadEnum(KeyVideoCookiesBrowser, _videoCookiesBrowser);
            _videoMaxHeight = SettingsCatalog.VideoMaxHeight.ClampInt(Settings.ReadValue(KeyVideoMaxHeight, _videoMaxHeight));
            int assetCache = Settings.ReadValue(KeyAssetCacheGigabytes, 0);
            _assetCacheGigabytes = assetCache <= 0 ? 0 : SettingsCatalog.AssetCacheGigabytes.ClampInt(assetCache);
            _vsync = Settings.ReadValue(KeyVSync, _vsync);
            int fps = Settings.ReadValue(KeyMaxFps, _maxFps);
            _maxFps = SettingsCatalog.MaxFps.ClampOptionalInt(fps);
            int bgFps = Settings.ReadValue(KeyBackgroundFps, _backgroundFps);
            _backgroundFps = SettingsCatalog.BackgroundFps.ClampOptionalInt(bgFps);
            _fullscreen = Settings.ReadValue(KeyFullscreen, _fullscreen);
            _renderScale = SettingsCatalog.RenderScale.Clamp(Settings.ReadValue(KeyRenderScale, _renderScale));
            _fieldOfView = SettingsCatalog.FieldOfView.Clamp(Settings.ReadValue(KeyFieldOfView, _fieldOfView));
            _dynamicResolution = Settings.ReadValue(KeyDynamicResolution, _dynamicResolution);
            _upscaler = SettingsCatalog.Upscaler.ClampInt(Settings.ReadValue(KeyUpscaler, _upscaler));
            _resolutionHeight = System.Math.Max(0, Settings.ReadValue(KeyResolutionHeight, _resolutionHeight));
            _maxTextureSize = SnapTextureSize(Settings.ReadValue(KeyMaxTextureSize, DefaultMaxTextureSize));
            // Builds before this saved Original on phones and headsets without anyone choosing it, and
            // there a picture's memory is the same memory everything else runs in. Once, an Original saved
            // that way becomes the mobile default; Original chosen after that stays.
            if (Engine.UseConstrainedResources && !Settings.ReadValue(KeyMobileTextureCeiling, false))
            {
                if (_maxTextureSize == 0)
                    _maxTextureSize = DefaultMaxTextureSize;
                Settings.WriteValue(KeyMaxTextureSize, _maxTextureSize);
                Settings.WriteValue(KeyMobileTextureCeiling, true);
            }
            _reflectionsEnabled = Settings.ReadValue(KeyReflectionsEnabled, _reflectionsEnabled);
            _lodBias = SettingsCatalog.LodBias.Clamp(Settings.ReadValue(KeyLodBias, _lodBias));
            float meshLod = Settings.ReadValue(KeyMeshLodThreshold, _meshLodThreshold);
            _meshLodThreshold = SettingsCatalog.MeshLodThreshold.ClampOptional(meshLod);
            _shadowDistanceScale = SettingsCatalog.ShadowDistanceScale.Clamp(Settings.ReadValue(KeyShadowDistanceScale, _shadowDistanceScale));
            _shadowedLights = SettingsCatalog.ShadowedLights.ClampInt(Settings.ReadValue(KeyShadowedLights, _shadowedLights));
            _shadowedLightsVR = SettingsCatalog.ShadowedLights.ClampInt(Settings.ReadValue(KeyShadowedLightsVR, _shadowedLightsVR));
            _movingShadowRate = SettingsCatalog.MovingShadowRate.ClampOptionalInt(Settings.ReadValue(KeyMovingShadowRate, _movingShadowRate));
            _tonemap = System.Math.Clamp(Settings.ReadValue(KeyTonemap, _tonemap), 0, TonemapOptions.Length - 1);
            _exposure = SettingsCatalog.Exposure.Clamp(Settings.ReadValue(KeyExposure, _exposure));
            _bloom = SettingsCatalog.Bloom.Clamp(Settings.ReadValue(KeyBloom, _bloom));
            _ambientOcclusion = SettingsCatalog.AmbientOcclusion.Clamp(Settings.ReadValue(KeyAmbientOcclusion, _ambientOcclusion));
            _screenSpaceReflections = Settings.ReadValue(KeyScreenSpaceReflections, _screenSpaceReflections);
            _screenSpaceIndirectLighting = Settings.ReadValue(KeyScreenSpaceIndirectLighting, _screenSpaceIndirectLighting);
            _contrast = SettingsCatalog.Contrast.Clamp(Settings.ReadValue(KeyContrast, _contrast));
            _saturation = SettingsCatalog.Saturation.Clamp(Settings.ReadValue(KeySaturation, _saturation));
            _antiAliasing = SnapAntiAliasing(Settings.ReadValue(KeyAntiAliasing, _antiAliasing));
            _shadowQuality = System.Math.Clamp(Settings.ReadValue(KeyShadowQuality, _shadowQuality), 0, ShadowQualityOptions.Length - 1);
            _turnMode = ReadEnum(KeyTurnMode, _turnMode);
            _snapTurnAngle = SettingsCatalog.SnapTurnAngle.Clamp(Settings.ReadValue(KeySnapTurnAngle, _snapTurnAngle));
            _smoothTurnSpeed = SettingsCatalog.SmoothTurnSpeed.Clamp(Settings.ReadValue(KeySmoothTurnSpeed, _smoothTurnSpeed));
            _movementDirection = ReadEnum(KeyMovementDirection, _movementDirection);
            _reticleStyle = ReadEnum(KeyReticleStyle, _reticleStyle);
            _gizmoControls = ReadEnum(KeyGizmoControls, GizmoControlStyle.ContextMenu);
            if (!Enum.IsDefined(_gizmoControls)) _gizmoControls = GizmoControlStyle.ContextMenu;
            _reticleSize = SettingsCatalog.ReticleSize.Clamp(Settings.ReadValue(KeyReticleSize, _reticleSize));
            _reticleThickness = SettingsCatalog.ReticleThickness.Clamp(Settings.ReadValue(KeyReticleThickness, _reticleThickness));
            // A reload can bring the other units from the account, and that counts as a flip too.
            bool imperial = Settings.ReadValue(KeyImperialUnits, _imperialUnits);
            if (imperial != _imperialUnits)
                UnitsVersion++;
            _imperialUnits = imperial;
            _tooltips = Settings.ReadValue(KeyTooltips, _tooltips);
            _tooltipDelay = SettingsCatalog.TooltipDelay.Clamp(Settings.ReadValue(KeyTooltipDelay, _tooltipDelay));
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
            Settings.WriteValue(KeyLaserSmoothing, _laserSmoothing);
            Settings.WriteValue(KeyHapticsStrength, _hapticsStrength);
            Settings.WriteValue(KeyNoclipSpeed, _noclipSpeed);
            Settings.WriteValue(KeyPreferredLocomotion, _preferredLocomotion);
            Settings.WriteValue(KeyPrimaryHand, _primaryHand.ToString());
            Settings.WriteValue(KeyPhotoSize, _photoSize);
            Settings.WriteValue(KeyPhotoEncoding, _photoEncoding.ToString());
            Settings.WriteValue(KeyPhotoTimerSeconds, _photoTimerSeconds);
            Settings.WriteValue(KeyHandFrame, _handFrame);
            Settings.WriteValue(KeyPhotosToInventory, _photosToInventory);
            Settings.WriteValue(KeyUserHeight, _userHeight);
            Settings.WriteValue(KeyAvatarArrival, _avatarArrival);
            Settings.WriteValue(KeyMasterVolume, _masterVolume);
            Settings.WriteValue(KeySoundEffectVolume, _soundEffectVolume);
            Settings.WriteValue(KeyMultimediaVolume, _multimediaVolume);
            Settings.WriteValue(KeyVoiceVolume, _voiceVolume);
            Settings.WriteValue(KeyUIVolume, _uiVolume);
            Settings.WriteValue(KeyWhisperAttenuationVolume, _whisperAttenuationVolume);
            Settings.WriteValue(KeyDefaultStreamBitrateKbps, _defaultStreamBitrateKbps);
            Settings.WriteValue(KeyAudioInputDevice, _audioInputDevice);
            Settings.WriteValue(KeyAudioOutputDevice, _audioOutputDevice);
            Settings.WriteValue(KeyStreamDevice, _streamDevice);
            Settings.WriteValue(KeyStreamOutputDevice, _streamOutputDevice);
            Settings.WriteValue(KeyDesktopMonitor, _desktopMonitor);
            Settings.WriteValue(KeyDesktopFollowCursor, _desktopFollowCursor);
            Settings.WriteValue(KeyDesktopBrightness, _desktopBrightness);
            Settings.WriteValue(KeyMicrophoneMuted, _microphoneMuted);
            Settings.WriteValue(KeyVoiceNormalization, _voiceNormalization);
            Settings.WriteValue(KeyNoiseSuppression, _noiseSuppression);
            Settings.WriteValue(KeyNoiseGateThreshold, _noiseGateThreshold);
            Settings.WriteValue(KeyNoiseGateAttack, _noiseGateAttack);
            Settings.WriteValue(KeyNoiseGateHold, _noiseGateHold);
            Settings.WriteValue(KeyNoiseGateRelease, _noiseGateRelease);
            Settings.WriteValue(KeyNormalizationThreshold, _normalizationThreshold);
            Settings.WriteValue(KeyVoiceTargetLevel, _voiceTargetLevel);
            Settings.WriteValue(KeyMicrophoneGain, _microphoneGain);
            Settings.WriteValue(KeyVideoCookiesBrowser, _videoCookiesBrowser.ToString());
            Settings.WriteValue(KeyVideoMaxHeight, _videoMaxHeight);
            Settings.WriteValue(KeyAssetCacheGigabytes, _assetCacheGigabytes);
            Settings.WriteValue(KeyVSync, _vsync);
            Settings.WriteValue(KeyMaxFps, _maxFps);
            Settings.WriteValue(KeyBackgroundFps, _backgroundFps);
            Settings.WriteValue(KeyFullscreen, _fullscreen);
            Settings.WriteValue(KeyRenderScale, _renderScale);
            Settings.WriteValue(KeyFieldOfView, _fieldOfView);
            Settings.WriteValue(KeyDynamicResolution, _dynamicResolution);
            Settings.WriteValue(KeyUpscaler, _upscaler);
            Settings.WriteValue(KeyResolutionHeight, _resolutionHeight);
            Settings.WriteValue(KeyMaxTextureSize, _maxTextureSize);
            Settings.WriteValue(KeyReflectionsEnabled, _reflectionsEnabled);
            Settings.WriteValue(KeyLodBias, _lodBias);
            Settings.WriteValue(KeyMeshLodThreshold, _meshLodThreshold);
            Settings.WriteValue(KeyShadowDistanceScale, _shadowDistanceScale);
            Settings.WriteValue(KeyShadowedLights, _shadowedLights);
            Settings.WriteValue(KeyShadowedLightsVR, _shadowedLightsVR);
            Settings.WriteValue(KeyMovingShadowRate, _movingShadowRate);
            Settings.WriteValue(KeyTonemap, _tonemap);
            Settings.WriteValue(KeyExposure, _exposure);
            Settings.WriteValue(KeyBloom, _bloom);
            Settings.WriteValue(KeyAmbientOcclusion, _ambientOcclusion);
            Settings.WriteValue(KeyScreenSpaceReflections, _screenSpaceReflections);
            Settings.WriteValue(KeyScreenSpaceIndirectLighting, _screenSpaceIndirectLighting);
            Settings.WriteValue(KeyContrast, _contrast);
            Settings.WriteValue(KeySaturation, _saturation);
            Settings.WriteValue(KeyAntiAliasing, _antiAliasing);
            Settings.WriteValue(KeyShadowQuality, _shadowQuality);
            Settings.WriteValue(KeyTurnMode, _turnMode.ToString());
            Settings.WriteValue(KeySnapTurnAngle, _snapTurnAngle);
            Settings.WriteValue(KeySmoothTurnSpeed, _smoothTurnSpeed);
            Settings.WriteValue(KeyMovementDirection, _movementDirection.ToString());
            Settings.WriteValue(KeyReticleStyle, _reticleStyle.ToString());
            Settings.WriteValue(KeyGizmoControls, _gizmoControls.ToString());
            Settings.WriteValue(KeyReticleSize, _reticleSize);
            Settings.WriteValue(KeyReticleThickness, _reticleThickness);
            Settings.WriteValue(KeyImperialUnits, _imperialUnits);
            Settings.WriteValue(KeyTooltips, _tooltips);
            Settings.WriteValue(KeyTooltipDelay, _tooltipDelay);
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
