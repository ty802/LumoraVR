// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Per-user voice stream scaffolding. NOT IMPLEMENTED - there is no working voice path today: this class
/// is never instantiated anywhere, has no audio-capture source, and carries no codec. Encode/Decode are
/// empty, so even if it were created and replicated it would transmit nothing.
/// </summary>
/// <remarks>
/// What real voice needs (external/platform work, not present in this repo):
///   1. An audio-capture hook that delivers microphone samples to <see cref="SubmitSamples"/> (platform
///      layer; no such hook exists).
///   2. An audio codec to compress a frame in <see cref="Encode"/> and decompress it in <see cref="Decode"/>
///      (e.g. an Opus binding; not referenced anywhere).
///   3. A playback path that drains <see cref="ReadSamples"/> into an output bus.
///   4. Wiring to create the stream (e.g. <c>user.GetStreamOrAdd&lt;VoiceStream&gt;("Voice")</c>) and to
///      drive Submit/Read each frame.
/// The stream base class encodes/decodes synchronously on the sync thread; a heavy codec would also need
/// an off-thread seam added to <see cref="Stream"/> (there is none today). Kept as a typed placeholder so
/// the shape is documented; delete it if voice is descoped. -xlinka
/// </remarks>
public class VoiceStream : ImplicitStream
{
    private bool _receivedFirstData;

    /// <summary>
    /// Audio is "valid" once any frame has arrived, or immediately for the local speaker. Note: no frame
    /// ever carries audio yet (Encode is empty), so this only reflects that a frame was received at all.
    /// </summary>
    public override bool HasValidData => _receivedFirstData || IsLocal;

    /// <summary>
    /// Intended entry point for captured microphone samples. NOT IMPLEMENTED: discards its input; there is
    /// no audio-capture hook to call it and no buffer behind it.
    /// </summary>
    public void SubmitSamples(float[] samples, int count)
    {
        CheckOwnership();
        // Not implemented: no capture buffer / codec. Samples are dropped.
    }

    /// <summary>
    /// Intended drain for decoded playback audio. NOT IMPLEMENTED: always writes nothing and returns 0.
    /// </summary>
    public int ReadSamples(float[] destination, int count)
    {
        // Not implemented: nothing is ever decoded into a playback buffer.
        return 0;
    }

    public override void Encode(BinaryWriter writer)
    {
        // Not implemented: no codec. Writes an empty frame.
    }

    public override void Decode(BinaryReader reader, StreamMessage message)
    {
        // Not implemented: no codec. The frame carries no audio; we only note that one arrived.
        _receivedFirstData = true;
    }
}
