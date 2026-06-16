// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public class RenderTexture : TextureAsset
{
    public bool RenderEnabled { get; private set; } = true;
    public int RenderWidth { get; private set; } = 1024;
    public int RenderHeight { get; private set; } = 1024;
    public int CullMask { get; private set; }
    public color ClearColor { get; private set; } = new color(0f, 0f, 0f, 0f);
    public float3 CameraPosition { get; private set; }
    public floatQ CameraRotation { get; private set; } = floatQ.Identity;
    public float OrthographicSize { get; private set; } = 1f;

    public void Configure(
        int width,
        int height,
        int cullMask,
        color clearColor,
        float3 cameraPosition,
        floatQ cameraRotation,
        float orthographicSize)
    {
        RenderWidth = width < 1 ? 1 : width;
        RenderHeight = height < 1 ? 1 : height;
        CullMask = cullMask;
        ClearColor = clearColor;
        CameraPosition = cameraPosition;
        CameraRotation = cameraRotation;
        OrthographicSize = orthographicSize <= 0f ? 1f : orthographicSize;
        Version++;

        (Hook as IRenderTextureAssetHook)?.Configure(
            RenderWidth, RenderHeight, CullMask, ClearColor,
            CameraPosition, CameraRotation, OrthographicSize);
    }

    /// <summary>
    /// Pause or resume the offscreen render. The viewport and its texture stay
    /// alive so consumers keep a valid (stale) image while paused.
    /// </summary>
    public void SetRenderEnabled(bool enabled)
    {
        if (RenderEnabled == enabled) return;
        RenderEnabled = enabled;
        (Hook as IRenderTextureAssetHook)?.SetRenderEnabled(enabled);
    }
}
