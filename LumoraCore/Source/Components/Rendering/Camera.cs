// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

[ComponentCategory("Rendering")]
public class Camera : ImplementableComponent
{
    [Group("Projection")]
    public readonly Sync<ProjectionType> Projection = new();

    // degrees; used in Perspective mode
    public readonly Sync<float> FieldOfView = new();

    // height in world units; used in Orthographic mode
    public readonly Sync<float> OrthographicSize = new();

    public readonly Sync<float> NearClip = new();

    public readonly Sync<float> FarClip = new();

    [Group("Clear")]
    public readonly Sync<ClearMode> Clear = new();

    // used when Clear = Color
    public readonly Sync<color> BackgroundColor = new();

    // null = render to screen
    [Group("Output")]
    public readonly AssetRef<RenderTexture> TargetTexture = new();

    // lower depth renders first
    public readonly Sync<int> Depth = new();

    public readonly Sync<int> CullingMask = new();

    [Group("Rendering")]
    public readonly Sync<bool> RenderShadows = new();

    public readonly Sync<bool> UseOcclusionCulling = new();

    public readonly Sync<bool> AllowHDR = new();

    public readonly Sync<bool> AllowMSAA = new();

    // normalized 0-1, as (x, y, width, height)
    public readonly Sync<float4> ViewportRect = new();

    public readonly Sync<bool> SelectiveRender = new();

    public readonly Sync<bool> RenderPostProcessing = new();

    // fed each frame by the platform layer (only it knows the real window size); fallback for
    // AspectRatio when no render target is set; defaults to a 16:9 sentinel until updated
    public float ScreenAspect { get; set; } = 16f / 9f;

    public override void OnInit()
    {
        base.OnInit();

        // Projection = ProjectionType.Perspective (enum 0, C# default, skip)
        FieldOfView.Value        = 60f;
        OrthographicSize.Value   = 5f;
        NearClip.Value           = 0.05f;
        FarClip.Value            = 1000f;
        // Clear = ClearMode.Skybox (enum 0, C# default, skip)
        BackgroundColor.Value    = new color(0.2f, 0.2f, 0.2f, 1f);
        // TargetTexture = default (C# default null, skip)
        // Depth = 0 (C# default, skip)
        CullingMask.Value        = -1; // All layers
        RenderShadows.Value      = true;
        UseOcclusionCulling.Value = true;
        AllowHDR.Value           = true;
        AllowMSAA.Value          = true;
        ViewportRect.Value       = new float4(0f, 0f, 1f, 1f);
        // SelectiveRender = false (C# default, skip)
        RenderPostProcessing.Value = true;
    }

    // derived from the render target when one is set, otherwise the platform-fed ScreenAspect
    public float AspectRatio
    {
        get
        {
            var target = TargetTexture.Target?.Asset;
            if (target != null && target.RenderHeight > 0)
                return (float)target.RenderWidth / target.RenderHeight;

            return ScreenAspect > 0f ? ScreenAspect : 16f / 9f;
        }
    }

    public bool IsRenderTexture
    {
        get { return TargetTexture.Target != null; }
    }
}

public enum ProjectionType
{
    Perspective,
    Orthographic
}

public enum ClearMode
{
    Skybox,
    Color,
    DepthOnly,
    Nothing
}
