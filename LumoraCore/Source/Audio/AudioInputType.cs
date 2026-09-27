// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Audio;

// What kind of thing a capture device actually is.
//
// The device list used to be a flat list of names, which is fine until somebody wants to share what
// their machine is playing rather than what their microphone hears. Those two are completely
// different jobs and the list gave no way to tell them apart: a person looking for their desktop
// audio had to know that the entry called "CABLE Output" was the one, and a person looking for their
// microphone had to know that it was not.
//
// So devices are classified and grouped by what they carry. -xlinka
public enum AudioInputType
{
    // Could not be told apart from the name.
    Unknown,

    // A real microphone or line input.
    Capture,

    // The machine's own output, captured back. This is what somebody means by "stream my audio".
    Loopback,

    // A virtual cable somebody installed, which is a loopback they wired themselves.
    VirtualCable,
}

public static class AudioInputTypes
{
    // CLASSIFICATION BY NAME, because that is the only thing the platform gives us.
    //
    // The audio layer we sit on enumerates capture endpoints and nothing else: it has no concept of a
    // loopback and no flag that says a device is one. On Linux the system exposes its own loopbacks as
    // ordinary capture devices called "Monitor of ...", so they are already in the list and only need
    // naming. On Windows nothing is there unless the person has turned on the old stereo mixer or
    // installed a virtual cable, and both of those announce themselves clearly in the name.
    //
    // Name matching is not elegant and it is what is available. It is also what every other
    // application that does this has to do. -xlinka
    public static AudioInputType Classify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return AudioInputType.Unknown;

        string lower = name!.ToLowerInvariant();

        // Virtual cables first: they are loopbacks, but naming them as what they are helps somebody
        // who installed one find it.
        if (lower.Contains("cable output") || lower.Contains("vb-audio") || lower.Contains("vb-cable")
            || lower.Contains("voicemeeter") || lower.Contains("virtual cable")
            || lower.Contains("blackhole") || lower.Contains("soundflower")
            || lower.Contains("loopback audio"))
            return AudioInputType.VirtualCable;

        // The system's own capture of its own output.
        if (lower.StartsWith("monitor of ") || lower.Contains("(monitor)")
            || lower.Contains("stereo mix") || lower.Contains("stereomix")
            || lower.Contains("what u hear") || lower.Contains("what you hear")
            || lower.Contains("loopback") || lower.Contains("wave out mix"))
            return AudioInputType.Loopback;

        if (lower.Contains("microphone") || lower.Contains("mic ") || lower.EndsWith(" mic")
            || lower.Contains("headset") || lower.Contains("line in") || lower.Contains("capture")
            || lower.Contains("webcam") || lower.Contains("array"))
            return AudioInputType.Capture;

        return AudioInputType.Unknown;
    }

    // True for anything that carries what the machine is playing.
    public static bool IsLoopback(AudioInputType type)
        => type == AudioInputType.Loopback || type == AudioInputType.VirtualCable;

    public static string Describe(AudioInputType type) => type switch
    {
        AudioInputType.Capture => "Microphone",
        AudioInputType.Loopback => "System audio",
        AudioInputType.VirtualCable => "Virtual cable",
        _ => "Device",
    };

    // The order the list groups by: what somebody is looking for when they open a device picker for
    // STREAMING is a loopback, so those come first there. The sort key is stable, so devices inside a
    // group keep the order the platform gave them.
    public static int StreamOrder(AudioInputType type) => type switch
    {
        AudioInputType.Loopback => 0,
        AudioInputType.VirtualCable => 1,
        AudioInputType.Capture => 2,
        _ => 3,
    };

    // And for a microphone picker it is the other way round.
    public static int CaptureOrder(AudioInputType type) => type switch
    {
        AudioInputType.Capture => 0,
        AudioInputType.Unknown => 1,
        AudioInputType.VirtualCable => 2,
        _ => 3,
    };
}
