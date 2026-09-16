// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Media;

// Where a decoder puts the sound it decodes. The decoder calls Configure once it knows the format,
// then Push from its own thread with interleaved float samples in that format; Flush on seek, stop
// or track change so stale audio does not play out after a jump. Volume is applied by the sink so a
// spatial emitter can fold it into its own gain. The Godot side implements this on the audio output
// hook; the decoder never sees a Godot type. -xlinka
public interface IVideoAudioSink
{
    void Configure(int channels, int sampleRate);
    // Seconds between Push and the ear. The decoder hands audio over this far ahead of its
    // timestamp so it lands with the picture instead of a buffer's length behind it.
    double LatencySeconds { get; }
    void Push(ReadOnlySpan<float> interleaved);
    void Flush();
    void SetVolume(float volume);
}
