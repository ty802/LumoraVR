// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The mapping between curve space (seconds across, value up) and the graph's canvas rect. The bottom
// strip is the time ruler: the plot proper starts above it. Shared by the drawing and the dragging so a
// key is hit exactly where it is drawn.
public readonly struct CurveView
{
    public const float RulerHeight = 28f;

    public readonly Rect Full;
    public readonly Rect Plot;
    public readonly float T0;
    public readonly float T1;
    public readonly float V0;
    public readonly float V1;

    public CurveView(in Rect full, float2 time, float2 value)
    {
        Full = full;
        float ruler = System.Math.Min(RulerHeight, full.height * 0.5f);
        Plot = Rect.FromMinMax(new float2(full.xMin, full.yMin + ruler), full.Max);
        T0 = time.x;
        T1 = time.y > time.x + 1e-5f ? time.y : time.x + 1e-5f;
        V0 = value.x;
        V1 = value.y > value.x + 1e-6f ? value.y : value.x + 1e-6f;
    }

    public bool IsValid => Plot.width > 1f && Plot.height > 1f;

    public float PixelsPerSecond => Plot.width / (T1 - T0);

    public float PixelsPerUnit => Plot.height / (V1 - V0);

    public float X(float time) => Plot.xMin + (time - T0) * PixelsPerSecond;

    public float Y(float value) => Plot.yMin + (value - V0) * PixelsPerUnit;

    public float2 Point(float time, float value) => new(X(time), Y(value));

    public float Time(float x) => T0 + (x - Plot.xMin) / PixelsPerSecond;

    public float Value(float y) => V0 + (y - Plot.yMin) / PixelsPerUnit;

    public bool InRuler(in float2 point) => point.y < Plot.yMin;
}
