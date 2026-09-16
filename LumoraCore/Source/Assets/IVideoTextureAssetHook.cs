// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Media;

namespace Lumora.Core.Assets;

// The platform side of a video: a decoder that owns a GPU texture and answers to the synced
// playback state. The core never drives the decoder's clock; every frame it hands over
// play/loop/position/speed from the SyncPlayback and the decoder chases that (seek when the drift
// passes the tolerance, keep going otherwise). Events fire from decoder threads; the asset
// re-posts them to the world. -xlinka
public interface IVideoTextureAssetHook : ITextureAssetHook
{
    // source is a file path, an http(s) URL, or a stream URL (rtsp/rtmp/rtp/mms/srt/...). forceEngine is
    // MediaServices.EngineNative / EngineLibVlc or null for automatic. mime is a hint, may be null.
    // isLive is what the resolver or the URL scheme already knows: a live source has no position to
    // chase and its reported length (a sliding DVR window) must not be trusted.
    // audioSource is a second URL carrying the sound when the service hands picture and sound out as
    // separate streams; null when the source already has its own audio.
    // maxWidth/maxHeight cap what an adaptive (HLS/DASH) source is allowed to pick; 0 means no cap.
    void Load(string source, string? audioSource, string? forceEngine, string? mime, int? audioTrackIndex, bool isStream, bool isLive, int maxWidth, int maxHeight);

    void UpdateState(bool play, bool loop, double position, float speed);

    void SetVolume(float volume);
    void SetAudioTrack(int? index);
    void SetAudioSink(IVideoAudioSink? sink);
    void SetTextureProperties(VideoFilterMode filter, int anisotropicLevel, TextureWrapMode wrapU, TextureWrapMode wrapV);

    // Seconds the decoder is ahead (+) or behind (-) the position the core asked for, last frame.
    float CurrentClockError { get; }

    VideoStats Stats { get; }

    event Action<VideoReadyInfo> Ready;
    event Action TextureChanged;
    event Action<string> Failed;
}
