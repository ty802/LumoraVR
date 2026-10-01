// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Desktop;

namespace Lumora.Core.Assets;

// One layer of a captured monitor as a texture: a horizontal strip of the picture, or the mouse pointer.
// Dynamic like VideoTexture; the hook owns the GPU texture and fills it from the capture. -xlinka
public sealed class DesktopTexture : TextureAsset
{
    private IDesktopTextureAssetHook? _desktopHook;

    // Raised on the main thread when the texture object changed and bindings must look again.
    public event Action<DesktopTexture>? FrameChanged;

    private IDesktopTextureAssetHook? DesktopHook
    {
        get
        {
            if (_desktopHook == null && Hook is IDesktopTextureAssetHook hook)
            {
                _desktopHook = hook;
                hook.TextureChanged += OnHookTextureChanged;
            }
            return _desktopHook;
        }
    }

    public void Bind(IDesktopFrameSource? source, int layer) => DesktopHook?.Bind(source, layer);

    // See IDesktopTextureAssetHook.Uploads.
    public int Uploads => DesktopHook?.Uploads ?? 0;

    private void OnHookTextureChanged()
    {
        Version++;
        FrameChanged?.Invoke(this);
    }

    public override void Unload()
    {
        if (_desktopHook != null)
        {
            _desktopHook.TextureChanged -= OnHookTextureChanged;
            _desktopHook.Bind(null, 0);
            _desktopHook = null;
        }
        base.Unload();
    }
}
