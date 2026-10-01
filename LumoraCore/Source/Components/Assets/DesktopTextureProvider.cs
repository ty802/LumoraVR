// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Desktop;

namespace Lumora.Core.Components.Assets;

// One layer of a monitor as a texture: a strip of the picture, or the pointer (Layer -1).
//
// It captures in userspace and nowhere else. In a shared world every visitor would see their own screen
// on it, and a camera plus a script could read that picture back and send it anywhere; so outside
// userspace it stays an empty texture, whoever put it there. Nothing it shows is ever replicated. -xlinka
[ComponentCategory("Hidden")]
[UserspaceOnly]
public sealed class DesktopTextureProvider : DynamicAssetProvider<DesktopTexture>
{
    // DesktopDisplay.Key of the monitor to show.
    public readonly Sync<string> Display;
    public readonly Sync<int> Layer;

    private DesktopTexture? _hooked;

    public DesktopTextureProvider()
    {
        Display = new Sync<string>(this, string.Empty);
        Layer = new Sync<int>(this, 0);
    }

    protected override void OnAssetCreated(DesktopTexture asset)
    {
        Detach();
        _hooked = asset;
        asset.FrameChanged += HandleFrameChanged;
    }

    protected override void UpdateAsset(DesktopTexture asset)
    {
        asset.Bind(ResolveSource(), Layer.Value);
    }

    protected override void OnAssetCleared() => Detach();

    public override void OnDestroy()
    {
        Detach();
        base.OnDestroy();
    }

    private IDesktopFrameSource? ResolveSource()
    {
        if (!IsExecutionAllowed || !DesktopPlatform.IsSupported)
            return null;
        var world = World;
        if (world == null || world != Lumora.Core.Engine.Current?.WorldManager?.UserspaceWorld)
            return null;
        return DesktopCaptures.Get(Display.Value);
    }

    private void Detach()
    {
        if (_hooked == null)
            return;
        _hooked.FrameChanged -= HandleFrameChanged;
        _hooked = null;
    }

    private void HandleFrameChanged(DesktopTexture asset)
    {
        RunSynchronously(() =>
        {
            if (IsDestroyed || !ReferenceEquals(asset, Asset))
                return;
            AssetUpdated();
        });
    }
}
