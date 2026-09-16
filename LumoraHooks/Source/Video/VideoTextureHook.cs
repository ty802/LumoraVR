// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Media;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks.Video;

// A video is a texture. Everything downstream binds it through IGodotTexture exactly like a PNG,
// and the only thing that makes it different is that the pixels arrive from a decoder instead of a
// file. The hook owns the texture, picks the decoder, and is the ONE place on the main thread where
// a decoded frame becomes a GPU upload.
//
// Threading: the core may call Load/UpdateState/SetVolume from the world thread, so everything that
// touches Godot is deferred onto the main thread and the per-frame work happens in a process node's
// _Process. Decoder threads never get near a Godot object; they publish through the backend's Tick,
// which only ever runs here. -xlinka
[ImplementableHook(typeof(VideoTexture))]
public sealed partial class VideoTextureHook : AssetHook, IVideoTextureAssetHook, IGodotTexture, IVideoFrameTarget
{
    // A stream that has produced nothing at all by now is not buffering, it is broken, and a player
    // staring at a blank panel deserves to be told which.
    private const double FirstFrameTimeoutSeconds = 20.0;

    private readonly object _requestSync = new();

    private ImageTexture? _imageTexture;
    private Image? _image;
    private Texture2D? _presented;

    private IVideoDecoderBackend? _backend;
    private VideoProcessNode? _processNode;

    private LoadRequest? _pendingRequest;
    private double _loadStartSeconds;
    private bool _sawFirstFrame;
    private bool _sawReady;
    private bool _timedOut;
    private volatile bool _unloaded;

    private bool _wantPlay = true;
    private bool _wantLoop;
    private double _wantPosition;
    private float _wantSpeed = 1f;
    private float _volume = 1f;
    private int? _audioTrack;
    private IVideoAudioSink? _sink;

    // Kept so a material hook can ask how this video wants to be sampled. The ImageTexture itself
    // has no wrap or filter state; sampling is decided by the material that binds it. -xlinka
    public VideoFilterMode Filter { get; private set; } = VideoFilterMode.Bilinear;
    public int AnisotropicLevel { get; private set; }
    public TextureWrapMode WrapU { get; private set; } = TextureWrapMode.Clamp;
    public TextureWrapMode WrapV { get; private set; } = TextureWrapMode.Clamp;

    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }

    public Texture2D? GodotTexture2D => _presented != null && GodotObject.IsInstanceValid(_presented) ? _presented : null;

    public bool IsValid => GodotTexture2D != null;

    public float CurrentClockError => _backend?.CurrentClockError ?? 0f;
    public VideoStats Stats => _backend?.Stats ?? default;

    public event Action<VideoReadyInfo>? Ready;
    public event Action? TextureChanged;
    public event Action<string>? Failed;

    private sealed record LoadRequest(string Source, string? AudioSource, string? ForceEngine, string? Mime, int? AudioTrackIndex, bool IsStream, bool IsLive, int MaxWidth, int MaxHeight);

    public void Load(string source, string? audioSource, string? forceEngine, string? mime, int? audioTrackIndex, bool isStream, bool isLive, int maxWidth, int maxHeight)
    {
        lock (_requestSync)
        {
            _unloaded = false;
            _pendingRequest = new LoadRequest(source ?? string.Empty, audioSource, forceEngine, mime, audioTrackIndex, isStream, isLive, maxWidth, maxHeight);
        }
        _audioTrack = audioTrackIndex;
        Callable.From(StartPendingLoad).CallDeferred();
    }

    public void UpdateState(bool play, bool loop, double position, float speed)
    {
        _wantPlay = play;
        _wantLoop = loop;
        _wantPosition = position;
        _wantSpeed = speed;
        _backend?.UpdateState(play, loop, position, speed);
    }

    public void SetVolume(float volume)
    {
        _volume = volume;
        _backend?.SetVolume(volume);
    }

    public void SetAudioTrack(int? index)
    {
        _audioTrack = index;
        _backend?.SetAudioTrack(index);
    }

    public void SetAudioSink(IVideoAudioSink? sink)
    {
        _sink = sink;
        _backend?.SetAudioSink(sink);
    }

    public void SetTextureProperties(VideoFilterMode filter, int anisotropicLevel, TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
        Filter = filter;
        AnisotropicLevel = anisotropicLevel;
        WrapU = wrapU;
        WrapV = wrapV;
    }

    // ITextureAssetHook: a provider may push a poster or a placeholder into the same texture the
    // decoder writes to, which is how a video panel can show something before the first frame lands.
    public void UploadData(byte[] pixels, int width, int height, bool hasMipmaps)
    {
        if (pixels == null || pixels.Length == 0) return;
        Callable.From(() => BuildFromUpload(pixels, width, height)).CallDeferred();
    }

    // Nothing to wait on: the first frame of a video is asynchronous by nature and the asset is
    // usable (as an empty texture) the moment it exists.
    public System.Threading.Tasks.Task WaitForUploadAsync() => System.Threading.Tasks.Task.CompletedTask;

    public void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
        WrapU = wrapU;
        WrapV = wrapV;
    }

    public override void Unload()
    {
        lock (_requestSync)
        {
            _unloaded = true;
            _pendingRequest = null;
        }

        var backend = _backend;
        var node = _processNode;
        var texture = _imageTexture;
        _backend = null;
        _processNode = null;
        _imageTexture = null;
        _image = null;
        _presented = null;

        Callable.From(() =>
        {
            backend?.Stop();
            backend?.Dispose();
            if (node != null && GodotObject.IsInstanceValid(node))
                node.QueueFree();
            texture?.Dispose();
        }).CallDeferred();
    }

    // MAIN THREAD

    private void StartPendingLoad()
    {
        LoadRequest? request;
        lock (_requestSync)
        {
            // Unload can land between the request and this deferred call; starting a decoder for an
            // asset that is already gone would leak the whole pipeline. -xlinka
            request = _unloaded ? null : _pendingRequest;
            _pendingRequest = null;
        }
        if (request == null) return;

        var previous = _backend;
        _backend = null;
        if (previous != null)
        {
            previous.Ready -= OnBackendReady;
            previous.Failed -= OnBackendFailed;
            previous.Stop();
            previous.Dispose();
        }

        _sawFirstFrame = false;
        _sawReady = false;
        _timedOut = false;
        _loadStartSeconds = global::Godot.Time.GetTicksMsec() / 1000.0;

        var host = EnsureProcessNode();
        if (host == null)
        {
            OnBackendFailed("no scene tree to host the decoder");
            return;
        }

        var backend = CreateBackend(request, host);
        if (backend == null)
            return;

        backend.Ready += OnBackendReady;
        backend.Failed += OnBackendFailed;
        _backend = backend;

        backend.SetAudioSink(_sink);
        backend.SetVolume(_volume);
        backend.UpdateState(_wantPlay, _wantLoop, _wantPosition, _wantSpeed);
        backend.Open(request.Source, request.AudioSource, _audioTrack, request.IsStream, request.IsLive, request.MaxWidth, request.MaxHeight);
    }

    // Engine choice, in one place so the rule is readable:
    //   .ogv/.ogg      -> the built-in Theora decoder, whatever was asked for
    //   anything else  -> libVLC
    //   libVLC missing -> Theora for an ogg, a named failure for everything else
    // A forced "libVLC" beats the extension, because someone forcing it wants the reason when it is
    // not there rather than a silent downgrade. -xlinka
    private IVideoDecoderBackend? CreateBackend(LoadRequest request, Node host)
    {
        bool isOgg = HasOggExtension(request.Source);
        bool wantsLibVlc = request.ForceEngine == MediaServices.EngineLibVlc || !isOgg;

        if (!wantsLibVlc)
            return new TheoraBackend(host);

#if GODOT_PC
        if (LibVlcRuntime.IsAvailable)
            return new LibVlcBackend();
        string reason = LibVlcRuntime.UnavailableReason ?? "not initialised";
#else
        const string reason = "this platform has no libVLC build";
#endif
        if (isOgg)
            return new TheoraBackend(host);

        OnBackendFailed($"libVLC unavailable: {reason}");
        return null;
    }

    private static bool HasOggExtension(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;
        string path = source;
        int query = path.IndexOfAny(new[] { '?', '#' });
        if (query >= 0)
            path = path.Substring(0, query);
        return path.EndsWith(".ogv", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    private Node? EnsureProcessNode()
    {
        if (_processNode != null && GodotObject.IsInstanceValid(_processNode))
            return _processNode;

        if (global::Godot.Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return null;

        // Parented to the scene tree root rather than to a world: an asset outlives any one world and
        // a decoder that stops with a backgrounded world would desync the moment you looked away.
        _processNode = new VideoProcessNode(this)
        {
            Name = "VideoDecoder",
            ProcessMode = Node.ProcessModeEnum.Always,
        };
        tree.Root.AddChild(_processNode);
        return _processNode;
    }

    internal void Tick()
    {
        var backend = _backend;
        if (backend == null) return;

        try
        {
            backend.Tick(this);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"VideoTextureHook: decoder tick failed: {ex.Message}");
        }

        if (_timedOut || _sawFirstFrame || _sawReady) return;
        if (global::Godot.Time.GetTicksMsec() / 1000.0 - _loadStartSeconds < FirstFrameTimeoutSeconds) return;

        _timedOut = true;
        OnBackendFailed($"no video after {FirstFrameTimeoutSeconds:0} s");
    }

    void IVideoFrameTarget.SubmitFrame(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        int expected = width * height * 4;
        if (rgba.Length < expected) return;

        // Godot refuses anything but the exact byte count, and a decoder buffer is allowed to run on
        // past the picture (coded height below the visible rows), so only the picture is handed over.
        var pixels = new ReadOnlySpan<byte>(rgba, 0, expected);
        try
        {
            if (_image == null || _image.GetWidth() != width || _image.GetHeight() != height)
            {
                _image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
                Adopt(_image);
            }
            else
            {
                // Same resource, new contents. Every material that bound this texture holds the
                // resource object, so pushing pixels through it is what keeps a playing video from
                // needing a rebind on every frame (same rule as a progressive texture load). -xlinka
                _image.SetData(width, height, false, Image.Format.Rgba8, pixels);
                _imageTexture!.Update(_image);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"VideoTextureHook: frame upload failed: {ex.Message}");
            return;
        }

        VideoWidth = width;
        VideoHeight = height;
        _sawFirstFrame = true;
        Present(_imageTexture);
    }

    void IVideoFrameTarget.PresentTexture(Texture2D? texture, int width, int height)
    {
        if (texture == null || !GodotObject.IsInstanceValid(texture)) return;
        VideoWidth = width;
        VideoHeight = height;
        _sawFirstFrame = true;
        Present(texture);
    }

    // TextureChanged means "bind a different object", not "new pixels". Pixels go into the same
    // ImageTexture in place, so a material that bound it once keeps seeing every frame. What a
    // material cannot see on its own is the first frame turning a not-yet-valid texture into a valid
    // one: Ready fires as soon as the decoder knows the size, before anything was decoded, and a
    // material that bound at that moment is holding its white fallback until told to look again. -xlinka
    private void Present(Texture2D? texture)
    {
        bool changed = !ReferenceEquals(_presented, texture);
        _presented = texture;
        if (changed)
            TextureChanged?.Invoke();
    }

    private void BuildFromUpload(byte[] pixels, int width, int height)
    {
        try
        {
            Image image;
            if (width > 0 && height > 0 && pixels.Length == width * height * 4)
            {
                image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
            }
            else
            {
                image = new Image();
                if (image.LoadPngFromBuffer(pixels) != Error.Ok && image.LoadJpgFromBuffer(pixels) != Error.Ok)
                {
                    LumoraLogger.Warn("VideoTextureHook: UploadData could not decode the supplied image");
                    return;
                }
            }

            _image = image;
            Adopt(image);
            VideoWidth = image.GetWidth();
            VideoHeight = image.GetHeight();
            Present(_imageTexture);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"VideoTextureHook: UploadData failed: {ex.Message}");
        }
    }

    private void Adopt(Image image)
    {
        if (_imageTexture == null || !GodotObject.IsInstanceValid(_imageTexture))
            _imageTexture = ImageTexture.CreateFromImage(image);
        else
            _imageTexture.SetImage(image);
    }

    private void OnBackendReady(VideoReadyInfo info)
    {
        _sawReady = true;
        if (info.Width > 0) VideoWidth = info.Width;
        if (info.Height > 0) VideoHeight = info.Height;
        Ready?.Invoke(info);
    }

    private void OnBackendFailed(string reason) => Failed?.Invoke(reason);

    private sealed partial class VideoProcessNode : Node
    {
        private readonly VideoTextureHook _hook;
        public VideoProcessNode(VideoTextureHook hook) => _hook = hook;
        public VideoProcessNode() => _hook = null!;

        public override void _Process(double delta) => _hook?.Tick();
    }
}
