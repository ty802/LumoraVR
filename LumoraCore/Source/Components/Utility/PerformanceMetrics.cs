// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components;

[ComponentCategory("Utility")]
public class PerformanceMetrics : Component
{
    public readonly Sync<float> FPS;
    public readonly Sync<float> ImmediateFPS;
    public readonly Sync<float> AverageFPS;
    public readonly Sync<float> FrameTime;

    private double _windowTime;
    private int _windowFrames;

    public PerformanceMetrics()
    {
        FPS = new Sync<float>(this, 0f);
        ImmediateFPS = new Sync<float>(this, 0f);
        AverageFPS = new Sync<float>(this, 0f);
        FrameTime = new Sync<float>(this, 0f);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var metrics = Engine.Current?.Metrics;
        if (metrics == null)
            return;

        ImmediateFPS.Value = (float)metrics.CurrentFPS;
        AverageFPS.Value = (float)metrics.AverageFPS;
        FrameTime.Value = (float)metrics.LastFrameTime;

        // Count frames over a fixed window so the displayed value is stable instead of
        // recomputed every frame; refresh roughly twice a second. - xlinka
        _windowFrames++;
        _windowTime += metrics.LastFrameTime;
        if (_windowTime >= 0.5)
        {
            FPS.Value = (float)(_windowFrames / _windowTime);
            _windowFrames = 0;
            _windowTime = 0;
        }
    }
}
