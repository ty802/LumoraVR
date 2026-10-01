// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Desktop;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks.Desktop;

// One strip of a monitor, or its pointer, as a Godot texture. Each frame it asks the capture whether its
// layer changed and, only if so, pushes the new pixels into the SAME ImageTexture, so the UI material that
// bound it once keeps showing every change without a rebind. A layer that did not change costs a lock and
// a compare. -xlinka
[ImplementableHook(typeof(DesktopTexture))]
public sealed partial class DesktopTextureHook : AssetHook, IDesktopTextureAssetHook, IGodotTexture
{
    private readonly object _gate = new();
    private IDesktopFrameSource? _source;
    private int _layer;
    private int _version;
    private bool _unloaded;

    private byte[] _pixels = Array.Empty<byte>();
    private Image? _image;
    private ImageTexture? _texture;
    private ImageTexture? _presented;
    private PumpNode? _node;

    public Texture2D? GodotTexture2D => _presented != null && GodotObject.IsInstanceValid(_presented) ? _presented : null;

    public bool IsValid => GodotTexture2D != null;

    public event Action? TextureChanged;

    private int _uploads;
    public int Uploads => Volatile.Read(ref _uploads);

    public void Bind(IDesktopFrameSource? source, int layer)
    {
        lock (_gate)
        {
            if (ReferenceEquals(source, _source) && layer == _layer)
                return;
            _source = source;
            _layer = layer;
            _version = 0;
            _unloaded = false;
        }
        if (source != null)
            Callable.From(EnsureNode).CallDeferred();
    }

    // The picture is the capture's to supply; there is nothing to take from outside.
    public void UploadData(byte[] pixels, int width, int height, bool hasMipmaps)
    {
    }

    public void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
    }

    public System.Threading.Tasks.Task WaitForUploadAsync() => System.Threading.Tasks.Task.CompletedTask;

    public override void Unload()
    {
        lock (_gate)
        {
            _source = null;
            _unloaded = true;
        }
        var node = _node;
        var texture = _texture;
        _node = null;
        _texture = null;
        _presented = null;
        _image = null;
        Callable.From(() =>
        {
            if (node != null && GodotObject.IsInstanceValid(node))
                node.QueueFree();
            texture?.Dispose();
        }).CallDeferred();
    }

    // MAIN THREAD

    private void EnsureNode()
    {
        lock (_gate)
        {
            if (_unloaded)
                return;
        }
        if (_node != null && GodotObject.IsInstanceValid(_node))
            return;
        if (global::Godot.Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return;
        _node = new PumpNode(this) { Name = "DesktopTexture", ProcessMode = Node.ProcessModeEnum.Always };
        tree.Root.AddChild(_node);
    }

    internal void Tick()
    {
        IDesktopFrameSource? source;
        int layer, version;
        lock (_gate)
        {
            source = _source;
            layer = _layer;
            version = _version;
        }
        if (source == null || !source.TryCopy(layer, ref version, ref _pixels, out int width, out int height))
            return;
        lock (_gate)
        {
            if (!ReferenceEquals(source, _source) || layer != _layer)
                return;
            _version = version;
        }
        Upload(width, height);
    }

    private void Upload(int width, int height)
    {
        try
        {
            if (_image == null || _image.GetWidth() != width || _image.GetHeight() != height)
            {
                _image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, _pixels);
                if (_texture == null || !GodotObject.IsInstanceValid(_texture))
                    _texture = ImageTexture.CreateFromImage(_image);
                else
                    _texture.SetImage(_image);
            }
            else
            {
                _image.SetData(width, height, false, Image.Format.Rgba8, _pixels);
                _texture!.Update(_image);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"DesktopTextureHook: upload failed: {ex.Message}");
            return;
        }

        Interlocked.Increment(ref _uploads);
        if (!ReferenceEquals(_presented, _texture))
        {
            _presented = _texture;
            TextureChanged?.Invoke();
        }
    }

    private sealed partial class PumpNode : Node
    {
        private readonly DesktopTextureHook _hook;
        public PumpNode(DesktopTextureHook hook) => _hook = hook;
        public PumpNode() => _hook = null!;

        public override void _Process(double delta) => _hook?.Tick();
    }
}
