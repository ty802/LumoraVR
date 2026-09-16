// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

#if GODOT_PC
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using LibVLCSharp.Shared;
using Lumora.Core.Media;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks.Video;

// libVLC through its memory outputs: pictures land in a managed buffer we pin, sound lands in the
// core's audio sink, and neither libVLC window nor libVLC audio device is ever opened. That is the
// whole reason for the callback plumbing - a video in a world is a texture and a spatial emitter,
// not a second window with its own speakers.
//
// Everything libVLC calls back on is its own thread. The only publishing point is Tick, which the
// hook runs on the main thread; nothing else in here touches Godot. -xlinka
public sealed class LibVlcBackend : IVideoDecoderBackend
{
    // A frame bigger than this is a decode we do not want to pay for on a headset or a laptop, and
    // libVLC will scale to whatever we write back in the format callback.
    private const int MaxDimension = 4096;

    // "RV32" is libVLC's 32-bit RGB, which on a little-endian machine is B,G,R,X in memory. Godot has
    // no BGRA image format, so the display callback swizzles into R,G,B,A on libVLC's own thread
    // rather than costing the main thread a full-frame pass. -xlinka
    private static readonly byte[] ChromaRv32 = { (byte)'R', (byte)'V', (byte)'3', (byte)'2' };

    private static readonly string[] LiveSchemes = { "rtsp", "rtmp", "rtmps", "rtp", "udp", "mms", "mmsh", "srt", "rist" };

    private readonly object _frameSync = new();

    private readonly MediaPlayer.LibVLCVideoFormatCb _formatCb;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _videoCleanupCb;
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly MediaPlayer.LibVLCVideoUnlockCb _unlockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;
    private readonly MediaPlayer.LibVLCAudioPlayCb _audioPlayCb;
    private readonly MediaPlayer.LibVLCAudioPauseCb _audioPauseCb;
    private readonly MediaPlayer.LibVLCAudioResumeCb _audioResumeCb;
    private readonly MediaPlayer.LibVLCAudioFlushCb _audioFlushCb;
    private readonly MediaPlayer.LibVLCAudioDrainCb _audioDrainCb;

    private LibVLC? _libvlc;
    private Media? _media;
    private MediaPlayer? _player;

    private readonly byte[]?[] _buffers = new byte[2][];
    private readonly GCHandle[] _pins = new GCHandle[2];
    // Buffers a format change replaced. They stay pinned until the player is gone: the decoder may
    // still be finishing a picture in one of them, and a write into memory the GC has since reused
    // is a crash inside the runtime, not here. -xlinka
    private readonly List<GCHandle> _retiredPins = new();
    private readonly IntPtr[] _pointers = new IntPtr[2];
    private int _writeIndex;
    private int _readIndex;
    private bool _frameReady;

    private int _frameWidth;
    private int _frameHeight;
    private int _visibleWidth;
    private int _visibleHeight;
    private byte[] _cropScratch = Array.Empty<byte>();
    private volatile bool _sizeKnown;

    private IVideoAudioSink? _sink;
    private float _volume = 1f;
    private float[] _audioScratch = Array.Empty<float>();
    private short[] _audioBlock = Array.Empty<short>();

    // The decoder hands audio over as soon as it is decoded, a whole network cache ahead of when it
    // is meant to be heard, stamped with the time it is due on libVLC's own clock. Pushing it to the
    // mixer on arrival plays the sound a second early and floods the ring; holding each block until
    // it is due (plus a small lead for the frame the release runs on) is what a sound card does. -xlinka
    private readonly object _audioQueueSync = new object();
    private long _audioBlocksReceived;
    private long _audioBlocksPushed;
    private long _audioBlocksLate;
    private long _audioLastLeadMicro;
    private long _framesDisplayed;
    private double _audioLeadSeconds;
    private VLCState _lastState;
    private readonly Queue<PacedAudioBlock> _audioQueue = new Queue<PacedAudioBlock>();
    private readonly Stack<float[]> _audioPool = new Stack<float[]>();
    private const long AudioLeadMicroseconds = 60_000;
    private const long AudioBacklogMicroseconds = 4_000_000;

    // Releasing from the frame tick tied the sound to the frame rate: a hitch on the main thread held
    // every due block back and the ring ran dry in a burst. A thread of its own with a 4 ms nap keeps
    // the release within a few milliseconds of due whatever the renderer is doing. -xlinka
    private Thread? _releaseThread;
    private volatile bool _releaseRunning;

    private struct PacedAudioBlock
    {
        public long Pts;
        public float[] Data;
        public int Count;
    }
    private const int AudioChannels = 2;
    private const int AudioSampleRate = 48000;

    private volatile bool _parseDone;
    private volatile bool _parseSucceeded;
    private volatile bool _parseHasVideo;
    private volatile bool _endReached;
    private volatile bool _errored;
    private volatile bool _restarting;
    private volatile bool _disposed;
    private volatile bool _applyAudioTrack;
    private volatile bool _isLive;
    private bool _declaredLive;
    private bool _slaved;
    private double _lastWantPosition;
    private double _lastWantSeconds;
    private string? _errorReason;

    private bool _wantPlay = true;
    private bool _wantLoop;
    private double _wantPosition;
    private float _wantSpeed = 1f;
    private bool _readyRaised;
    private bool _readyAudioOnly;
    private bool _readyClassified;
    private volatile int _lengthSeenMs;
    private long _firstFrameTicks;
    private const double UnclassifiedLiveAfterSeconds = 4.0;
    private bool _remote;
    private long _audioFirstSeenTicks;
    private const double RemoteAudioOnlyAfterSeconds = 5.0;
    private bool _firstFrameSeen;
    private int? _pendingAudioTrack;
    private double _lastSeekSeconds;
    private const double MaxDeviationSeconds = 2.0;
    private const double SeekSettleSeconds = 5.0;
    private const double PlayCooloffSeconds = 2.0;
    private const double MaxSeekErrorSeconds = 30.0;
    private double _lastReportedPosition;
    private double _lastReportedAt;
    private double _lastReportedBeforeSeek;
    private double _lastReportedAtBeforeSeek;
    private double _lastSeekError;
    private double _playCooloffUntil;
    private volatile bool _seeking;
    private volatile bool _firstReportAfterSeek;
    private float _clockError;

    private string _source = string.Empty;

    public LibVlcBackend()
    {
        _formatCb = OnVideoFormat;
        _videoCleanupCb = OnVideoCleanup;
        _lockCb = OnVideoLock;
        _unlockCb = OnVideoUnlock;
        _displayCb = OnVideoDisplay;
        _audioPlayCb = OnAudioPlay;
        _audioPauseCb = OnAudioPause;
        _audioResumeCb = OnAudioResume;
        _audioFlushCb = OnAudioFlush;
        _audioDrainCb = OnAudioDrain;
    }

    public string EngineName => MediaServices.EngineLibVlc;

    public float CurrentClockError => _clockError;

    public VideoStats Stats
    {
        get
        {
            var stats = new VideoStats
            {
                VideoFrames = Interlocked.Read(ref _framesDisplayed),
                WantedSeconds = _wantPosition,
                State = _lastState.ToString(),
            };
            lock (_audioQueueSync)
            {
                stats.AudioBlocks = _audioBlocksReceived;
                stats.AudioBlocksPushed = _audioBlocksPushed;
                stats.AudioBlocksLate = _audioBlocksLate;
                stats.AudioQueued = _audioQueue.Count;
                stats.AudioLeadSeconds = _audioLeadSeconds;
            }
            double reportedAt = _lastReportedAt;
            stats.DecoderSeconds = reportedAt > 0.0
                ? EstimatedPosition(global::Godot.Time.GetTicksMsec() / 1000.0, _wantSpeed <= 0f ? 1f : _wantSpeed)
                : -1.0;
            return stats;
        }
    }

    public event Action<VideoReadyInfo>? Ready;
    public event Action<string>? Failed;

    public void Open(string source, string? audioSource, int? audioTrackIndex, bool isStream, bool isLive, int maxWidth, int maxHeight)
    {
        _source = source ?? string.Empty;
        _pendingAudioTrack = audioTrackIndex;
        // Declared live (the resolver said so, or the scheme is a live protocol) is final. libVLC
        // reports a length for a live HLS playlist, the DVR window, and a length would otherwise
        // turn the chase back on: the decoder sits at the live edge, the transport sits near zero,
        // and every corrective seek re-opens the playlist for two frames. -xlinka
        _declaredLive = isLive || IsLiveScheme(_source);
        _isLive = _declaredLive;
        _slaved = !string.IsNullOrEmpty(audioSource);
        _lastWantSeconds = 0;
        _lastReportedPosition = 0.0;
        _lastReportedAt = 0.0;
        _lastReportedBeforeSeek = 0.0;
        _lastReportedAtBeforeSeek = 0.0;
        _lastSeekError = 0.0;
        _seeking = false;
        _firstReportAfterSeek = false;
        _playCooloffUntil = 0.0;
        _readyClassified = false;
        _lengthSeenMs = 0;
        _firstFrameTicks = 0;
        // A stream whose playlist lives on disk (a master written by the resolver) is still a stream:
        // the picture comes over the network, and nothing about it may be judged like a local file.
        _remote = isStream || LooksRemote(_source);
        _audioFirstSeenTicks = 0;
        _readyAudioOnly = false;

        _libvlc = LibVlcRuntime.Instance;
        if (_libvlc == null)
        {
            Fail($"libVLC unavailable: {LibVlcRuntime.UnavailableReason ?? "not initialised"}");
            return;
        }

        try
        {
            var options = new List<string>();
            if (isStream || _isLive || LooksRemote(_source))
            {
                // A world is not a media player: a second of buffer is what keeps a stream from
                // stuttering every time the network hiccups, and the transport chase absorbs the
                // latency it costs.
                options.Add(":network-caching=1000");
                options.Add(":live-caching=1000");
            }
            else
            {
                options.Add(":file-caching=300");
            }
            options.Add(":no-video-title-show");

            // A DASH-split service hands picture and sound out as two URLs. libVLC takes the second
            // as an input slave and lines it up on timestamps, which is how VLC itself plays such
            // pairs; the sink never learns there were two inputs.
            if (!string.IsNullOrEmpty(audioSource))
                options.Add(":input-slave=" + audioSource);

            // An HLS/DASH master lets libVLC pick the rendition; these keep it inside the item's caps
            // (and the user's setting behind them) instead of grabbing 4K because the line allows it.
            if (maxWidth > 0)
                options.Add($":adaptive-maxwidth={maxWidth}");
            if (maxHeight > 0)
                options.Add($":adaptive-maxheight={maxHeight}");

            var fromType = LooksRemote(_source) ? FromType.FromLocation : FromType.FromPath;
            _media = new Media(_libvlc, _source, fromType, options.ToArray());

            _player = new MediaPlayer(_media);
            _player.EnableKeyInput = false;
            _player.EnableMouseInput = false;

            // Hardware decoding is left at libVLC's own default. Forcing avcodec-hw=any turns a
            // driver quirk into a black frame or a crash on machines we cannot test, and the CPU
            // decode is fast enough for the sizes a world panel actually shows. -xlinka

            _player.SetVideoFormatCallbacks(_formatCb, _videoCleanupCb);
            _player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);

            // A fixed format instead of negotiating one: libVLC resamples to it, the sink is
            // configured once, and a track change can never hand the emitter a different rate
            // halfway through a buffer. 16-bit on purpose: this libVLC delivers S16N stereo no matter
            // which float fourcc is requested ("FL32" and "f32l" both measured as 4 bytes a frame),
            // and reading a block as floats is reading twice its size, which is an access violation
            // inside the runtime the first time a block ends at a page boundary. Ask for what arrives
            // and convert here. -xlinka
            _player.SetAudioFormat("S16N", AudioSampleRate, AudioChannels);
            _player.SetAudioCallbacks(_audioPlayCb, _audioPauseCb, _audioResumeCb, _audioFlushCb, _audioDrainCb);

            // libVLC's own gain stays at full. The user's volume is the sink's business so a spatial
            // emitter can fold it into its falloff instead of fighting it.
            _player.Volume = 100;

            _player.EndReached += OnEndReached;
            _player.EncounteredError += OnEncounteredError;
            _player.Playing += OnPlaying;
            _player.Paused += OnPaused;
            _player.Stopped += OnStopped;
            _player.LengthChanged += OnLengthChanged;
            _player.TimeChanged += OnTimeChanged;
            _player.Vout += OnVout;

            _sink?.Configure(AudioChannels, AudioSampleRate);
            _sink?.SetVolume(_volume);

            // Play now even when the core asked for a paused transport: nothing knows the size, the
            // length or the track list until libVLC has actually opened the media, and the first
            // frame is the poster a paused player shows. Tick applies the real pause state after it.
            _player.Play();
            StartReleaseThread();

            // The parse verdict only decides audio-only for local files. On a remote source it is a
            // second connection to the same stream that nothing reads, and on a live one that is a
            // second seat on the broadcast for nothing.
            if (_remote)
                _parseDone = true;
            else
                _ = ParseAsync(_media);
        }
        catch (Exception ex)
        {
            Fail($"libVLC could not open the media: {ex.Message}");
        }
    }

    private async Task ParseAsync(Media media)
    {
        try
        {
            var status = await media.Parse(MediaParseOptions.ParseNetwork, 15000).ConfigureAwait(false);
            if (status == MediaParsedStatus.Done)
            {
                _parseSucceeded = true;
                foreach (var track in media.Tracks)
                {
                    if (track.TrackType == TrackType.Video)
                    {
                        _parseHasVideo = true;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Debug($"LibVlcBackend: parse failed for '{_source}': {ex.Message}");
        }
        finally
        {
            _parseDone = true;
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
        _sink?.SetVolume(volume);
    }

    public void SetAudioTrack(int? index)
    {
        _pendingAudioTrack = index;
        var player = _player;
        if (player == null || index == null) return;
        try
        {
            player.SetAudioTrack(index.Value);
            _sink?.Flush();
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"LibVlcBackend: could not select audio track {index.Value}: {ex.Message}");
        }
    }

    public void SetAudioSink(IVideoAudioSink? sink)
    {
        if (ReferenceEquals(sink, _sink)) return;
        var previous = _sink;
        _sink = sink;
        previous?.Flush();
        if (sink != null)
        {
            sink.Configure(AudioChannels, AudioSampleRate);
            sink.SetVolume(_volume);
        }
    }

    public void Tick(IVideoFrameTarget target)
    {
        if (_disposed) return;

        if (_errored)
        {
            _errored = false;
            Fail(_errorReason ?? "libVLC reported a playback error");
            return;
        }

        PublishFrame(target);
        RaiseReadyIfKnown();
        ReleaseDueAudio(false);

        if (_applyAudioTrack)
        {
            _applyAudioTrack = false;
            SetAudioTrack(_pendingAudioTrack);
        }

        ApplyTransport();
    }

    private void PublishFrame(IVideoFrameTarget target)
    {
        // The lock is held across the copy on purpose: it is one memcpy of a single frame, and
        // blocking libVLC's display thread for that long is cheaper than the third buffer a
        // lock-free handover would need at 4K. -xlinka
        if (_visibleWidth == 0 && _sizeKnown)
            ReadVisibleSize();

        int width;
        int height;
        lock (_frameSync)
        {
            if (!_frameReady) return;
            _frameReady = false;
            var buffer = _buffers[_readIndex];
            if (buffer == null || _frameWidth <= 0 || _frameHeight <= 0) return;

            int bufferWidth = _frameWidth;
            int bufferHeight = _frameHeight;
            width = _visibleWidth > 0 && _visibleWidth <= bufferWidth ? _visibleWidth : bufferWidth;
            height = _visibleHeight > 0 && _visibleHeight <= bufferHeight ? _visibleHeight : bufferHeight;

            if (width == bufferWidth)
            {
                // Rows are contiguous, so the padding under the picture is simply not submitted.
                target.SubmitFrame(buffer, width, height);
            }
            else
            {
                int rowBytes = width * 4;
                int pitch = bufferWidth * 4;
                if (_cropScratch.Length < rowBytes * height)
                    _cropScratch = new byte[rowBytes * height];
                for (int y = 0; y < height; y++)
                    Buffer.BlockCopy(buffer, y * pitch, _cropScratch, y * rowBytes, rowBytes);
                target.SubmitFrame(_cropScratch, width, height);
            }
        }
        if (!_firstFrameSeen)
        {
            _firstFrameSeen = true;
            _firstFrameTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            LumoraLogger.Log($"LibVlcBackend: first frame {width}x{height} from {Describe(_source)}");
        }
    }

    private void RaiseReadyIfKnown()
    {
        bool sizeKnown = _sizeKnown && _frameWidth > 0 && _frameHeight > 0;

        // A Ready can be superseded twice. An audio-only verdict gives way to a picture the moment a
        // frame size shows up. A length-unknown verdict gives way to the real length when the demuxer
        // reports one (an HLS playlist only learns it after the first segments), or to "live" once the
        // source has been running for a while with no length and no way to seek, which is what a
        // pasted live playlist looks like. Until one of those lands the source is unclassified and the
        // transport is not chased: chasing an undeclared live stream is a seek every second. -xlinka
        bool lengthArrived = _readyRaised && !_readyClassified && _lengthSeenMs > 0;
        bool looksLive = _readyRaised && !_readyClassified && !_declaredLive && UnclassifiedLooksLive();
        if (_readyRaised && !(_readyAudioOnly && sizeKnown) && !lengthArrived && !looksLive) return;

        // Deciding "there is no picture" is the dangerous call. A parse that failed or timed out says
        // nothing, and a network parse of an HLS/DASH playlist reports NO elementary streams even for
        // a 1080p live stream (they only exist once the demuxer runs), so a remote source is never
        // judged from its parse. Local files are: a successful parse with no video track is what an
        // audio file looks like. A remote source is audio-only only once sound has been flowing for a
        // few seconds with no video format callback, which is what an internet radio stream looks
        // like. Everything else waits for a real frame size; the hook's timeout catches a dead source. -xlinka
        bool audioOnly = false;
        if (!sizeKnown)
        {
            if (!_remote && _parseSucceeded && _parseDone && !_parseHasVideo)
                audioOnly = true;
            else if (_remote && _audioFirstSeenTicks != 0
                     && (System.Diagnostics.Stopwatch.GetTimestamp() - _audioFirstSeenTicks) / (double)System.Diagnostics.Stopwatch.Frequency > RemoteAudioOnlyAfterSeconds)
                audioOnly = true;
            if (!audioOnly) return;
        }

        var player = _player;
        var media = _media;
        if (player == null || media == null) return;

        _readyRaised = true;
        _readyAudioOnly = audioOnly;

        long durationMs = 0;
        try { durationMs = media.Duration; }
        catch (Exception ex) { LumoraLogger.Debug($"LibVlcBackend: duration unavailable: {ex.Message}"); }
        if (durationMs <= 0)
            durationMs = _lengthSeenMs;

        bool live = _declaredLive || looksLive;
        bool lengthKnown = durationMs > 0;
        double length = live ? double.PositiveInfinity : (lengthKnown ? durationMs / 1000.0 : -1.0);
        _readyClassified = live || lengthKnown;

        int width = 0;
        int height = 0;
        lock (_frameSync)
        {
            width = _frameWidth;
            height = _frameHeight;
        }
        if (width > 0 && _visibleWidth == 0)
            ReadVisibleSize();
        if (_visibleWidth > 0 && _visibleWidth <= width && _visibleHeight <= height)
        {
            width = _visibleWidth;
            height = _visibleHeight;
        }

        var tracks = CollectAudioTracks(player, media);
        _isLive = live;
        Ready?.Invoke(new VideoReadyInfo(length, width, height, false, EngineName, tracks, live));
        LumoraLogger.Log($"LibVlcBackend: ready {width}x{height} live={live} audioOnly={audioOnly} length={(live ? "-" : (lengthKnown ? length.ToString("0.0") : "?"))}s from {Describe(_source)}");
    }

    private bool UnclassifiedLooksLive()
    {
        if (!_firstFrameSeen || _firstFrameTicks == 0) return false;
        double running = (System.Diagnostics.Stopwatch.GetTimestamp() - _firstFrameTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (running < UnclassifiedLiveAfterSeconds) return false;
        try { return _player != null && !_player.IsSeekable; }
        catch (Exception) { return false; }
    }

    // Stream URLs carry a session token the length of a paragraph; the host and the start of the path
    // is what a log reader needs to tell two loads apart.
    private static string Describe(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            string path = uri.AbsolutePath;
            if (path.Length > 48) path = path.Substring(0, 48) + "...";
            return uri.Host + path;
        }
        return source;
    }

    // The decoder hands vmem its coded picture size (1920x1090 for a 1080p h264 stream, measured),
    // while the track carries the visible size. Uploading the coded size would put a strip of padding
    // under every picture and skew the aspect, so the visible size is what gets submitted. Called off
    // the frame lock: it asks libVLC for the track list. -xlinka
    private void ReadVisibleSize()
    {
        var media = _media;
        if (media == null) return;
        int bufferWidth;
        int bufferHeight;
        lock (_frameSync)
        {
            bufferWidth = _frameWidth;
            bufferHeight = _frameHeight;
        }
        try
        {
            foreach (var track in media.Tracks)
            {
                if (track.TrackType != TrackType.Video) continue;
                int w = (int)track.Data.Video.Width;
                int h = (int)track.Data.Video.Height;
                if (w > 0 && h > 0 && w <= bufferWidth && h <= bufferHeight)
                {
                    _visibleWidth = w;
                    _visibleHeight = h;
                }
                return;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Debug($"LibVlcBackend: visible size unavailable: {ex.Message}");
        }
    }

    private static IReadOnlyList<VideoAudioTrack> CollectAudioTracks(MediaPlayer player, Media media)
    {
        var result = new List<VideoAudioTrack>();
        try
        {
            var descriptions = player.AudioTrackDescription;
            if (descriptions == null) return result;

            foreach (var description in descriptions)
            {
                // libVLC always offers a synthetic "Disable" entry at id -1; it is a control, not a track.
                if (description.Id < 0) continue;

                int channels = 0;
                int rate = 0;
                var tracks = media.Tracks;
                if (tracks != null)
                {
                    foreach (var track in tracks)
                    {
                        if (track.TrackType != TrackType.Audio || track.Id != description.Id) continue;
                        channels = (int)track.Data.Audio.Channels;
                        rate = (int)track.Data.Audio.Rate;
                        break;
                    }
                }
                result.Add(new VideoAudioTrack(description.Id, description.Name ?? $"Track {description.Id}", channels, rate));
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Debug($"LibVlcBackend: audio track list unavailable: {ex.Message}");
        }
        return result;
    }

    private void ApplyTransport()
    {
        var player = _player;
        if (player == null) return;

        if (_endReached)
        {
            _endReached = false;
            if (_wantLoop)
                Restart(player);
            return;
        }

        try
        {
            float speed = _wantSpeed <= 0f ? 1f : _wantSpeed;
            if (System.Math.Abs(player.Rate - speed) > 0.01f)
                player.SetRate(speed);

            // Never pause before the first picture: a paused decoder that never decoded anything
            // leaves a blank texture where the poster frame should be. Ready arrives before the
            // picture now (it is raised on the frame size), so it is not enough on its own.
            if (_firstFrameSeen || _readyAudioOnly)
            {
                var state = player.State;
                _lastState = state;
                if (_wantPlay)
                {
                    if (state == VLCState.Paused)
                        player.SetPause(false);
                    else if (state is VLCState.Stopped or VLCState.Ended or VLCState.NothingSpecial)
                        Restart(player);
                }
                else if (state == VLCState.Playing)
                {
                    player.SetPause(true);
                }
            }

            ChaseClock(player);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"LibVlcBackend: transport update failed: {ex.Message}");
        }
    }

    // A live stream has no position to chase: whatever arrives is now, and seeking it would only
    // throw away the buffer. Files get a dead band and a minimum interval between corrective seeks,
    // because a decoder that seeks every frame spends its whole life re-opening keyframes. -xlinka
    private void ChaseClock(MediaPlayer player)
    {
        if (_isLive || !_wantPlay || !_readyClassified)
        {
            _clockError = 0f;
            // A paused decoder reports nothing, so its position is held where it stopped instead
            // of carried forward by the clock; otherwise the resume reads as a huge drift and seeks.
            if (_lastReportedAt > 0.0)
                _lastReportedAt = global::Godot.Time.GetTicksMsec() / 1000.0;
            return;
        }

        if (_slaved)
        {
            ChaseBySeekOnly(player);
            return;
        }

        double now = global::Godot.Time.GetTicksMsec() / 1000.0;
        float speed = _wantSpeed <= 0f ? 1f : _wantSpeed;
        if (_lastReportedAt <= 0.0)
        {
            // Nothing reported yet: the decoder is still opening and has no position to be wrong about.
            _clockError = 0f;
            return;
        }

        double estimated = EstimatedPosition(now, speed);
        double drift = estimated - _wantPosition;
        _clockError = (float)drift;

        // A couple of seconds either way is what a segmented stream costs to open and what a
        // viewer never notices; below that a seek costs more sync than it buys.
        if (System.Math.Abs(drift) <= MaxDeviationSeconds)
            return;
        if (now < _playCooloffUntil)
            return;
        if (_seeking)
        {
            // A seek that never reports back died in the demuxer; after a while it is fair game again.
            if (now - _lastSeekSeconds < SeekSettleSeconds)
                return;
            _seeking = false;
        }
        if (now - _lastSeekSeconds < 1.0)
            return;
        if (!player.IsSeekable)
            return;

        _lastSeekSeconds = now;
        _lastReportedBeforeSeek = _lastReportedPosition;
        _lastReportedAtBeforeSeek = _lastReportedAt;
        double target = System.Math.Max(0.0, _wantPosition + _lastSeekError);
        LumoraLogger.Debug($"LibVlcBackend: chase seek to {_wantPosition:0.00}s (correction {_lastSeekError:+0.00}s), decoder near {estimated:0.00}s, drift {drift:+0.00}s");
        player.Time = (long)(target * 1000.0);
        // From here the decoder is assumed to be where it was sent until it says otherwise.
        _lastReportedPosition = _wantPosition;
        _lastReportedAt = now;
        _seeking = true;
        _firstReportAfterSeek = true;
        _sink?.Flush();
    }

    // With an input slave attached libVLC's reported time freezes at the last seek target (measured),
    // so measured drift is meaningless and chasing it would re-seek every second for the rest of the
    // clip. What can still be honoured is a scrub: the wanted position jumping somewhere the clock
    // could not have carried it. -xlinka
    private void ChaseBySeekOnly(MediaPlayer player)
    {
        _clockError = 0f;
        double now = global::Godot.Time.GetTicksMsec() / 1000.0;
        float speed = _wantSpeed <= 0f ? 1f : _wantSpeed;
        if (_lastWantSeconds > 0)
        {
            double expected = _lastWantPosition + (now - _lastWantSeconds) * speed;
            if (System.Math.Abs(_wantPosition - expected) > 1.0
                && now - _lastSeekSeconds >= 1.0
                && player.IsSeekable)
            {
                _lastSeekSeconds = now;
                LumoraLogger.Debug($"LibVlcBackend: scrub seek to {_wantPosition:0.00}s, expected {expected:0.00}s");
                player.Time = (long)(_wantPosition * 1000.0);
                _sink?.Flush();
            }
        }
        _lastWantPosition = _wantPosition;
        _lastWantSeconds = now;
    }

    // Stop blocks until libVLC has torn the decoder down, which is not a thing to do on the frame
    // thread, and restarting from inside the EndReached callback is forbidden by libVLC outright.
    private void Restart(MediaPlayer player)
    {
        if (_restarting) return;
        _restarting = true;
        Task.Run(() =>
        {
            try
            {
                player.Stop();
                player.Play();
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"LibVlcBackend: restart failed: {ex.Message}");
            }
            finally
            {
                _restarting = false;
            }
        });
    }

    public void Stop()
    {
        StopReleaseThread();
        DropQueuedAudio();
        StopInternal();
    }

    private void StopInternal()
    {
        var player = _player;
        if (player == null) return;
        try { player.Stop(); }
        catch (Exception ex) { LumoraLogger.Debug($"LibVlcBackend: stop failed: {ex.Message}"); }
        _sink?.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var player = _player;
        var media = _media;
        _player = null;
        _media = null;
        _sink = null;

        if (player != null)
        {
            player.EndReached -= OnEndReached;
            player.EncounteredError -= OnEncounteredError;
            player.Playing -= OnPlaying;
            player.Paused -= OnPaused;
            player.Stopped -= OnStopped;
            player.LengthChanged -= OnLengthChanged;
            player.TimeChanged -= OnTimeChanged;
            player.Vout -= OnVout;
        }

        // Off the frame thread, and strictly ordered: the buffers stay pinned until the player that
        // writes into them is gone. The callback delegates are captured here so the GC cannot take
        // them while libVLC still holds the function pointers. -xlinka
        var pins = _pins;
        List<GCHandle> retired;
        lock (_frameSync)
        {
            retired = new List<GCHandle>(_retiredPins);
            _retiredPins.Clear();
        }
        var callbacks = new object[]
        {
            _formatCb, _videoCleanupCb, _lockCb, _unlockCb, _displayCb,
            _audioPlayCb, _audioPauseCb, _audioResumeCb, _audioFlushCb, _audioDrainCb,
        };
        Task.Run(() =>
        {
            try
            {
                player?.Stop();
                player?.Dispose();
                media?.Dispose();
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"LibVlcBackend: teardown failed: {ex.Message}");
            }
            finally
            {
                for (int i = 0; i < pins.Length; i++)
                {
                    if (pins[i].IsAllocated)
                        pins[i].Free();
                }
                foreach (var pin in retired)
                {
                    if (pin.IsAllocated)
                        pin.Free();
                }
                GC.KeepAlive(callbacks);
            }
        });
    }

    // libVLC CALLBACKS - all of these run on libVLC threads

    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        int w = (int)System.Math.Clamp(width, 1u, (uint)MaxDimension);
        int h = (int)System.Math.Clamp(height, 1u, (uint)MaxDimension);
        width = (uint)w;
        height = (uint)h;

        Marshal.Copy(ChromaRv32, 0, chroma, 4);
        pitches = (uint)(w * 4);
        lines = (uint)h;

        AllocateBuffers(w, h);
        _sizeKnown = true;
        return 1;
    }

    private void OnVideoCleanup(ref IntPtr opaque)
    {
        lock (_frameSync)
            _frameReady = false;
    }

    private IntPtr OnVideoLock(IntPtr opaque, IntPtr planes)
    {
        // lock/unlock/display all arrive on libVLC's one video output thread, so the write index is
        // only ever read and advanced there; the frame lock exists for the handover to the main thread.
        IntPtr pointer = _pointers[_writeIndex];
        Marshal.WriteIntPtr(planes, 0, pointer);
        return pointer;
    }

    private void OnVideoUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
    }

    private void OnVideoDisplay(IntPtr opaque, IntPtr picture)
    {
        int index = _writeIndex;
        var buffer = _buffers[index];
        if (buffer == null) return;

        SwizzleBgrxToRgba(buffer);

        lock (_frameSync)
        {
            _readIndex = index;
            _writeIndex = index ^ 1;
            _frameReady = true;
            _framesDisplayed++;
        }
    }

    private static void SwizzleBgrxToRgba(byte[] buffer)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan());
        for (int i = 0; i < pixels.Length; i++)
        {
            uint p = pixels[i];
            pixels[i] = 0xFF000000u | ((p & 0x000000FFu) << 16) | (p & 0x0000FF00u) | ((p >> 16) & 0x000000FFu);
        }
    }

    private void AllocateBuffers(int width, int height)
    {
        lock (_frameSync)
        {
            if (_frameWidth == width && _frameHeight == height && _buffers[0] != null)
                return;

            for (int i = 0; i < _pins.Length; i++)
            {
                if (_pins[i].IsAllocated)
                    _retiredPins.Add(_pins[i]);
                var buffer = new byte[width * height * 4];
                _buffers[i] = buffer;
                _pins[i] = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                _pointers[i] = _pins[i].AddrOfPinnedObject();
            }

            _frameWidth = width;
            _frameHeight = height;
            _visibleWidth = 0;
            _visibleHeight = 0;
            _writeIndex = 0;
            _readIndex = 0;
            _frameReady = false;
        }
    }

    private void OnAudioPlay(IntPtr data, IntPtr samples, uint count, long pts)
    {
        if (_audioFirstSeenTicks == 0)
            _audioFirstSeenTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        var sink = _sink;
        if (sink == null || count == 0) return;

        // count is frames; the block holds count * channels 16-bit samples, interleaved.
        int values = (int)count * AudioChannels;
        if (_audioBlock.Length < values)
            _audioBlock = new short[values];
        Marshal.Copy(samples, _audioBlock, 0, values);

        float[] block;
        lock (_audioQueueSync)
        {
            block = _audioPool.Count > 0 ? _audioPool.Pop() : Array.Empty<float>();
        }
        if (block.Length < values)
            block = new float[System.Math.Max(values, 4096)];

        const float scale = 1f / 32768f;
        for (int i = 0; i < values; i++)
            block[i] = _audioBlock[i] * scale;

        lock (_audioQueueSync)
        {
            _audioQueue.Enqueue(new PacedAudioBlock { Pts = pts, Data = block, Count = values });
            _audioBlocksReceived++;
        }
        ReleaseDueAudio(false);
    }

    // Runs on the decoder's audio thread after every block and on the main thread every tick, so a
    // block never waits longer than a frame past its time. everything = drain regardless of time.
    private void ReleaseDueAudio(bool everything)
    {
        var sink = _sink;
        var libvlc = _libvlc;
        // Released as far ahead of its time as the sink is slow, so the sound and the picture
        // that share a timestamp reach the viewer together. -xlinka
        long lead = AudioLeadMicroseconds;
        if (sink != null)
        {
            try
            {
                double latency = sink.LatencySeconds;
                if (latency > 0.0 && latency < 2.0) lead = (long)(latency * 1_000_000.0);
            }
            catch (Exception) { }
        }
        lock (_audioQueueSync)
        {
            _audioLeadSeconds = lead / 1_000_000.0;
            long now = 0;
            if (!everything)
            {
                try { now = libvlc?.Clock ?? 0; }
                catch (Exception) { everything = true; }
            }
            while (_audioQueue.Count > 0)
            {
                var head = _audioQueue.Peek();
                if (!everything) _audioLastLeadMicro = head.Pts - now;
                if (!everything && head.Pts - now > lead)
                    break;
                _audioQueue.Dequeue();
                // A block due more than a few seconds ago belongs to a stall that already happened;
                // playing it now would only push everything after it late as well.
                if (everything || now - head.Pts < AudioBacklogMicroseconds)
                {
                    try { sink?.Push(new ReadOnlySpan<float>(head.Data, 0, head.Count)); }
                    catch (Exception) { }
                    _audioBlocksPushed++;
                }
                else
                {
                    _audioBlocksLate++;
                }
                if (_audioPool.Count < 64)
                    _audioPool.Push(head.Data);
            }
        }
    }

    private void StartReleaseThread()
    {
        if (_releaseThread != null) return;
        _releaseRunning = true;
        _releaseThread = new Thread(() =>
        {
            while (_releaseRunning)
            {
                try { ReleaseDueAudio(false); }
                catch (Exception) { }
                Thread.Sleep(4);
            }
        })
        {
            Name = "Lumora video audio pacing",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _releaseThread.Start();
    }

    private void StopReleaseThread()
    {
        _releaseRunning = false;
        var thread = _releaseThread;
        _releaseThread = null;
        if (thread != null && thread.IsAlive)
            thread.Join(200);
    }

    private void DropQueuedAudio()
    {
        lock (_audioQueueSync)
        {
            while (_audioQueue.Count > 0)
            {
                var head = _audioQueue.Dequeue();
                if (_audioPool.Count < 64)
                    _audioPool.Push(head.Data);
            }
        }
    }

    // What was already handed over is not sent again after a pause, so it is dropped rather than
    // played out over the pause; the resume delivers fresh blocks with fresh times.
    private void OnAudioPause(IntPtr data, long pts)
    {
        DropQueuedAudio();
        _sink?.Flush();
    }

    private void OnAudioResume(IntPtr data, long pts)
    {
    }

    private void OnAudioFlush(IntPtr data, long pts)
    {
        DropQueuedAudio();
        _sink?.Flush();
    }

    private void OnAudioDrain(IntPtr data) => ReleaseDueAudio(true);

    private void OnEndReached(object? sender, EventArgs e) => _endReached = true;

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        _errorReason = _libvlc?.LastLibVLCError;
        if (string.IsNullOrWhiteSpace(_errorReason))
            _errorReason = "libVLC could not play this source";
        _errored = true;
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        if (_pendingAudioTrack.HasValue)
            _applyAudioTrack = true;
        _playCooloffUntil = global::Godot.Time.GetTicksMsec() / 1000.0 + PlayCooloffSeconds;
        _seeking = false;
    }

    // The decoder's own clock is only trusted through its reports: the time property freezes for
    // whole seconds on segmented streams and reads the seek target before the seek has landed, so
    // the position is the last report carried forward by wall time. -xlinka
    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        double now = global::Godot.Time.GetTicksMsec() / 1000.0;
        double reported = e.Time / 1000.0;
        if (reported < 0.0) return;
        if (_firstReportAfterSeek)
        {
            // The first report after a seek is the old position on its way out, not the new one.
            _firstReportAfterSeek = false;
            return;
        }
        if (_seeking)
        {
            float speed = _wantSpeed <= 0f ? 1f : _wantSpeed;
            double before = _lastReportedBeforeSeek + (now - _lastReportedAtBeforeSeek) * speed;
            // Landed somewhere other than asked, which segmented streams do when they snap to a
            // segment or keyframe. The miss is remembered so the next seek asks for the spot that
            // actually lands on the wanted time.
            if (System.Math.Abs(before - reported) > MaxDeviationSeconds)
            {
                _lastSeekError += EstimatedPosition(now, speed) - reported;
                if (_lastSeekError > MaxSeekErrorSeconds) _lastSeekError = MaxSeekErrorSeconds;
                else if (_lastSeekError < -MaxSeekErrorSeconds) _lastSeekError = -MaxSeekErrorSeconds;
            }
            _seeking = false;
        }
        _lastReportedPosition = reported;
        _lastReportedAt = now;
    }

    private double EstimatedPosition(double now, float speed)
        => _lastReportedPosition + (now - _lastReportedAt) * speed;

    private void OnPaused(object? sender, EventArgs e) => _sink?.Flush();

    private void OnStopped(object? sender, EventArgs e) => _sink?.Flush();

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        if (e.Length <= 0) return;
        _lengthSeenMs = (int)System.Math.Min(e.Length, int.MaxValue);
        if (!_declaredLive)
            _isLive = false;
    }

    private void OnVout(object? sender, MediaPlayerVoutEventArgs e)
    {
        if (e.Count > 0)
            _sizeKnown = true;
    }

    private void Fail(string reason)
    {
        LumoraLogger.Warn($"LibVlcBackend: {reason} ({_source})");
        Failed?.Invoke(reason);
    }

    private static bool LooksRemote(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;
        int scheme = source.IndexOf("://", StringComparison.Ordinal);
        if (scheme <= 0) return false;
        return !source.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiveScheme(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;
        int scheme = source.IndexOf("://", StringComparison.Ordinal);
        if (scheme <= 0) return false;
        string prefix = source.Substring(0, scheme);
        foreach (var live in LiveSchemes)
        {
            if (string.Equals(prefix, live, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
#endif
