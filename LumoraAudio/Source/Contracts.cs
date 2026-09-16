// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Audio;

// Anything that can be heard. Read runs on the audio thread once per rendered frame per output
// that has this source, and fills the whole buffer at the simulator's sample rate in stereo (a mono
// source writes the same value to both channels). It must never block on a lock the world thread
// holds for long, and it must never throw: silence is the answer to every problem. -xlinka
public interface IAudioDataSource
{
    int ChannelCount { get; }

    // False means "skip me entirely this frame" (a stopped clip, a muted stream): cheaper than a
    // buffer of zeros, and the output stays parked so it does not count against the voice limit.
    bool IsActive { get; }

    void Read(Span<StereoSample> buffer, AudioSimulator simulator);
}

// One stage of a listener's effect chain. Process runs on the audio thread, in place, on the
// listener's mixed buffer, after every output for that listener has been summed. -xlinka
public interface IAudioDspFilter
{
    bool Enabled { get; }
    void Process(Span<StereoSample> buffer, int sampleRate);
    void Reset();
}

// The device seam. The simulator thread pushes rendered frames whenever the device says it has
// room; the platform behind it (Godot's generator stream, later a raw device) does the clocking.
// Everything here may be called from the simulator thread; Start/Stop come from the main thread.
public interface IAudioDeviceOutput
{
    int SampleRate { get; }

    // How many stereo frames Write can accept right now without blocking or dropping.
    int FramesWritable { get; }

    // Seconds between a frame being written and it reaching the ear: the device's own buffer plus
    // whatever the platform adds after it. Anything that has to line up with a picture reads this.
    double LatencySeconds { get; }

    void Write(ReadOnlySpan<StereoSample> frames);
    void Start();
    void Stop();
}

// What capture and playback devices this machine has, and which one each direction is pointed at.
// Separate from the two seams above because it is about CHOOSING a device rather than moving samples
// through one; the platform owns it because only the platform can enumerate hardware.
//
// An empty or null name means "whatever the system calls default", which is not the same as naming
// the device that happens to be default today: the system choice follows the machine when a headset
// is plugged in, a named one does not. Everything here is called from the main thread. -xlinka
public interface IAudioDevicePlatform
{
    IReadOnlyList<string> InputDevices { get; }
    IReadOnlyList<string> OutputDevices { get; }

    // What each direction is running on right now, resolved to a device name where the platform will
    // say and empty where it will not.
    string CurrentInputDevice { get; }
    string CurrentOutputDevice { get; }

    // Re-enumerate. Anything plugged in since the last call only exists in the lists after this.
    void Refresh();

    // Both restart the device they belong to, so a change lands without a relaunch.
    void ApplyInput(string? deviceName);
    void ApplyOutput(string? deviceName);
}

public enum AudioRolloffCurve
{
    // 1/d past MinDistance, never reaching silence.
    Logarithmic,
    // 1/d past MinDistance, then fades to silence at MaxDistance.
    LogarithmicFadeOff,
    // Straight line from MinDistance (full) to MaxDistance (silent).
    Linear,
}

public enum AudioShapeKind
{
    // Same volume everywhere, no panning.
    Global,
    // Rolloff from MinDistance to MaxDistance around the output's position.
    Sphere,
}

public readonly struct AudioShape
{
    public readonly AudioShapeKind Kind;
    public readonly float MinDistance;
    public readonly float MaxDistance;
    public readonly AudioRolloffCurve Rolloff;

    private AudioShape(AudioShapeKind kind, float minDistance, float maxDistance, AudioRolloffCurve rolloff)
    {
        Kind = kind;
        MinDistance = minDistance;
        MaxDistance = maxDistance;
        Rolloff = rolloff;
    }

    public static AudioShape Global => new(AudioShapeKind.Global, 0f, float.PositiveInfinity, AudioRolloffCurve.Logarithmic);

    public static AudioShape Sphere(float minDistance, float maxDistance, AudioRolloffCurve rolloff)
        => new(AudioShapeKind.Sphere, minDistance, maxDistance, rolloff);
}

// Everything the world side tells an output voice per frame. Written on the world thread through
// AudioOutputVoice.Update, copied by the audio thread at the start of its next render. -xlinka
public struct AudioOutputParams
{
    public float3 Position;
    public floatQ Rotation;
    // World units per second, for doppler; zero when the caller does not track it.
    public float3 Velocity;
    // 0..1, group and user volumes already folded in by the caller.
    public float Volume;
    // Higher wins when the voice limit is hit. 128 is the reference default.
    public int Priority;
    public bool Spatialize;
    // 0 = plain stereo everywhere, 1 = fully positioned; in between blends the two mixes.
    public float SpatialBlend;
    // Inside SpatializationStartDistance of a listener the sound is not positioned at all; it fades
    // into positioning over SpatializationTransitionRange. Stops a source at the ear from panning hard.
    public float SpatializationStartDistance;
    public float SpatializationTransitionRange;
    // Playback rate multiplier, 1 = native.
    public float Pitch;
    // 0 = no doppler, 1 = physical.
    public float DopplerLevel;
    public AudioShape Shape;
    // True bypasses the listener's effect chain (UI sounds, the user's own voice monitor).
    public bool IgnoreEffects;

    public static AudioOutputParams Default => new()
    {
        Rotation = floatQ.Identity,
        Volume = 1f,
        Priority = 128,
        Spatialize = true,
        SpatialBlend = 1f,
        SpatializationStartDistance = 0.01f,
        SpatializationTransitionRange = 0.01f,
        Pitch = 1f,
        DopplerLevel = 1f,
        Shape = AudioShape.Sphere(1f, 500f, AudioRolloffCurve.LogarithmicFadeOff),
    };
}

public struct AudioListenerParams
{
    public float3 Position;
    public floatQ Rotation;
    public float3 Velocity;

    public static AudioListenerParams Default => new() { Rotation = floatQ.Identity };
}

// Snapshot the world side can read for diagnostics; filled by the audio thread, no locking needed
// for a glance at it.
public struct AudioSimulatorStats
{
    public long RenderedFrames;
    public int DroppedFrames;
    public int ActiveOutputs;
    public int CulledOutputs;
    public int ActiveListeners;
    public double LastRenderMilliseconds;
    public int DeviceUnderruns;
    // Peak absolute sample of the last frame handed to the device; zero means the device got silence.
    public float LastDevicePeak;
    // Samples that were NaN or infinite anywhere on the way to the device, replaced by silence. Any
    // number above zero is a bug upstream; the count says how loud it would have been.
    public long NonFiniteSamples;
}
