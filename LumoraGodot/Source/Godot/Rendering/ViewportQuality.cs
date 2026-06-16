// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;

namespace Lumora.Source.Godot.Rendering;

public static class ViewportQuality
{
    private const float DesktopXrRenderTargetMultiplier = 2.0f;
    private const float StandaloneXrRenderTargetMultiplier = 1.35f;
    private const float DesktopXrSupersampleScale = 1.5f;
    private const float StandaloneXrSupersampleScale = 1.15f;

    public static void ConfigureOpenXRBeforeInitialize(OpenXRInterface? xrInterface, Action<string>? log = null)
    {
        if (xrInterface == null)
            return;

        if (xrInterface.IsInitialized())
        {
            log?.Invoke("OpenXR quality: render target multiplier unchanged because the session is already initialized.");
            return;
        }

        var multiplier = OS.HasFeature("android")
            ? StandaloneXrRenderTargetMultiplier
            : DesktopXrRenderTargetMultiplier;

        xrInterface.RenderTargetSizeMultiplier = multiplier;
        log?.Invoke($"OpenXR quality: render target multiplier set to {multiplier:0.##} before initialization.");
    }

    public static void ConfigureOpenXRAfterInitialize(OpenXRInterface? xrInterface, Viewport? viewport, Action<string>? log = null)
    {
        if (xrInterface == null || !xrInterface.IsInitialized())
        {
            log?.Invoke("OpenXR quality: skipped because OpenXR is not initialized.");
            return;
        }

        ApplyXrViewportDefaults(viewport);

        if (xrInterface == null)
            return;

        try
        {
            xrInterface.FoveationDynamic = false;
            xrInterface.FoveationLevel = 0;
            xrInterface.FoveationWithSubsampledImages = false;
            xrInterface.VrsStrength = 0.0f;
            xrInterface.VrsMinRadius = 1.0f;
        }
        catch (Exception ex)
        {
            log?.Invoke($"OpenXR quality: foveation/VRS override skipped ({ex.Message}).");
        }

        if (viewport is not SubViewport subViewport)
            return;

        try
        {
            var targetSize = xrInterface.GetRenderTargetSize();
            var width = Math.Max(1, (int)Math.Ceiling(targetSize.X));
            var height = Math.Max(1, (int)Math.Ceiling(targetSize.Y));
            var size = new Vector2I(width, height);

            if (subViewport.Size != size)
            {
                subViewport.Size = size;
                log?.Invoke($"OpenXR quality: XR viewport resized to {width}x{height}.");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"OpenXR quality: render target size query skipped ({ex.Message}).");
        }
    }

    public static void ApplyXrViewportDefaults(Viewport? viewport)
    {
        if (viewport == null)
            return;

        try
        {
            viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
            viewport.Scaling3DScale = OS.HasFeature("android")
                ? StandaloneXrSupersampleScale
                : DesktopXrSupersampleScale;
            viewport.FsrSharpness = 1.0f;
            viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
            viewport.Msaa3D = OS.HasFeature("android") ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Msaa8X;
            viewport.UseTaa = false;
            viewport.UseDebanding = true;
            viewport.TextureMipmapBias = 0.25f;
            viewport.AnisotropicFilteringLevel = OS.HasFeature("android")
                ? Viewport.AnisotropicFiltering.Anisotropy8X
                : Viewport.AnisotropicFiltering.Anisotropy16X;
            viewport.VrsMode = Viewport.VrsModeEnum.Disabled;
            viewport.VrsUpdateMode = Viewport.VrsUpdateModeEnum.Disabled;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"OpenXR quality: viewport defaults skipped ({ex.Message}).");
        }
    }

    public static void ApplyRenderScale(Viewport viewport, float scale)
    {
        if (viewport == null)
            return;

        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = scale;
    }

    public static void ApplyAntiAliasing(Viewport viewport, int index)
    {
        if (viewport == null)
            return;

        switch (index)
        {
            case 0:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                viewport.Msaa3D = Viewport.Msaa.Disabled;
                viewport.UseTaa = false;
                break;
            case 1:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                viewport.Msaa3D = Viewport.Msaa.Disabled;
                viewport.UseTaa = false;
                break;
            case 2:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                viewport.Msaa3D = Viewport.Msaa.Msaa2X;
                viewport.UseTaa = false;
                break;
            case 3:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                viewport.Msaa3D = Viewport.Msaa.Msaa4X;
                viewport.UseTaa = false;
                break;
            case 4:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                viewport.Msaa3D = Viewport.Msaa.Disabled;
                viewport.UseTaa = true;
                break;
        }
    }
}
