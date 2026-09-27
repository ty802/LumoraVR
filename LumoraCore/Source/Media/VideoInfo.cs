// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Core.Media;

// What the decoder reports once it knows what it is playing. Length is seconds, PositiveInfinity for
// a live stream. Width/Height are the decoded frame size; a stream with no video (a radio stream, an
// audio-only pick) reports 0x0 and the player shows its audio placeholder instead of a frame. -xlinka
public sealed record VideoReadyInfo(
    double Length,
    int Width,
    int Height,
    bool HasAlpha,
    string PlaybackEngine,
    IReadOnlyList<VideoAudioTrack> AudioTracks,
    bool IsLive);

public sealed record VideoAudioTrack(int Index, string Name, int Channels, int SampleRate);

public enum VideoFilterMode : byte
{
    Point,
    Bilinear,
    Trilinear,
}

// Which browser's cookie jar the service resolver may hand to yt-dlp, for age-gated or member-only
// videos. None is the default; nothing is read without the user picking one. -xlinka
public enum CookiesBrowser : byte
{
    None,
    Chrome,
    Firefox,
    Edge,
    Brave,
    Chromium,
    Opera,
    Safari,
    Vivaldi,
}

public sealed record VideoServiceRequest(
    int? MaxWidth,
    int? MaxHeight,
    bool PreferAudioOnly,
    CookiesBrowser Cookies);

// One playable format the resolver picked: a direct stream URL plus what is in it. Vcodec/Acodec are
// yt-dlp's strings (avc1..., mp4a..., "none") so the provider can choose a decoder engine from the
// codecs. IsLive marks a live service stream (Twitch, a YouTube premiere). -xlinka
public sealed record VideoServiceResolution(
    Uri StreamUrl,
    string? Title,
    bool HasVideo,
    bool HasAudio,
    string? Vcodec,
    string? Acodec,
    int Width,
    int Height,
    bool IsLive,
    // Set when the service only offers picture and sound as separate renditions (YouTube above
    // 360p is DASH-split); the decoder plays it as a slave of the video stream.
    Uri? AudioStreamUrl = null);

// The platform layer owns the yt-dlp process; the core only knows it can ask for a stream URL. Null
// means "could not resolve", and the provider reports that as a failed load with the message. -xlinka
public interface IVideoServiceResolver
{
    Task<VideoServiceResolution?> ResolveAsync(Uri url, VideoServiceRequest request, CancellationToken cancellationToken);
}

// Platform registrations for media. Set once by the runner before any world loads. -xlinka
public static class MediaServices
{
    public static IVideoServiceResolver? VideoServiceResolver { get; set; }

    // Decoder engine names as the provider and the UI spell them. ForcePlaybackEngine on a provider
    // takes one of these or null for automatic.
    public const string EngineNative = "Native";
    public const string EngineLibVlc = "libVLC";
}
