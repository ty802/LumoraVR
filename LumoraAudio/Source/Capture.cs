// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

public delegate void AudioSamplesHandler(ReadOnlySpan<StereoSample> samples);

// A microphone or any other capture device on the platform side. Samples arrive on whatever thread
// the platform captures on, as stereo at SampleRate (a mono device duplicates its channel); the
// engine-side input resamples, filters and fans them out. Start/Stop come from the main thread. -xlinka
public interface IAudioDeviceInput
{
    string Name { get; }
    string DeviceId { get; }
    int SampleRate { get; }
    bool IsSystemDefault { get; }
    bool IsCapturing { get; }

    event AudioSamplesHandler? SamplesCaptured;

    void Start();
    void Stop();
}
