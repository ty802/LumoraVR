// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Assets;

[ComponentCategory("Assets/Textures")]
public sealed class RenderTextureProvider : DynamicAssetProvider<RenderTexture>
{
    public readonly Sync<int> Width;
    public readonly Sync<int> Height;
    public readonly Sync<int> CullMask;
    public readonly Sync<color> ClearColor;
    public readonly Sync<float3> CameraPosition;
    public readonly Sync<floatQ> CameraRotation;
    public readonly Sync<float> OrthographicSize;

    public RenderTextureProvider()
    {
        Width = new Sync<int>(this, 1024);
        Height = new Sync<int>(this, 1024);
        CullMask = new Sync<int>(this, 0);
        ClearColor = new Sync<color>(this, new color(0f, 0f, 0f, 0f));
        CameraPosition = new Sync<float3>(this, float3.Zero);
        CameraRotation = new Sync<floatQ>(this, floatQ.Identity);
        OrthographicSize = new Sync<float>(this, 1f);
    }

    protected override void OnAssetCreated(RenderTexture asset) { }

    protected override void UpdateAsset(RenderTexture asset)
    {
        asset.Configure(
            Width.Value,
            Height.Value,
            CullMask.Value,
            ClearColor.Value,
            CameraPosition.Value,
            CameraRotation.Value,
            OrthographicSize.Value);
    }

    protected override void OnAssetCleared() { }
}
