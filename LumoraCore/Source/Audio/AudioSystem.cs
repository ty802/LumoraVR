// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Audio;

namespace Lumora.Core.Audio;

// What a sound is for, so one slider can turn all of a kind up or down.
public enum AudioTypeGroup
{
    SoundEffect,
    Multimedia,
    Voice,
    UI,
}

// Whether an output's distances are in its slot's scale or in world metres.
public enum AudioDistanceSpace
{
    Local,
    Global,
}

// Engine-wide audio: the one mixer, the device it renders into, and the volumes that sit above any
// world. The platform hands over a device after engine init (null = headless: time still advances,
// nothing is heard). Worlds get their own space through World.Audio. -xlinka
public sealed class AudioSystem : IDisposable
{
    public AudioSimulator Simulator { get; } = new AudioSimulator();

    public bool IsInitialized { get; private set; }

    private bool _shutdown;

    private readonly List<AudioInput> _inputs = new List<AudioInput>();

    // Read from the settings once a frame and held here, because an output folds its group volume in
    // while it builds its params and a hundred outputs must not be a hundred settings reads.
    private float _master = 1f;
    private float _soundEffect = 1f;
    private float _multimedia = 1f;
    private float _voice = 1f;
    private float _ui = 1f;

    private long _statsLoggedAtMs;
    private int _loggedDeviceUnderruns;
    private int _loggedDroppedFrames;

    // Seconds from a source being read by the mixer to that sound reaching the ear.
    public double OutputLatencySeconds
        => (Simulator.PrimaryOutput.Device?.LatencySeconds ?? 0.0) + (double)Simulator.FrameSize / Simulator.SampleRate;

    public void Initialize(IAudioDeviceOutput? device)
    {
        if (IsInitialized || _shutdown)
            return;

        Simulator.PrimaryOutput.Device = device;
        Simulator.Start();
        IsInitialized = true;
    }

    // CAPTURE
    //
    // Inputs are registered by the platform at startup. There is no hot-plug rescan yet: a device that
    // appears after boot is not noticed until the next run.

    public IReadOnlyList<AudioInput> AudioInputs => _inputs;

    // The platform's own default if it named one, otherwise whatever came first. Null when the build
    // has no capture at all (headless, or a machine with no input device).
    public AudioInput? DefaultAudioInput
    {
        get
        {
            AudioInput? first = null;
            for (int i = 0; i < _inputs.Count; i++)
            {
                var input = _inputs[i];
                if (!input.IsConnected)
                    continue;
                if (input.IsSystemDefault)
                    return input;
                first ??= input;
            }
            return first;
        }
    }

    // DEVICE SELECTION
    //
    // Null in a headless build and until the platform hands one over, which is why every caller here
    // goes through the null check rather than assuming a machine has audio hardware at all.

    public IAudioDevicePlatform? DevicePlatform { get; set; }

    public IReadOnlyList<string> InputDeviceNames => DevicePlatform?.InputDevices ?? Array.Empty<string>();

    public IReadOnlyList<string> OutputDeviceNames => DevicePlatform?.OutputDevices ?? Array.Empty<string>();

    public string CurrentInputDeviceName => DevicePlatform?.CurrentInputDevice ?? string.Empty;

    public string CurrentOutputDeviceName => DevicePlatform?.CurrentOutputDevice ?? string.Empty;

    // Points both directions at what the settings ask for. Empty is the system default. The platform
    // drops a name it no longer has, so a device that was unplugged since the setting was saved falls
    // back to the system default rather than leaving the engine pointed at nothing.
    public void ApplyDeviceSelection()
    {
        var platform = DevicePlatform;
        if (platform == null)
            return;

        try
        {
            platform.ApplyInput(EngineSettings.AudioInputDevice);
            platform.ApplyOutput(EngineSettings.AudioOutputDevice);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"AudioSystem: could not apply the device selection: {ex.Message}");
        }
    }

    // Re-enumerate and point at the selection again. What the settings screen's reload button runs, and
    // the only way a device plugged in after boot is noticed.
    public void RefreshDevices()
    {
        var platform = DevicePlatform;
        if (platform == null)
            return;

        try
        {
            platform.Refresh();
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"AudioSystem: could not re-enumerate the audio devices: {ex.Message}");
            return;
        }

        ApplyDeviceSelection();
    }

    public event Action<AudioInput>? AudioInputAdded;

    public void RegisterAudioInput(AudioInput input)
    {
        if (input == null || _shutdown || _inputs.Contains(input))
            return;

        input.TargetSampleRate = Simulator.SampleRate;
        input.ApplySettings(_voiceFilter);
        _inputs.Add(input);
        AudioInputAdded?.Invoke(input);
    }

    private VoiceFilterSettings _voiceFilter = VoiceFilterSettings.Default;

    // PER-CATEGORY VOLUMES
    //
    // A second, open-ended layer above the four fixed groups: one gain per arbitrary key, which is how a
    // single user gets turned down without touching the Voice group everybody else rides. Keyed by
    // whatever the caller considers stable (User.LocalVolume keys on account/user id, falling back to
    // machine id), so nothing here has to know what a user is. -xlinka
    private readonly Dictionary<string, float> _categoryVolumes = new(StringComparer.Ordinal);

    // Users this machine has muted for itself. NOT persisted: EngineSettings has no collection shape at
    // all (every setting in it is a scalar or a single string), and inventing a delimited-string setting
    // to hold a set would be a new persistence pattern smuggled in sideways. A block lasts the session.
    // -xlinka
    private readonly HashSet<string> _blockedAudioUsers = new(StringComparer.Ordinal);

    public float WhisperAttenuationVolume => Clamp01(EngineSettings.WhisperAttenuationVolume);

    public bool IsMuted => EngineSettings.MicrophoneMuted;

    public void SetCategoryVolume(string category, float volume)
    {
        if (string.IsNullOrEmpty(category))
            return;
        _categoryVolumes[category] = Clamp01(volume);
    }

    public bool TryGetCategoryVolume(string category, out float volume)
    {
        if (!string.IsNullOrEmpty(category))
            return _categoryVolumes.TryGetValue(category, out volume);
        volume = 1f;
        return false;
    }

    public float GetCategoryVolume(string category)
        => TryGetCategoryVolume(category, out float volume) ? volume : 1f;

    public bool IsAudioBlocked(string key)
        => !string.IsNullOrEmpty(key) && _blockedAudioUsers.Contains(key);

    public void SetAudioBlocked(string key, bool blocked)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (blocked)
            _blockedAudioUsers.Add(key);
        else
            _blockedAudioUsers.Remove(key);
    }

    // Per engine frame, on the world thread: folds settings into group volumes and the master gain,
    // and pushes the voice filter settings down to every capture input so the capture thread never
    // has to read them itself.
    public void Update()
    {
        _master = Clamp01(EngineSettings.MasterVolume);
        _soundEffect = Clamp01(EngineSettings.SoundEffectVolume);
        _multimedia = Clamp01(EngineSettings.MultimediaVolume);
        _voice = Clamp01(EngineSettings.VoiceVolume);
        _ui = Clamp01(EngineSettings.UIVolume);

        // Whispering quiets the room you are whispering in. Voice is deliberately left alone: the whole
        // point of leaning in is to hear the person you leaned in to, and their voice rides the Voice
        // group. The emitter side of the same idea (a distant whisperer attenuated for everyone else)
        // lives on AvatarAudioOutputManager. -xlinka
        if (IsLocalUserWhispering())
        {
            float attenuation = WhisperAttenuationVolume;
            _soundEffect *= attenuation;
            _multimedia *= attenuation;
            _ui *= attenuation;
        }

        Simulator.PrimaryOutput.Volume = _master;

        if (_inputs.Count > 0)
        {
            _voiceFilter.Normalization = EngineSettings.VoiceNormalization;
            _voiceFilter.GateThreshold = EngineSettings.NoiseGateThreshold;
            _voiceFilter.GateAttack = EngineSettings.NoiseGateAttack;
            _voiceFilter.GateHold = EngineSettings.NoiseGateHold;
            _voiceFilter.GateRelease = EngineSettings.NoiseGateRelease;
            _voiceFilter.NormalizationTarget = EngineSettings.NormalizationThreshold;

            for (int i = 0; i < _inputs.Count; i++)
                _inputs[i].ApplySettings(_voiceFilter);
        }
    }

    private static bool IsLocalUserWhispering()
    {
        var world = Engine.Current?.WorldManager?.FocusedWorld;
        var user = world?.LocalUser;
        return user != null && user.ActiveVoiceMode == VoiceMode.Whisper;
    }

    public float GetGroupVolume(AudioTypeGroup group) => group switch
    {
        AudioTypeGroup.SoundEffect => _soundEffect,
        AudioTypeGroup.Multimedia => _multimedia,
        AudioTypeGroup.Voice => _voice,
        AudioTypeGroup.UI => _ui,
        _ => 1f,
    };

    public void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;
        IsInitialized = false;

        for (int i = 0; i < _inputs.Count; i++)
            _inputs[i].Dispose();
        _inputs.Clear();
        AudioInputAdded = null;
        _categoryVolumes.Clear();
        _blockedAudioUsers.Clear();

        Simulator.Stop();
        Simulator.Dispose();
    }

    public void Dispose() => Shutdown();

    private static float Clamp01(float value)
        => float.IsNaN(value) ? 0f : (value < 0f ? 0f : (value > 1f ? 1f : value));
}
