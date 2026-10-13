// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Audio;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot;

// Which microphone and which speakers, for the settings screen to offer and for the engine to be
// pointed at.
//
// The platform names its system default "Default" and never resolves that to the device behind it,
// so the sentinel is translated to an empty name on the way up and back again on the way down; the
// lists handed out hold real devices only. A choice that is no longer plugged in falls back to the
// system default rather than leaving the engine aimed at a device that is not there.
//
// Applying restarts the device it belongs to. The audio driver reinitialises under a device change
// on its own, but the input stream and the generator playback are ours and both are attached to
// the stream that was running before it, so they come back new. -xlinka
public sealed class GodotAudioDevicePlatform : IAudioDevicePlatform
{
    // What the platform calls "whatever the system picked".
    private const string SystemDefault = "Default";

    private readonly GodotAudioDeviceInput? _input;
    private readonly GodotAudioDeviceOutput? _output;

    private string[] _inputDevices = Array.Empty<string>();
    private string[] _outputDevices = Array.Empty<string>();

    public GodotAudioDevicePlatform(GodotAudioDeviceInput? input, GodotAudioDeviceOutput? output)
    {
        _input = input;
        _output = output;
        Refresh();
    }

    public IReadOnlyList<string> InputDevices => _inputDevices;

    public IReadOnlyList<string> OutputDevices => _outputDevices;

    public string CurrentInputDevice => Read(() => AudioServer.InputDevice);

    public string CurrentOutputDevice => Read(() => AudioServer.OutputDevice);

    public void Refresh()
    {
        _inputDevices = Enumerate(static () => AudioServer.GetInputDeviceList());
        _outputDevices = Enumerate(static () => AudioServer.GetOutputDeviceList());
        LumoraLogger.Log($"Audio devices: {_inputDevices.Length} input, {_outputDevices.Length} output");
    }

    public void ApplyInput(string? deviceName)
    {
        string target = Resolve(deviceName, _inputDevices);
        RunOnMainThread(() =>
        {
            try
            {
                if (AudioServer.InputDevice == target)
                    return;
                AudioServer.InputDevice = target;

                // Only restart what was already open: with nobody subscribed the microphone is closed
                // and opening it here would put a recording light on for a settings change. -xlinka
                var input = _input;
                if (input != null && input.IsCapturing)
                {
                    input.Stop();
                    input.Start();
                }

                LumoraLogger.Debug($"Audio: input device -> {Describe(target)}");
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"GodotAudioDevicePlatform: could not switch the input device: {ex.Message}");
            }
        });
    }

    public void ApplyOutput(string? deviceName)
    {
        string target = Resolve(deviceName, _outputDevices);
        RunOnMainThread(() =>
        {
            try
            {
                if (AudioServer.OutputDevice == target)
                    return;
                AudioServer.OutputDevice = target;

                var output = _output;
                if (output != null)
                {
                    output.Stop();
                    output.Start();
                }

                LumoraLogger.Debug($"Audio: output device -> {Describe(target)}");
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"GodotAudioDevicePlatform: could not switch the output device: {ex.Message}");
            }
        });
    }

    private static string Describe(string device)
        => string.Equals(device, SystemDefault, StringComparison.Ordinal) ? "system default" : device;

    // A name nobody has is not an error worth refusing: the headset it belonged to is unplugged, and
    // the system default is what the user would have got without ever opening this screen.
    private static string Resolve(string? deviceName, string[] known)
    {
        if (string.IsNullOrEmpty(deviceName))
            return SystemDefault;
        for (int i = 0; i < known.Length; i++)
        {
            if (string.Equals(known[i], deviceName, StringComparison.Ordinal))
                return deviceName;
        }
        return SystemDefault;
    }

    private static string[] Enumerate(Func<string[]> read)
    {
        string[] raw;
        try
        {
            raw = read() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"GodotAudioDevicePlatform: could not list the audio devices: {ex.Message}");
            return Array.Empty<string>();
        }

        var kept = new List<string>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            string name = raw[i];
            if (string.IsNullOrEmpty(name) || string.Equals(name, SystemDefault, StringComparison.Ordinal))
                continue;
            kept.Add(name);
        }
        return kept.ToArray();
    }

    private static string Read(Func<string> get)
    {
        try
        {
            string device = get();
            return string.IsNullOrEmpty(device) || string.Equals(device, SystemDefault, StringComparison.Ordinal)
                ? string.Empty
                : device;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void RunOnMainThread(Action work)
    {
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
            work();
        else
            Callable.From(work).CallDeferred();
    }
}
