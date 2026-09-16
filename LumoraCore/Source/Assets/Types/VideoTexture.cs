// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Media;

namespace Lumora.Core.Assets;

// A texture whose pixels come from a decoder instead of a file. Dynamic like RenderTexture: the
// provider creates it, the hook (registered for this type) owns the decoder and the GPU texture,
// and materials sample it through the same IGodotTexture seam as any other texture, so a video is
// just a texture to everything downstream. Length is seconds, PositiveInfinity while live. -xlinka
public sealed class VideoTexture : TextureAsset
{
    private IVideoTextureAssetHook? _videoHook;

    public bool IsReady { get; private set; }
    public bool IsLive { get; private set; }
    public double Length { get; private set; } = -1.0;
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    public bool HasAlpha { get; private set; }
    public string PlaybackEngine { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }
    public IReadOnlyList<VideoAudioTrack> AudioTracks { get; private set; } = Array.Empty<VideoAudioTrack>();

    public float CurrentClockError => _videoHook?.CurrentClockError ?? 0f;
    public VideoStats Stats => _videoHook?.Stats ?? default;

    // Raised on the decoder's thread. The provider marshals to the world before touching sync state.
    public event Action<VideoTexture>? Ready;
    public event Action<VideoTexture>? FrameChanged;
    public event Action<VideoTexture, string>? Failed;

    private IVideoTextureAssetHook? VideoHook
    {
        get
        {
            if (_videoHook == null && Hook is IVideoTextureAssetHook hook)
            {
                _videoHook = hook;
                hook.Ready += OnHookReady;
                hook.TextureChanged += OnHookTextureChanged;
                hook.Failed += OnHookFailed;
            }
            return _videoHook;
        }
    }

    public void Load(string source, string? audioSource, string? forceEngine, string? mime, int? audioTrackIndex, bool isStream, bool isLive, int maxWidth, int maxHeight)
    {
        IsReady = false;
        FailureReason = null;
        Length = -1.0;
        VideoWidth = 0;
        VideoHeight = 0;
        var hook = VideoHook;
        if (hook == null)
        {
            OnHookFailed("no video decoder on this platform");
            return;
        }
        hook.Load(source, audioSource, forceEngine, mime, audioTrackIndex, isStream, isLive, maxWidth, maxHeight);
    }

    public void UpdateState(bool play, bool loop, double position, float speed)
        => VideoHook?.UpdateState(play, loop, position, speed);

    public void SetVolume(float volume) => VideoHook?.SetVolume(volume);
    public void SetAudioTrack(int? index) => VideoHook?.SetAudioTrack(index);
    public void SetAudioSink(IVideoAudioSink? sink) => VideoHook?.SetAudioSink(sink);

    public void SetTextureProperties(VideoFilterMode filter, int anisotropicLevel, TextureWrapMode wrapU, TextureWrapMode wrapV)
        => VideoHook?.SetTextureProperties(filter, anisotropicLevel, wrapU, wrapV);

    private void OnHookReady(VideoReadyInfo info)
    {
        Length = info.Length;
        IsLive = info.IsLive || double.IsPositiveInfinity(info.Length);
        VideoWidth = info.Width;
        VideoHeight = info.Height;
        HasAlpha = info.HasAlpha;
        PlaybackEngine = info.PlaybackEngine;
        AudioTracks = info.AudioTracks ?? Array.Empty<VideoAudioTrack>();
        IsReady = true;
        Version++;
        Ready?.Invoke(this);
    }

    private void OnHookTextureChanged()
    {
        Version++;
        FrameChanged?.Invoke(this);
    }

    private void OnHookFailed(string reason)
    {
        FailureReason = reason;
        IsReady = false;
        Failed?.Invoke(this, reason);
    }

    public override void Unload()
    {
        if (_videoHook != null)
        {
            _videoHook.Ready -= OnHookReady;
            _videoHook.TextureChanged -= OnHookTextureChanged;
            _videoHook.Failed -= OnHookFailed;
            _videoHook = null;
        }
        IsReady = false;
        base.Unload();
    }
}
