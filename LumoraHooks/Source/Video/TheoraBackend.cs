// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core.Media;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks.Video;

// The decoder that is always there. Theora ships inside the engine, so an .ogv plays on every
// platform we build for, including the ones with no libVLC at all, and it is the only thing left
// when libVLC fails to initialise.
//
// Its sound does NOT go through the audio sink: the player is a real audio source of its own and
// mixes straight onto the "Music" bus. That means an .ogv is heard at the listener rather than at
// the panel, which is the trade for having a decoder that needs no native library. -xlinka
public sealed class TheoraBackend : IVideoDecoderBackend
{
    private const string AudioBus = "Music";

    private readonly Node _host;

    private VideoStreamPlayer? _player;
    private bool _readyRaised;
    private bool _failed;
    private bool _finished;
    private string _source = string.Empty;

    private bool _wantPlay = true;
    private bool _wantLoop;
    private double _wantPosition;
    private float _wantSpeed = 1f;
    private float _volume = 1f;
    private double _lastSeekSeconds;
    private float _clockError;

    public TheoraBackend(Node host)
    {
        _host = host;
    }

    public string EngineName => MediaServices.EngineNative;

    public float CurrentClockError => _clockError;
    public VideoStats Stats => default;

    public event Action<VideoReadyInfo>? Ready;
    public event Action<string>? Failed;

    public void Open(string source, string? audioSource, int? audioTrackIndex, bool isStream, bool isLive, int maxWidth, int maxHeight)
    {
        _source = source ?? string.Empty;

        string path = ResolvePath(_source);
        if (path.Length == 0)
        {
            Fail("the built-in decoder plays local Ogg Theora files only");
            return;
        }
        if (!FileAccess.FileExists(path))
        {
            Fail($"file not found: {path}");
            return;
        }

        try
        {
            _player = new VideoStreamPlayer
            {
                Name = "VideoTheora",
                Visible = false,
                Expand = false,
                Bus = AudioBus,
                ProcessMode = Node.ProcessModeEnum.Always,
                Stream = new VideoStreamTheora { File = path },
            };
            _player.Finished += OnFinished;
            _host.AddChild(_player);

            if (audioTrackIndex.HasValue)
                _player.AudioTrack = audioTrackIndex.Value;

            ApplyVolume();
            _player.Play();
        }
        catch (Exception ex)
        {
            Fail($"the built-in decoder could not open the file: {ex.Message}");
        }
    }

    public void UpdateState(bool play, bool loop, double position, float speed)
    {
        _wantPlay = play;
        _wantLoop = loop;
        _wantPosition = position;
        _wantSpeed = speed;
    }

    public void SetVolume(float volume)
    {
        _volume = volume;
        ApplyVolume();
    }

    // Track selection is a property on the player and takes effect on the next buffer.
    public void SetAudioTrack(int? index)
    {
        if (_player == null || index == null || !GodotObject.IsInstanceValid(_player)) return;
        _player.AudioTrack = index.Value;
    }

    // Nothing to attach to: this decoder owns its own audio source. A world that wants the sound at
    // the panel has to play the file through libVLC instead.
    public void SetAudioSink(IVideoAudioSink? sink)
    {
    }

    public void Tick(IVideoFrameTarget target)
    {
        var player = _player;
        if (player == null || !GodotObject.IsInstanceValid(player)) return;

        var texture = player.GetVideoTexture();
        if (texture != null && texture.GetWidth() > 0)
        {
            target.PresentTexture(texture, texture.GetWidth(), texture.GetHeight());

            if (!_readyRaised)
            {
                _readyRaised = true;
                double length = player.GetStreamLength();
                bool live = length <= 0.0;
                Ready?.Invoke(new VideoReadyInfo(
                    live ? double.PositiveInfinity : length,
                    texture.GetWidth(),
                    texture.GetHeight(),
                    false,
                    EngineName,
                    Array.Empty<VideoAudioTrack>(),
                    live));
            }
        }

        ApplyTransport(player);
    }

    private void ApplyTransport(VideoStreamPlayer player)
    {
        player.Loop = _wantLoop;

        float speed = _wantSpeed <= 0f ? 1f : _wantSpeed;
        if (System.Math.Abs(player.SpeedScale - speed) > 0.01f)
            player.SpeedScale = speed;

        if (_finished)
        {
            _finished = false;
            if (_wantLoop)
                player.Play();
        }

        if (_wantPlay)
        {
            if (!player.IsPlaying())
                player.Play();
            else if (player.Paused)
                player.Paused = false;
        }
        else if (player.IsPlaying() && !player.Paused)
        {
            player.Paused = true;
        }

        ChaseClock(player);
    }

    // Theora seeks are keyframe-coarse and re-open the stream, so the dead band and the minimum
    // interval matter more here than they do for libVLC, not less. -xlinka
    private void ChaseClock(VideoStreamPlayer player)
    {
        if (!_wantPlay || !player.IsPlaying())
        {
            _clockError = 0f;
            return;
        }

        double decoderPosition = player.StreamPosition;
        double drift = decoderPosition - _wantPosition;
        _clockError = (float)drift;

        if (System.Math.Abs(drift) <= 0.5)
            return;

        double now = global::Godot.Time.GetTicksMsec() / 1000.0;
        if (now - _lastSeekSeconds < 1.0)
            return;

        _lastSeekSeconds = now;
        player.StreamPosition = _wantPosition;
    }

    private void ApplyVolume()
    {
        if (_player == null || !GodotObject.IsInstanceValid(_player)) return;
        _player.VolumeDb = _volume <= 0.0001f ? -80f : (float)(20.0 * System.Math.Log10(System.Math.Clamp(_volume, 0.0001f, 4f)));
    }

    private void OnFinished() => _finished = true;

    public void Stop()
    {
        if (_player == null || !GodotObject.IsInstanceValid(_player)) return;
        _player.Stop();
    }

    public void Dispose()
    {
        var player = _player;
        _player = null;
        if (player == null || !GodotObject.IsInstanceValid(player)) return;
        player.Finished -= OnFinished;
        player.Stop();
        player.QueueFree();
    }

    // FileAccess understands res://, user:// and plain OS paths; a file:// URL has to be unwrapped
    // first, and anything else is a network source this decoder cannot open.
    private static string ResolvePath(string source)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        if (source.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            return source;

        if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri.LocalPath;

        if (source.Contains("://", StringComparison.Ordinal))
            return string.Empty;

        return source;
    }

    private void Fail(string reason)
    {
        if (_failed) return;
        _failed = true;
        LumoraLogger.Warn($"TheoraBackend: {reason} ({_source})");
        Failed?.Invoke(reason);
    }
}
