// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;

namespace Lumora.Godot.Debug;

#nullable enable

/// <summary>
/// Tracy-style frame-time strip: every frame is a vertical bar whose height is its frame time and whose colour is
/// graded against the frame budget (green under 60fps, amber up to 30fps, red beyond). A reference line marks the
/// target budget so spikes stand out at a glance. Feeds from the PERF telemetry's frame time. -xlinka
/// </summary>
public partial class ProfilerGraph : Control
{
    private readonly List<float> _frameMs = new();

    [Export] public int MaxSamples { get; set; } = 240;
    [Export] public float TargetMs { get; set; } = 16.6667f;   // 60fps budget
    [Export] public Color BackgroundColor { get; set; } = new(0.045f, 0.045f, 0.06f, 1f);
    [Export] public Color GridColor { get; set; } = new(0.2f, 0.2f, 0.28f, 0.35f);
    [Export] public Color TargetLineColor { get; set; } = new(0.4f, 0.8f, 1f, 0.7f);
    [Export] public Color GoodColor { get; set; } = new(0.3f, 0.9f, 0.45f, 1f);
    [Export] public Color WarnColor { get; set; } = new(1f, 0.85f, 0.25f, 1f);
    [Export] public Color BadColor { get; set; } = new(1f, 0.35f, 0.35f, 1f);

    public override void _Ready()
    {
        ClipContents = true;
        CustomMinimumSize = new Vector2(0, 150);
    }

    public void AddFrame(float frameMs)
    {
        if (frameMs < 0f || float.IsNaN(frameMs) || float.IsInfinity(frameMs))
        {
            return;
        }

        _frameMs.Add(frameMs);
        if (_frameMs.Count > MaxSamples)
        {
            _frameMs.RemoveRange(0, _frameMs.Count - MaxSamples);
        }
        QueueRedraw();
    }

    public void ClearSamples()
    {
        _frameMs.Clear();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var full = new Rect2(Vector2.Zero, Size);
        DrawRect(full, BackgroundColor, true);

        if (Size.X < 8f || Size.Y < 8f)
        {
            return;
        }

        const float pad = 8f;
        var graph = new Rect2(
            pad, pad,
            Mathf.Max(1f, Size.X - pad * 2f),
            Mathf.Max(1f, Size.Y - pad * 2f));

        // Vertical scale: at least 2x the budget so the target line sits mid-graph, but grow to fit spikes.
        float maxMs = Mathf.Max(TargetMs * 2f, 1f);
        foreach (var ms in _frameMs)
        {
            if (ms > maxMs) maxMs = ms;
        }

        // Horizontal grid + ms labels
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            float y = graph.Position.Y + graph.Size.Y * t;
            DrawLine(new Vector2(graph.Position.X, y), new Vector2(graph.End.X, y), GridColor, 1f);
        }

        // Budget reference line
        float targetY = graph.End.Y - Mathf.Clamp(TargetMs / maxMs, 0f, 1f) * graph.Size.Y;
        DrawLine(new Vector2(graph.Position.X, targetY), new Vector2(graph.End.X, targetY), TargetLineColor, 1f);

        if (_frameMs.Count == 0)
        {
            return;
        }

        // One vertical bar per frame, newest on the right, coloured by how far over budget it ran.
        float barSlot = graph.Size.X / Mathf.Max(1, _frameMs.Count);
        float barWidth = Mathf.Max(1f, barSlot - 1f);
        float warnMs = TargetMs * 2f; // 30fps

        for (int i = 0; i < _frameMs.Count; i++)
        {
            float ms = _frameMs[i];
            float h = Mathf.Clamp(ms / maxMs, 0f, 1f) * graph.Size.Y;
            float x = graph.Position.X + barSlot * i;
            float y = graph.End.Y - h;

            Color c = ms <= TargetMs ? GoodColor : ms <= warnMs ? WarnColor : BadColor;
            DrawRect(new Rect2(x, y, barWidth, h), c, true);
        }
    }
}
