// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components;

[ComponentCategory("Utility")]
public class PerformanceMetrics : Component
{
    public readonly Sync<float> FPS;
    public readonly Sync<float> AverageFPS;
    public readonly Sync<float> FrameTime;

    public PerformanceMetrics()
    {
        FPS = new Sync<float>(this, 0f);
        AverageFPS = new Sync<float>(this, 0f);
        FrameTime = new Sync<float>(this, 0f);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var metrics = Engine.Current?.Metrics;
        if (metrics == null)
            return;

        FPS.Value = (float)metrics.CurrentFPS;
        AverageFPS.Value = (float)metrics.AverageFPS;
        FrameTime.Value = (float)metrics.LastFrameTime;
    }
}
