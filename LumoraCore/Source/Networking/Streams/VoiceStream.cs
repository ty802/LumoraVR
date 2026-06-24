// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Per-user voice stream: carries encoded audio frames over the network. The stream plumbing is fully
/// wired - it's an implicit (periodic) stream, replicates like any other, reconstructs on peers via the
/// generic instantiation, and is addressed by name (e.g. <c>user.GetStreamOrAdd&lt;VoiceStream&gt;("Voice")</c>).
/// The audio codec itself is intentionally not implemented here. -xlinka
/// </summary>
/// <remarks>
/// TODO(techy): implement the audio codec. <see cref="SubmitSamples"/> buffers captured mic audio;
/// <see cref="Encode"/> should compress one frame (Opus or similar) and write it; <see cref="Decode"/>
/// should decompress an incoming frame into a playback buffer that <see cref="ReadSamples"/> drains.
/// The codec is heavy, so it opts into the Stream async seam (SupportsAsyncCodec) and runs the codec
/// off the world thread in InternalAsyncEncode/InternalAsyncDecode.
/// </remarks>
public class VoiceStream : ImplicitStream
{
    private bool _receivedFirstData;

    /// <summary>
    /// Audio is "valid" once any frame has arrived, or immediately for the local speaker.
    /// </summary>
    public override bool HasValidData => _receivedFirstData || IsLocal;

    /// <summary>
    /// Submit captured microphone samples to be encoded and sent on the next stream tick.
    /// </summary>
    public void SubmitSamples(float[] samples, int count)
    {
        CheckOwnership();
        // TODO(techy): buffer the mic samples for the next Encode (frame accumulator / ring buffer).
    }

    /// <summary>
    /// Drain the most recently decoded audio into <paramref name="destination"/> for playback (remote
    /// speakers). Returns the number of samples written.
    /// </summary>
    public int ReadSamples(float[] destination, int count)
    {
        // TODO(techy): copy decoded playback audio into destination; return the sample count written.
        return 0;
    }

    public override void Encode(BinaryWriter writer)
    {
        // TODO(techy): encode the buffered mic samples into a compressed audio frame and write it.
    }

    public override void Decode(BinaryReader reader, StreamMessage message)
    {
        _receivedFirstData = true;
        // TODO(techy): decode the incoming compressed audio frame and push it to the playback buffer.
    }

    // Voice is heavy enough to run the codec off the world thread, so it opts into the async seam on
    // Stream and overrides the background hooks below. -xlinka
    public override bool SupportsAsyncCodec => true;

    protected override void InternalAsyncEncode(BinaryWriter writer)
    {
        // TODO(techy): encode buffered mic samples into a compressed audio frame (Opus) and write it.
    }

    protected override void InternalAsyncDecode(BinaryReader reader, StreamMessage message)
    {
        // TODO(techy): decode the incoming compressed audio frame and push it to the playback buffer.
    }
}
