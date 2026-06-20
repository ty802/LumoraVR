// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

[ImplementableHook(typeof(RenderTexture))]
public class RenderTextureHook : AssetHook, IRenderTextureAssetHook, IGodotTexture
{
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;

    private bool _renderEnabled = true;
    private int _width = 1024;
    private int _height = 1024;
    private uint _cullMask;
    private Color _clearColor;
    private Vector3 _cameraPosition;
    private Quaternion _cameraRotation = Quaternion.Identity;
    private float _orthoSize = 1f;

    public Texture2D? GodotTexture2D => _viewport != null && GodotObject.IsInstanceValid(_viewport)
        ? _viewport.GetTexture()
        : null;

    public bool IsValid => _viewport != null && GodotObject.IsInstanceValid(_viewport) && _viewport.IsInsideTree();

    public void Configure(
        int width,
        int height,
        int cullMask,
        Lumora.Core.Math.color clearColor,
        Lumora.Core.Math.float3 cameraPosition,
        Lumora.Core.Math.floatQ cameraRotation,
        float orthographicSize)
    {
        _width = System.Math.Max(1, width);
        _height = System.Math.Max(1, height);
        if (cullMask != 0)
            _cullMask = (uint)cullMask;
        _clearColor = new Color(clearColor.r, clearColor.g, clearColor.b, clearColor.a);
        _cameraPosition = new Vector3(cameraPosition.x, cameraPosition.y, cameraPosition.z);
        _cameraRotation = new Quaternion(cameraRotation.x, cameraRotation.y, cameraRotation.z, cameraRotation.w);
        _orthoSize = orthographicSize <= 0f ? 1f : orthographicSize;

        Callable.From(ApplyOnMainThread).CallDeferred();
    }

    private void ApplyOnMainThread()
    {
        var host = WorldManagerHook.Instance?.Root;
        if (host == null || !GodotObject.IsInstanceValid(host))
        {
            LumoraLogger.Warn("RenderTextureHook: no WorldManager root to host the viewport yet");
            return;
        }

        if (_viewport == null || !GodotObject.IsInstanceValid(_viewport))
        {
            _viewport = new SubViewport
            {
                Name = "RenderTexture",
                // Render ON DEMAND (one frame per change via RequestRender) instead of every frame. The
                // captured scene is just the UI canvas, which only changes when its mesh is rebuilt, so
                // re-rendering this full-res supersampled viewport every frame was pure waste. Disabled keeps
                // the last rendered frame on the texture. -xlinka
                RenderTargetUpdateMode = _renderEnabled ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled,
                RenderTargetClearMode = SubViewport.ClearMode.Always,
                OwnWorld3D = false,
                HandleInputLocally = false,
                Disable3D = false,
            };

            _camera = new Camera3D
            {
                Name = "CaptureCamera",
                Projection = Camera3D.ProjectionType.Orthogonal,
                Near = 0.01f,
                Far = 50f,
                PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
            };
            _viewport.AddChild(_camera);
            host.AddChild(_viewport);

            var world = host.GetWorld3D();
            if (world != null)
                _viewport.World3D = world;
        }

        _viewport.Size = new Vector2I(_width, _height);
        _viewport.TransparentBg = _clearColor.A < 1f;

        if (_cullMask != 0)
            _camera.CullMask = _cullMask;
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Size = _orthoSize;
        _camera.Position = _cameraPosition;
        _camera.Quaternion = _cameraRotation;
        _camera.Current = true;
    }

    public void SetRenderEnabled(bool enabled)
    {
        _renderEnabled = enabled;
        Callable.From(ApplyRenderEnabled).CallDeferred();
    }

    private void ApplyRenderEnabled()
    {
        if (_viewport == null || !GodotObject.IsInstanceValid(_viewport)) return;
        // Enabling draws one frame now; subsequent frames come from RequestRender on actual UI changes. -xlinka
        _viewport.RenderTargetUpdateMode = _renderEnabled
            ? SubViewport.UpdateMode.Once
            : SubViewport.UpdateMode.Disabled;
    }

    public void RequestRender()
    {
        if (!_renderEnabled) return;
        Callable.From(ApplyRequestRender).CallDeferred();
    }

    private void ApplyRequestRender()
    {
        if (!_renderEnabled || _viewport == null || !GodotObject.IsInstanceValid(_viewport)) return;
        // UpdateMode.Once renders a single frame then reverts to no-updates on its own, keeping that frame on
        // the texture until the next request. -xlinka
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    public void UploadData(byte[] pixels, int width, int height, bool hasMipmaps) { }

    public void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV) { }

    public override void Unload()
    {
        if (_viewport != null && GodotObject.IsInstanceValid(_viewport))
        {
            _viewport.QueueFree();
        }
        _viewport = null!;
        _camera = null!;
    }
}
