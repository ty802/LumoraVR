// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

// A picture that lives somewhere else, shown here: another world's camera feed on a dash widget. Asked for
// on every update, so a source that comes, goes or changes size is followed. Nothing about it is sent; the
// picture belongs to the machine that drew it. -xlinka
[ComponentCategory("Hidden")]
public sealed class TextureLink : AssetProvider<TextureAsset>
{
    public Func<TextureAsset?>? Source { get; set; }

    private TextureAsset? _shown;
    private int _width;
    private int _height;

    public override TextureAsset Asset => _shown!;
    public override bool IsAssetAvailable => _shown != null;

    protected override void FreeAsset()
    {
        if (_shown == null)
            return;
        _shown = null;
        AssetRemoved();
    }

    protected override void UpdateAsset() => Follow();

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        Follow();
    }

    private void Follow()
    {
        TextureAsset? next;
        try
        {
            next = Source?.Invoke();
        }
        catch
        {
            next = null;
        }
        int width = next is RenderTexture render ? render.RenderWidth : next?.Width ?? 0;
        int height = next is RenderTexture renderHigh ? renderHigh.RenderHeight : next?.Height ?? 0;
        if (ReferenceEquals(next, _shown))
        {
            if (next != null && (width != _width || height != _height))
            {
                _width = width;
                _height = height;
                AssetUpdated();
            }
            return;
        }
        bool had = _shown != null;
        _shown = next;
        _width = width;
        _height = height;
        if (next == null)
            AssetRemoved();
        else if (had)
            AssetUpdated();
        else
            AssetCreated();
    }
}
